namespace GSM.Domain.Communications;

/// <summary>
/// Conjunto fijo de reacciones a un anuncio; se muestran con iconos, nunca con emojis libres.
/// Label en Postgres: <c>communications.reaction_kind</c>.
/// </summary>
public enum ReactionKind
{
    Like,
    Celebrate,
    Strong,
}
