using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Logs;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Identity;

public sealed class ListKiosksHandler(ICurrentUserService currentUser, IKioskDeviceService kiosks)
{
    public async Task<IReadOnlyList<KioskDeviceResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var devices = await kiosks.ListAsync(currentUser.RequireGymId(), cancellationToken);
        return devices.Select(KioskMapping.ToResponse).ToList();
    }
}

public sealed class CreateKioskHandler(
    ICurrentUserService currentUser,
    IKioskDeviceService kiosks,
    ILogger<CreateKioskHandler> logger)
{
    public async Task<KioskCreatedResponse> HandleAsync(CreateKioskRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario autenticado en el contexto.");

        var (device, code) = await kiosks.CreateAsync(gymId, request.Name, actorId, cancellationToken);

        logger.LogInformation("Kiosco {KioskId} creado en el gym {GymId}", device.Id, gymId);
        return new KioskCreatedResponse(KioskMapping.ToResponse(device), code, device.PairingCodeExpiresAt!.Value);
    }
}

public sealed class RevokeKioskHandler(
    ICurrentUserService currentUser,
    IKioskDeviceService kiosks,
    ILogger<RevokeKioskHandler> logger)
{
    public async Task<KioskDeviceResponse> HandleAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var device = await kiosks.RevokeAsync(gymId, deviceId, cancellationToken);
        logger.LogInformation("Kiosco {KioskId} revocado en el gym {GymId}", deviceId, gymId);
        return KioskMapping.ToResponse(device);
    }
}

/// <summary>Anónimo: el equipo canjea el código de emparejamiento y recibe su sesión de dispositivo (rol <c>kiosk</c>).</summary>
public sealed class PairKioskHandler(
    IKioskDeviceService kiosks,
    IJwtTokenService jwtTokens,
    IGymLoginGate loginGate,
    ILogStore logStore,
    ILogger<PairKioskHandler> logger)
{
    public async Task<AuthTokensResponse> HandleAsync(PairKioskRequest request, CancellationToken cancellationToken)
    {
        var paired = await kiosks.PairAsync(request.PairingCode, cancellationToken);
        if (paired is null)
        {
            // Seguridad (§4.7): sin el código intentado (los códigos nunca se guardan).
            logStore.RecordSecurityEvent(new SecurityEventLogEntry(SecurityEventTypes.KioskPairingFailed));
        }

        var pairing = paired
            ?? throw new UnauthorizedDomainException("El código es inválido o ya venció. Pídele al dueño uno nuevo.");

        if (await loginGate.IsSuspendedAsync(pairing.Device.GymId, cancellationToken))
        {
            throw new ForbiddenDomainException("La cuenta del gimnasio está suspendida. Contacta con soporte.");
        }

        var access = jwtTokens.CreateKioskAccessToken(pairing.Device.Id, pairing.Device.Name, pairing.Device.GymId);
        logger.LogInformation("Kiosco {KioskId} emparejado en el gym {GymId}", pairing.Device.Id, pairing.Device.GymId);
        return new AuthTokensResponse(access.Value, pairing.RefreshToken, access.ExpiresAtUtc);
    }
}

/// <summary>
/// Anónimo: renueva el access token del kiosco (dura 15 minutos). El token de renovación del dispositivo
/// <b>no rota</b> y no vence: un equipo sin supervisión no puede quedar fuera por una respuesta perdida;
/// lo corta el Owner al revocar el kiosco.
/// </summary>
public sealed class RefreshKioskHandler(
    IKioskDeviceService kiosks,
    IJwtTokenService jwtTokens,
    IGymLoginGate loginGate)
{
    public async Task<AuthTokensResponse> HandleAsync(KioskRefreshRequest request, CancellationToken cancellationToken)
    {
        var device = await kiosks.ResolveActiveAsync(request.RefreshToken, cancellationToken)
            ?? throw new UnauthorizedDomainException("La sesión del kiosco ya no es válida. Vuelve a emparejarlo.");

        if (await loginGate.IsSuspendedAsync(device.GymId, cancellationToken))
        {
            throw new ForbiddenDomainException("La cuenta del gimnasio está suspendida. Contacta con soporte.");
        }

        var access = jwtTokens.CreateKioskAccessToken(device.Id, device.Name, device.GymId);
        return new AuthTokensResponse(access.Value, request.RefreshToken, access.ExpiresAtUtc);
    }
}

internal static class KioskMapping
{
    public static KioskDeviceResponse ToResponse(KioskDeviceRecord d)
        => new(d.Id, d.Name, d.Status, d.CreatedAt, d.PairingCodeExpiresAt, d.PairedAt, d.RevokedAt);
}
