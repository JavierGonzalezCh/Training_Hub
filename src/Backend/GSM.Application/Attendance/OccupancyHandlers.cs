using GSM.Application.Attendance.Abstractions;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Domain.Attendance;

namespace GSM.Application.Attendance;

/// <summary>Niveles de ocupación que ve el miembro (A5): tranquilo &lt; 40 %, moderado 40-75 %, lleno &gt; 75 %.</summary>
public static class OccupancyLevel
{
    public const string Calm = "calm";
    public const string Moderate = "moderate";
    public const string Full = "full";

    /// <summary>Nivel según la proporción exacta (no la redondeada). Sin aforo no hay nivel.</summary>
    public static string? For(int peopleInside, int? capacity)
    {
        if (capacity is not > 0)
        {
            return null;
        }

        var ratio = (double)peopleInside / capacity.Value;
        return ratio < 0.40 ? Calm : ratio <= 0.75 ? Moderate : Full;
    }
}

/// <summary>
/// Estimación de cuánta gente hay adentro: personas distintas con una entrada no anulada en la permanencia asumida
/// del gym (la salida no se registra). El aforo es de Gyms y se lee por <see cref="IGymReader"/>.
/// </summary>
public static class OccupancyEstimator
{
    public static async Task<OccupancyResponse> EstimateAsync(
        Guid gymId,
        IClock clock,
        IGymReader gyms,
        ICheckinSettingsRepository settings,
        IAttendanceRepository attendance,
        CancellationToken cancellationToken)
    {
        var current = await settings.GetAsync(gymId, cancellationToken);
        var stay = current?.AssumedStayMinutes ?? CheckinSettings.DefaultAssumedStayMinutes;
        var capacity = await gyms.GetCapacityAsync(gymId, cancellationToken);

        var now = clock.UtcNow;
        var people = await attendance.CountDistinctMembersInWindowAsync(
            gymId, now.AddMinutes(-stay), now, cancellationToken,
            // Quien entrena fuera de la sede no está «dentro» de un local: no cuenta para el aforo.
            excludeRoutineEntries: current is { RemoteTraining: true });

        int? percent = capacity is > 0 ? (int)Math.Round(people * 100.0 / capacity.Value, MidpointRounding.AwayFromZero) : null;
        return new OccupancyResponse(people, capacity is > 0 ? capacity : null, percent, OccupancyLevel.For(people, capacity), stay);
    }
}

/// <summary>Ocupación con número y porcentaje, para el staff (dashboard del panel).</summary>
public sealed class GetOccupancyHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    ICheckinSettingsRepository settings,
    IAttendanceRepository attendance)
{
    public Task<OccupancyResponse> HandleAsync(CancellationToken cancellationToken)
        => OccupancyEstimator.EstimateAsync(
            currentUser.RequireGymId(), clock, gyms, settings, attendance, cancellationToken);
}

/// <summary>Ocupación para el miembro: solo el nivel, sin número exacto.</summary>
public sealed class GetMyOccupancyHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    ICheckinSettingsRepository settings,
    IAttendanceRepository attendance)
{
    public async Task<MemberOccupancyResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var estimate = await OccupancyEstimator.EstimateAsync(
            currentUser.RequireGymId(), clock, gyms, settings, attendance, cancellationToken);
        return new MemberOccupancyResponse(estimate.Level);
    }
}
