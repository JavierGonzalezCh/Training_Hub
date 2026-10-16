using System.Security.Cryptography;
using System.Text;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity;
using GSM.Application.Identity.Abstractions;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GSM.Infrastructure.Identity;

/// <summary>
/// Cambio de email con dos códigos (Identity §3.4 e I6-1): uno al correo nuevo y otro al actual. Vence a los 10 minutos, un solo uso, 5 intentos; se puede pedir
/// uno por minuto y 5 por hora. El hash es un HMAC con la clave de firma, el id de la persona y el email nuevo
/// (un código reutilizado para otro email no sirve). El consumo y el conteo de intentos son <c>UPDATE</c>
/// atómicos: dos confirmaciones simultáneas con el código bueno no pasan las dos.
/// </summary>
public sealed class EmailChangeService(
    UserManager<AppUser> userManager,
    GsmDbContext dbContext,
    IClock clock,
    IOptions<JwtOptions> jwtOptions) : IEmailChangeService
{
    public async Task<EmailChangeIssue> IssueAsync(
        Guid userId, string newEmail, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is not { IsActive: true })
        {
            return new EmailChangeIssue(EmailChangeIssueStatus.AccountUnavailable);
        }

        var email = newEmail.Trim();
        if (string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            return new EmailChangeIssue(EmailChangeIssueStatus.SameEmail);
        }

        var owner = await userManager.FindByEmailAsync(email);
        if (owner is not null && owner.Id != user.Id)
        {
            return new EmailChangeIssue(EmailChangeIssueStatus.EmailTaken);
        }

        var now = clock.UtcNow;
        var hourAgo = now - TimeSpan.FromHours(1);
        var recent = await dbContext.EmailChangeRequests
            .Where(r => r.UserId == user.Id && r.IssuedAt > hourAgo)
            .Select(r => r.IssuedAt)
            .ToListAsync(cancellationToken);

        if (recent.Count >= EmailChangeRequest.MaxPerHour
            || recent.Any(issuedAt => issuedAt > now - EmailChangeRequest.MinInterval))
        {
            return new EmailChangeIssue(EmailChangeIssueStatus.Throttled);
        }

        // Un pedido nuevo anula el anterior: solo vale el último código que llegó al correo.
        await dbContext.EmailChangeRequests
            .Where(r => r.UserId == user.Id && r.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ConsumedAt, now), cancellationToken);

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var currentCode = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        dbContext.EmailChangeRequests.Add(EmailChangeRequest.Issue(
            user.Id, email, Hash(user.Id, email, code), Hash(user.Id, email, currentCode, CurrentPurpose), now));
        await dbContext.SaveChangesAsync(cancellationToken);

        return new EmailChangeIssue(
            EmailChangeIssueStatus.Issued, email, user.FullName, code, user.Email, currentCode);
    }

    public async Task<EmailChangeConfirmation> ConfirmAsync(
        Guid userId, string code, string currentCode, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is not { IsActive: true })
        {
            return new EmailChangeConfirmation(EmailChangeConfirmationStatus.AccountUnavailable);
        }

        var now = clock.UtcNow;
        var current = await dbContext.EmailChangeRequests
            .Where(r => r.UserId == user.Id && r.ConsumedAt == null && r.ExpiresAt > now)
            .OrderByDescending(r => r.IssuedAt)
            .Select(r => new { r.Id, r.NewEmail, r.CodeHash, r.CurrentCodeHash, r.Attempts })
            .FirstOrDefaultAsync(cancellationToken);

        if (current is null || current.Attempts >= EmailChangeRequest.MaxAttempts)
        {
            return new EmailChangeConfirmation(EmailChangeConfirmationStatus.InvalidCode);
        }

        // Los dos códigos deben coincidir: el del correo nuevo (es suyo) y el del actual (la cuenta es suya).
        var matches = CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(current.CodeHash),
                Encoding.ASCII.GetBytes(Hash(user.Id, current.NewEmail, code.Trim())))
            & CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(current.CurrentCodeHash),
                Encoding.ASCII.GetBytes(Hash(user.Id, current.NewEmail, currentCode.Trim(), CurrentPurpose)));

        if (!matches)
        {
            // Cada fallo suma un intento; al quinto el pedido queda agotado y hay que pedir otro código.
            await dbContext.EmailChangeRequests
                .Where(r => r.Id == current.Id && r.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Attempts, r => r.Attempts + 1), cancellationToken);
            await dbContext.EmailChangeRequests
                .Where(r => r.Id == current.Id && r.ConsumedAt == null && r.Attempts >= EmailChangeRequest.MaxAttempts)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ConsumedAt, now), cancellationToken);
            return new EmailChangeConfirmation(EmailChangeConfirmationStatus.InvalidCode);
        }

        // Un solo uso: solo gana la petición que logra marcarlo consumido.
        var consumed = await dbContext.EmailChangeRequests
            .Where(r => r.Id == current.Id && r.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ConsumedAt, now), cancellationToken);
        if (consumed != 1)
        {
            return new EmailChangeConfirmation(EmailChangeConfirmationStatus.InvalidCode);
        }

        var owner = await userManager.FindByEmailAsync(current.NewEmail);
        if (owner is not null && owner.Id != user.Id)
        {
            return new EmailChangeConfirmation(EmailChangeConfirmationStatus.EmailTaken);
        }

        var oldEmail = user.Email ?? string.Empty;
        var usernameFollowsEmail = string.Equals(user.UserName, oldEmail, StringComparison.OrdinalIgnoreCase);

        // SetEmailAsync renueva el sello de seguridad: los enlaces de restablecer o activar pendientes del email
        // viejo dejan de valer. El correo nuevo queda confirmado: acaba de recibir el código.
        var result = await userManager.SetEmailAsync(user, current.NewEmail);
        if (result.Succeeded && usernameFollowsEmail)
        {
            result = await userManager.SetUserNameAsync(user, current.NewEmail);
        }

        if (result.Succeeded)
        {
            user.EmailConfirmed = true;
            result = await userManager.UpdateAsync(user);
        }

        // Un duplicado que ganó la carrera contra el índice único también cae aquí.
        return result.Succeeded
            ? new EmailChangeConfirmation(
                EmailChangeConfirmationStatus.Changed, oldEmail, current.NewEmail, user.FullName)
            : new EmailChangeConfirmation(EmailChangeConfirmationStatus.EmailTaken);
    }

    private const string NewPurpose = "email-change";
    private const string CurrentPurpose = "email-change-current";

    private string Hash(Guid userId, string newEmail, string code, string purpose = NewPurpose)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(jwtOptions.Value.SigningKey));
        return Convert.ToHexString(hmac.ComputeHash(
                Encoding.UTF8.GetBytes($"{purpose}:{userId:N}:{newEmail.ToLowerInvariant()}:{code}")))
            .ToLowerInvariant();
    }
}
