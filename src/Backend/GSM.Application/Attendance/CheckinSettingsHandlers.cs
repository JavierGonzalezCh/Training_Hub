using GSM.Application.Attendance.Abstractions;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Domain.Attendance;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Attendance;

public sealed class GetCheckinSettingsHandler(
    ICurrentUserService currentUser, ICheckinSettingsRepository settings)
{
    public async Task<CheckinSettingsResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var current = await settings.GetAsync(gymId, cancellationToken) ?? CheckinSettings.Default(gymId);
        return new CheckinSettingsResponse(current.Mode.ToString());
    }
}

public sealed class UpdateCheckinSettingsHandler(
    ICurrentUserService currentUser,
    ICheckinSettingsRepository settings,
    IUnitOfWork unitOfWork,
    ILogger<UpdateCheckinSettingsHandler> logger)
{
    /// <summary>El modo viaja como texto (Rotating/Static), como el resto de enums de la API.</summary>
    internal static CheckinMode ParseMode(string mode)
        => Enum.TryParse<CheckinMode>(mode, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ValidationDomainException("Modo de check-in inválido.");

    public async Task<CheckinSettingsResponse> HandleAsync(
        UpdateCheckinSettingsRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var current = await settings.GetAsync(gymId, cancellationToken);
        if (current is null)
        {
            current = CheckinSettings.Default(gymId);
            settings.Add(current);
        }

        current.Update(ParseMode(request.Mode));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Ajustes de check-in del gym {GymId}: modo {Mode}", gymId, current.Mode);
        return new CheckinSettingsResponse(current.Mode.ToString());
    }
}

public sealed class GetKioskCodeHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    ICheckinSettingsRepository settings,
    ICheckinCodeSigner signer)
{
    public async Task<KioskCodeResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await KioskCode.EnsureEnabledAsync(gyms, gymId, cancellationToken);

        var current = await settings.GetAsync(gymId, cancellationToken) ?? CheckinSettings.Default(gymId);
        return KioskCode.Build(signer, clock, gymId, current);
    }
}

public sealed class RegenerateStaticCodeHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    ICheckinSettingsRepository settings,
    ICheckinCodeSigner signer,
    IUnitOfWork unitOfWork,
    ILogger<RegenerateStaticCodeHandler> logger)
{
    public async Task<KioskCodeResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await KioskCode.EnsureEnabledAsync(gyms, gymId, cancellationToken);

        // Sin fila, el gym está en el modo por defecto (rotativo): no hay cartel que regenerar.
        var current = await settings.GetAsync(gymId, cancellationToken);
        if (current is null || current.Mode != CheckinMode.Static)
        {
            throw new ConflictException("Solo se puede regenerar el código en modo de cartel impreso.");
        }

        current.RegenerateStaticCode();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Código fijo de check-in regenerado en el gym {GymId}", gymId);
        return KioskCode.Build(signer, clock, gymId, current);
    }
}

internal static class KioskCode
{
    public static async Task EnsureEnabledAsync(IGymReader gyms, Guid gymId, CancellationToken cancellationToken)
    {
        var flags = await gyms.GetFeatureFlagsAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("Gimnasio no encontrado.");

        if (!flags.CheckinQr)
        {
            throw new ForbiddenDomainException("El check-in con QR está desactivado en este gimnasio.");
        }
    }

    public static KioskCodeResponse Build(ICheckinCodeSigner signer, IClock clock, Guid gymId, CheckinSettings s)
    {
        var now = clock.UtcNow;
        var code = signer.Generate(gymId, s.Mode, s.StaticCodeVersion, now);

        DateTimeOffset? expires = null;
        if (s.Mode == CheckinMode.Rotating)
        {
            var window = now.ToUnixTimeSeconds() / CheckinCodeWindow.Seconds;
            expires = DateTimeOffset.FromUnixTimeSeconds((window + 1) * CheckinCodeWindow.Seconds);
        }

        return new KioskCodeResponse(s.Mode.ToString(), code, expires);
    }
}
