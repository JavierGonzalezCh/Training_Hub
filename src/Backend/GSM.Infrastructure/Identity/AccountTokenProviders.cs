using GSM.Application.Common.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace GSM.Infrastructure.Identity;

/// <summary>
/// Proveedor de códigos de un solo uso (restablecer, activar) con vencimiento medido por <see cref="IClock"/>.
/// Hace lo mismo que <c>DataProtectorTokenProvider</c> de ASP.NET Identity (código protegido, atado al usuario,
/// al propósito y a su <c>SecurityStamp</c>: al cambiar la contraseña deja de servir), pero con la vida útil
/// propia de cada enlace y un reloj que los tests pueden mover.
/// </summary>
public abstract class ClockedTokenProvider(IDataProtectionProvider dataProtection, IClock clock, string name)
    : IUserTwoFactorTokenProvider<AppUser>
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("GSM.AccountTokens", name);

    protected abstract TimeSpan Lifespan { get; }

    public async Task<string> GenerateAsync(string purpose, UserManager<AppUser> manager, AppUser user)
    {
        using var stream = new MemoryStream();
        await using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(clock.UtcNow.UtcTicks);
            writer.Write(await manager.GetUserIdAsync(user));
            writer.Write(purpose ?? string.Empty);
            writer.Write(manager.SupportsUserSecurityStamp ? await manager.GetSecurityStampAsync(user) ?? string.Empty : string.Empty);
        }

        return Convert.ToBase64String(_protector.Protect(stream.ToArray()));
    }

    public async Task<bool> ValidateAsync(string purpose, string token, UserManager<AppUser> manager, AppUser user)
    {
        try
        {
            var bytes = _protector.Unprotect(Convert.FromBase64String(token));
            using var reader = new BinaryReader(new MemoryStream(bytes), System.Text.Encoding.UTF8);

            var issuedAt = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
            if (issuedAt + Lifespan < clock.UtcNow)
            {
                return false;
            }

            if (reader.ReadString() != await manager.GetUserIdAsync(user)
                || reader.ReadString() != (purpose ?? string.Empty))
            {
                return false;
            }

            var stamp = reader.ReadString();
            var current = manager.SupportsUserSecurityStamp ? await manager.GetSecurityStampAsync(user) ?? string.Empty : string.Empty;
            return reader.PeekChar() == -1 && stamp == current;
        }
        catch (Exception)
        {
            // Código mal formado, alterado o de otra clave: no es válido, sin más detalle.
            return false;
        }
    }

    public Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<AppUser> manager, AppUser user)
        => Task.FromResult(false);
}

/// <summary>Enlace de "olvidé mi contraseña" (<c>/restablecer</c>): vence a las 24 horas.</summary>
public sealed class ResetTokenProvider(IDataProtectionProvider dataProtection, IClock clock)
    : ClockedTokenProvider(dataProtection, clock, ProviderName)
{
    public const string ProviderName = "GsmReset";

    public static readonly TimeSpan Validity = TimeSpan.FromHours(24);

    protected override TimeSpan Lifespan => Validity;
}

/// <summary>Enlace de activación de staff y owners (<c>/activar</c>): vence a las 72 horas.</summary>
public sealed class ActivationTokenProvider(IDataProtectionProvider dataProtection, IClock clock)
    : ClockedTokenProvider(dataProtection, clock, ProviderName)
{
    public const string ProviderName = "GsmActivation";

    public const string Purpose = "activation";

    public static readonly TimeSpan Validity = TimeSpan.FromHours(72);

    protected override TimeSpan Lifespan => Validity;
}
