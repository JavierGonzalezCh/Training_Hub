using GSM.Domain.Common;

namespace GSM.Domain.Gyms;

/// <summary>
/// Interruptores de módulos por gym. Todos arrancan en <c>true</c>. La app móvil los lee desde ya,
/// incluso los de módulos aún no construidos, para no migrar datos cuando lleguen.
/// Relación 1:1 con <see cref="Gym"/> (índice único sobre <see cref="GymId"/>); la PK sigue siendo
/// <c>id</c> por convención (§8.1 de la especificación).
/// </summary>
public sealed class GymFeatureFlags : BaseEntity, ITenantEntity
{
    private GymFeatureFlags()
    {
    }

    public Guid GymId { get; private set; }

    public bool ShowRoutines { get; private set; } = true;

    public bool ShowClasses { get; private set; } = true;

    public bool ShowProgress { get; private set; } = true;

    public bool ShowGamification { get; private set; } = true;

    public bool ShowCommunity { get; private set; } = true;

    public bool CheckinQr { get; private set; } = true;

    public bool CommunityComments { get; private set; } = true;

    /// <summary>El socio puede armarse rutinas él mismo (etapa 9.6). Arranca en <c>false</c>.</summary>
    public bool MembersCanCreateRoutines { get; private set; }

    public static GymFeatureFlags Default(Guid gymId) => new() { GymId = gymId };

    public void Update(
        bool showRoutines,
        bool showClasses,
        bool showProgress,
        bool showGamification,
        bool showCommunity,
        bool checkinQr,
        bool communityComments,
        bool membersCanCreateRoutines)
    {
        ShowRoutines = showRoutines;
        ShowClasses = showClasses;
        ShowProgress = showProgress;
        ShowGamification = showGamification;
        ShowCommunity = showCommunity;
        CheckinQr = checkinQr;
        CommunityComments = communityComments;
        MembersCanCreateRoutines = membersCanCreateRoutines;
    }
}
