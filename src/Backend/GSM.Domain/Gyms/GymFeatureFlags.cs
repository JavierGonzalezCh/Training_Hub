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

    /// <summary>El miembro puede armarse rutinas él mismo (etapa 9.6). Arranca en <c>false</c>.</summary>
    public bool MembersCanCreateRoutines { get; private set; }

    /// <summary>
    /// El miembro puede reportar fallas de las máquinas desde la app (Equipment E4). Arranca en <c>true</c>
    /// ("permitidos por defecto"); el gym lo apaga si no los quiere.
    /// </summary>
    public bool MembersCanReportEquipment { get; private set; } = true;

    /// <summary>
    /// Muestra el ranking mensual voluntario de constancia (Achievements L6). Arranca en <c>true</c>; el gym lo apaga
    /// si no lo quiere. Aun encendido, solo aparece quien acepta participar.
    /// </summary>
    public bool ShowRanking { get; private set; } = true;

    /// <summary>
    /// Muestra Recepción, el kiosco y el aforo en el panel, y «¿Qué tan lleno está?» en la app del miembro. Es una opción de
    /// interfaz, como <see cref="ShowCommunity"/>: apagada no bloquea nada en la API (docs/GSM-tipos-de-negocio.md §5.3).
    /// Arranca en <c>true</c>.
    /// </summary>
    public bool ShowFrontDesk { get; private set; } = true;

    public static GymFeatureFlags Default(Guid gymId) => new() { GymId = gymId };

    /// <summary>
    /// Los interruptores iniciales de la plantilla del tipo de negocio (docs/GSM-tipos-de-negocio.md §4.1). Es solo el
    /// valor de arranque: el dueño lo cambia después en Configuración y nada vuelve a consultar el tipo.
    /// </summary>
    public static GymFeatureFlags ForType(Guid gymId, GymType type)
    {
        var flags = Default(gymId);
        switch (type)
        {
            case GymType.Studio:
            case GymType.Academy:
                flags.ShowRoutines = false;
                flags.MembersCanReportEquipment = false;
                break;
            case GymType.PersonalTrainer:
                flags.ShowClasses = false;
                flags.ShowCommunity = false;
                flags.CheckinQr = false;
                flags.MembersCanReportEquipment = false;
                flags.ShowRanking = false;
                flags.ShowFrontDesk = false;
                break;
        }

        return flags;
    }

    public void Update(
        bool showRoutines,
        bool showClasses,
        bool showProgress,
        bool showGamification,
        bool showCommunity,
        bool checkinQr,
        bool membersCanCreateRoutines,
        bool membersCanReportEquipment = true,
        bool? showRanking = null,
        bool? showFrontDesk = null)
    {
        ShowRoutines = showRoutines;
        ShowClasses = showClasses;
        ShowProgress = showProgress;
        ShowGamification = showGamification;
        ShowCommunity = showCommunity;
        CheckinQr = checkinQr;
        MembersCanCreateRoutines = membersCanCreateRoutines;
        MembersCanReportEquipment = membersCanReportEquipment;
        ShowRanking = showRanking ?? ShowRanking;
        ShowFrontDesk = showFrontDesk ?? ShowFrontDesk;
    }
}
