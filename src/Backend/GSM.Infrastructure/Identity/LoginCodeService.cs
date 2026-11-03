using System.Security.Cryptography;
using System.Text;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Logs;
using GSM.Application.Identity;
using GSM.Application.Identity.Abstractions;
using GSM.Domain.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GSM.Infrastructure.Identity;

/// <summary>
/// Código por correo del miembro (Identity §6). Vence a los 10 minutos, un solo uso, 5 intentos; se puede
/// pedir uno por minuto y 5 por hora. El hash es un HMAC con la clave de firma y el id de la persona (un
/// SHA-256 simple de 6 dígitos se rompe por fuerza bruta si la base se filtra). El consumo y el conteo de
/// intentos son <c>UPDATE</c> atómicos: dos peticiones simultáneas con el código bueno no entran las dos.
/// </summary>
public sealed class LoginCodeService(
    UserManager<AppUser> userManager,
    GsmDbContext dbContext,
    IClock clock,
    IOptions<JwtOptions> jwtOptions,
    ILogStore logStore) : ILoginCodeService
{
    public async Task<LoginCodeIssue> IssueAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await FindEligibleAsync(email, cancellationToken);
        if (user is null)
        {
            return new LoginCodeIssue(LoginCodeIssueStatus.NotEligible);
        }

        var now = clock.UtcNow;
        var hourAgo = now - TimeSpan.FromHours(1);
        var recent = await dbContext.LoginCodes
            .Where(c => c.UserId == user.Id && c.IssuedAt > hourAgo)
            .Select(c => c.IssuedAt)
            .ToListAsync(cancellationToken);

        if (recent.Count >= LoginCode.MaxPerHour || recent.Any(issuedAt => issuedAt > now - LoginCode.MinInterval))
        {
            return new LoginCodeIssue(LoginCodeIssueStatus.Throttled);
        }

        // Un código nuevo anula el anterior: solo vale el último que llegó al correo.
        await dbContext.LoginCodes
            .Where(c => c.UserId == user.Id && c.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), cancellationToken);

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        dbContext.LoginCodes.Add(LoginCode.Issue(user.Id, Hash(user.Id, code), now));
        await dbContext.SaveChangesAsync(cancellationToken);

        return new LoginCodeIssue(LoginCodeIssueStatus.Issued, user.Email, user.FullName, code);
    }

    public async Task<AccountIdentity?> VerifyAsync(
        string email, string code, CancellationToken cancellationToken = default)
    {
        var user = await FindEligibleAsync(email, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var now = clock.UtcNow;
        var current = await dbContext.LoginCodes
            .Where(c => c.UserId == user.Id && c.ConsumedAt == null && c.ExpiresAt > now)
            .OrderByDescending(c => c.IssuedAt)
            .Select(c => new { c.Id, c.CodeHash, c.Attempts })
            .FirstOrDefaultAsync(cancellationToken);

        if (current is null || current.Attempts >= LoginCode.MaxAttempts)
        {
            return null;
        }

        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(current.CodeHash), Encoding.ASCII.GetBytes(Hash(user.Id, code.Trim())));

        if (!matches)
        {
            // Cada fallo suma un intento; al quinto el código queda agotado y hay que pedir otro.
            await dbContext.LoginCodes
                .Where(c => c.Id == current.Id && c.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Attempts, c => c.Attempts + 1), cancellationToken);
            var exhausted = await dbContext.LoginCodes
                .Where(c => c.Id == current.Id && c.ConsumedAt == null && c.Attempts >= LoginCode.MaxAttempts)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), cancellationToken);

            if (exhausted > 0)
            {
                // LG2-4: este fallo agotó el código (quinto intento). Solo el email y el número de intentos; nunca el código.
                logStore.RecordSecurityEvent(new SecurityEventLogEntry(
                    SecurityEventTypes.CodeAttemptsExceeded,
                    $"email={email.Trim()}; intentos={LoginCode.MaxAttempts}"));
            }

            return null;
        }

        // Un solo uso: solo gana la petición que logra marcarlo consumido.
        var consumed = await dbContext.LoginCodes
            .Where(c => c.Id == current.Id && c.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), cancellationToken);

        return consumed == 1
            ? new AccountIdentity(user.Id, user.Email ?? string.Empty, user.FullName, user.IsSuperAdmin, user.MustChangePassword)
            : null;
    }

    /// <summary>Persona activa, que no es el SuperAdmin y tiene un acceso de miembro activo.</summary>
    private async Task<AppUser?> FindEligibleAsync(string email, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email.Trim());
        if (user is not { IsActive: true, IsSuperAdmin: false })
        {
            return null;
        }

        var isMember = await dbContext.GymAccesses
            .AnyAsync(a => a.UserId == user.Id && a.IsActive && a.Role == UserRole.Member, cancellationToken);
        return isMember ? user : null;
    }

    private string Hash(Guid userId, string code)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(jwtOptions.Value.SigningKey));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"login-code:{userId:N}:{code}")))
            .ToLowerInvariant();
    }
}
