using GSM.Domain.Achievements;

namespace GSM.Application.Achievements.Abstractions;

/// <summary>
/// Otorgamiento retroactivo (L1, spec §6): a quien ya cumple la condición de un logro al crearlo o al bajar
/// su valor. Lo implementa Infrastructure porque necesita las métricas de otros módulos (por sus lectores).
/// </summary>
public interface IRetroactiveAchievementGrant
{
    /// <summary>
    /// Miembros activos del gym que ya cumplen la condición y todavía no tienen el logro
    /// (<paramref name="achievementId"/> excluye a quienes ya lo ganaron). Es la cifra del aviso previo.
    /// </summary>
    Task<int> CountEligibleAsync(
        AchievementConditionType conditionType, int conditionValue, Guid? achievementId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Desbloquea el logro, con la fecha de hoy, a todo miembro activo que lo cumple y aún no lo tiene, y
    /// publica un <c>AchievementUnlockedEvent</c> por miembro (un solo aviso cada uno). Idempotente: una
    /// segunda ejecución no encuentra a nadie nuevo. Devuelve cuántos lo recibieron ahora.
    /// </summary>
    Task<int> GrantAsync(Achievement achievement, CancellationToken cancellationToken = default);
}
