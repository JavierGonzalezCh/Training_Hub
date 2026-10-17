using GSM.Application.Common.Abstractions;
using GSM.Application.Notifications.Abstractions;
using GSM.Application.Notifications.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;
using GSM.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Notifications;

/// <summary>Datos públicos del cliente web de Firebase. Implementado en Infrastructure (lee la configuración).</summary>
public interface IPushWebConfigProvider
{
    PushConfigResponse Get();
}

/// <summary>Nombres y conversión de las plataformas de push.</summary>
internal static class PushHandlers
{
    public static bool TryParsePlatform(string? value, out PushPlatform platform)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "android":
                platform = PushPlatform.Android;
                return true;
            case "web_member":
                platform = PushPlatform.WebMember;
                return true;
            case "web_panel":
                platform = PushPlatform.WebPanel;
                return true;
            default:
                platform = default;
                return false;
        }
    }

    public static string Name(PushPlatform platform) => platform switch
    {
        PushPlatform.Android => "android",
        PushPlatform.WebMember => "web_member",
        _ => "web_panel",
    };

    /// <summary>Plataformas que le corresponden a un rol: el miembro, la app y la PWA del miembro; el equipo, la app y el panel.</summary>
    public static IReadOnlyList<PushPlatform> PlatformsFor(UserRole? role)
        => role == UserRole.Member
            ? [PushPlatform.Android, PushPlatform.WebMember]
            : [PushPlatform.Android, PushPlatform.WebPanel];
}

/// <summary>
/// Registra el dispositivo de la persona. Un token repetido no duplica: se reutiliza la fila (y se reasigna
/// si el mismo teléfono entra con otra cuenta). El dispositivo es de la persona, no del gym.
/// </summary>
public sealed class RegisterPushDeviceHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IPushDeviceRepository devices,
    IUnitOfWork unitOfWork,
    ILogger<RegisterPushDeviceHandler> logger)
{
    public async Task<PushDeviceResponse> HandleAsync(RegisterPushDeviceRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedDomainException("Sin usuario en el contexto.");
        if (!PushHandlers.TryParsePlatform(request.Platform, out var platform))
        {
            throw new ValidationDomainException("Plataforma inválida: android, web_member o web_panel.");
        }

        if (!PushHandlers.PlatformsFor(currentUser.Role).Contains(platform))
        {
            throw new ValidationDomainException("Esa plataforma no corresponde a tu rol.");
        }

        var token = request.Token.Trim();
        var device = await devices.FindByTokenAsync(token, cancellationToken);
        if (device is null)
        {
            device = PushDevice.Register(userId, token, platform, clock.UtcNow);
            devices.Add(device);
        }
        else
        {
            device.Reregister(userId, token, platform, clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Usuario {UserId}: dispositivo push {DeviceId} registrado ({Platform})", userId, device.Id, request.Platform);
        return new PushDeviceResponse(device.Id, PushHandlers.Name(device.Platform));
    }
}

/// <summary>Da de baja el dispositivo propio. Idempotente: un token desconocido o ajeno no es un error ni se toca.</summary>
public sealed class UnregisterPushDeviceHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IPushDeviceRepository devices,
    IUnitOfWork unitOfWork,
    ILogger<UnregisterPushDeviceHandler> logger)
{
    public async Task HandleAsync(UnregisterPushDeviceRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedDomainException("Sin usuario en el contexto.");
        var device = await devices.FindByTokenAsync(request.Token.Trim(), cancellationToken);
        if (device is null || device.UserId != userId || !device.IsActive)
        {
            return;
        }

        device.Revoke(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Usuario {UserId}: dispositivo push {DeviceId} dado de baja", userId, device.Id);
    }
}

public sealed class GetPushConfigHandler(IPushWebConfigProvider provider)
{
    public PushConfigResponse Handle() => provider.Get();
}

/// <summary>Los avisos al staff que cada persona enciende o apaga en su teléfono (§8). Sin fila, el valor por defecto.</summary>
public sealed class GetMyStaffPushPreferencesHandler(
    ICurrentUserService currentUser,
    IStaffNotificationPreferenceRepository preferences)
{
    public async Task<StaffPushPreferencesResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var userId = currentUser.UserId ?? throw new UnauthorizedDomainException("Sin usuario en el contexto.");
        return StaffPushMapper.ToResponse(
            await preferences.GetForUserAsync(gymId, userId, cancellationToken), currentUser.Role == UserRole.Owner);
    }
}

public sealed class UpdateMyStaffPushPreferencesHandler(
    ICurrentUserService currentUser,
    IStaffNotificationPreferenceRepository preferences,
    IUnitOfWork unitOfWork,
    ILogger<UpdateMyStaffPushPreferencesHandler> logger)
{
    public async Task<StaffPushPreferencesResponse> HandleAsync(
        UpdateStaffPushPreferencesRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var userId = currentUser.UserId ?? throw new UnauthorizedDomainException("Sin usuario en el contexto.");
        var rows = (await preferences.GetForUserAsync(gymId, userId, cancellationToken)).ToList();

        foreach (var item in request.Items)
        {
            var type = StaffPushMapper.ParseType(item.Type);
            var row = rows.FirstOrDefault(r => r.Type == type);
            if (row is null)
            {
                row = StaffNotificationPreference.Create(gymId, userId, type, item.Push);
                preferences.Add(row);
                rows.Add(row);
            }
            else
            {
                row.Set(item.Push);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Usuario {UserId}: preferencias de push del staff actualizadas", userId);
        return StaffPushMapper.ToResponse(rows, currentUser.Role == UserRole.Owner);
    }
}

internal static class StaffPushMapper
{
    public static StaffPushPreferencesResponse ToResponse(IEnumerable<StaffNotificationPreference> rows, bool isOwner)
    {
        var byType = rows.ToDictionary(r => r.Type);
        return new StaffPushPreferencesResponse(StaffNotificationPreference.Configurable.Select(type =>
            new StaffPushPreferenceItem(
                type.ToString(), Label(type),
                byType.TryGetValue(type, out var row) ? row.Push : StaffNotificationPreference.DefaultPush(type, isOwner))).ToList());
    }

    public static NotificationType ParseType(string value)
        => Enum.TryParse<NotificationType>(value, ignoreCase: true, out var type)
            && StaffNotificationPreference.IsConfigurable(type)
                ? type
                : throw new ValidationDomainException($"Tipo de aviso inválido: {value}.");

    private static string Label(NotificationType type) => type switch
    {
        NotificationType.CheckinRejected => "Intento de entrada rechazado",
        NotificationType.InstructorClassReminder => "Recordatorio de mi clase",
        _ => type.ToString(),
    };
}
