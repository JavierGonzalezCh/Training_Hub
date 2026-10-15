using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Communications;

/// <summary>
/// Filtros de la audiencia de un anuncio (CO2). Todos vacíos = todos los miembros. Los valores de un
/// mismo filtro suman (O); los filtros distintos se combinan con "y". Los estados comerciales viajan
/// como texto (<c>Current</c>, <c>Expiring</c>, <c>Grace</c>, <c>Expired</c>, <c>Frozen</c>,
/// <c>NoPlan</c>) porque el enum vive en Memberships y Domain no lo conoce; el caso de uso los valida.
/// </summary>
public sealed record AnnouncementAudience
{
    public const int MaxValuesPerFilter = 50;
    public const int MaxTagLength = 40;

    public static AnnouncementAudience Everyone { get; } = new();

    public IReadOnlyList<string> Statuses { get; init; } = [];

    public IReadOnlyList<Guid> PlanIds { get; init; } = [];

    public IReadOnlyList<Guid> ClassIds { get; init; } = [];

    public IReadOnlyList<string> Tags { get; init; } = [];

    public bool IsEveryone => Statuses.Count == 0 && PlanIds.Count == 0 && ClassIds.Count == 0 && Tags.Count == 0;

    /// <summary>Normaliza (recorta, quita vacíos y repetidos) y valida los límites.</summary>
    public static AnnouncementAudience Create(
        IEnumerable<string>? statuses, IEnumerable<Guid>? planIds, IEnumerable<Guid>? classIds, IEnumerable<string>? tags)
    {
        var audience = new AnnouncementAudience
        {
            Statuses = Clean(statuses),
            PlanIds = (planIds ?? []).Where(id => id != Guid.Empty).Distinct().ToArray(),
            ClassIds = (classIds ?? []).Where(id => id != Guid.Empty).Distinct().ToArray(),
            Tags = Clean(tags),
        };

        if (audience.Statuses.Count > MaxValuesPerFilter || audience.PlanIds.Count > MaxValuesPerFilter
            || audience.ClassIds.Count > MaxValuesPerFilter || audience.Tags.Count > MaxValuesPerFilter)
        {
            throw new ValidationDomainException($"Cada filtro admite hasta {MaxValuesPerFilter} valores.");
        }

        if (audience.Tags.Any(t => t.Length > MaxTagLength))
        {
            throw new ValidationDomainException($"Una etiqueta no puede pasar de {MaxTagLength} caracteres.");
        }

        return audience;
    }

    private static string[] Clean(IEnumerable<string>? values)
        => (values ?? [])
            .Select(v => (v ?? string.Empty).Trim())
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
