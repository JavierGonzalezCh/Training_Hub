using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Achievements;

/// <summary>
/// Un miembro que aceptó aparecer en el ranking mensual de constancia (L6, spec §5). El ranking es voluntario: sin
/// fila no se aparece. Salirse borra la fila (no se guarda quién estuvo). Se le ve con nombre e inicial del apellido
/// y su foto ("Ana T."), o con un alias, sin foto.
/// </summary>
public sealed class RankingParticipant : BaseEntity, ITenantEntity
{
    public const int AliasMinLength = 3;
    public const int AliasMaxLength = 20;

    private RankingParticipant()
    {
    }

    public Guid GymId { get; private set; }

    public Guid MemberId { get; private set; }

    /// <summary><c>true</c> = se muestra el <see cref="Alias"/>; <c>false</c> = nombre e inicial del apellido con foto.</summary>
    public bool UseAlias { get; private set; }

    public string? Alias { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    public static RankingParticipant Join(Guid gymId, Guid memberId, bool useAlias, string? alias, DateTimeOffset now)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty)
        {
            throw new ValidationDomainException("Datos de participación incompletos.");
        }

        var participant = new RankingParticipant { GymId = gymId, MemberId = memberId, JoinedAt = now };
        participant.SetDisplay(useAlias, alias);
        return participant;
    }

    public void SetDisplay(bool useAlias, string? alias)
    {
        if (useAlias)
        {
            var normalized = NormalizeAlias(alias)
                ?? throw new ValidationDomainException("Escribe el alias con el que quieres aparecer.");
            UseAlias = true;
            Alias = normalized;
            return;
        }

        UseAlias = false;
        Alias = null;
    }

    /// <summary>Alias recortado y validado (3 a 20 caracteres; letras, números, espacios, punto, guion y guion bajo). Nulo si viene vacío.</summary>
    public static string? NormalizeAlias(string? alias)
    {
        var trimmed = alias?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length is < AliasMinLength or > AliasMaxLength)
        {
            throw new ValidationDomainException(
                $"El alias debe tener entre {AliasMinLength} y {AliasMaxLength} caracteres.");
        }

        if (!trimmed.All(c => char.IsLetterOrDigit(c) || c is ' ' or '.' or '-' or '_'))
        {
            throw new ValidationDomainException("El alias solo admite letras, números, espacios, punto, guion y guion bajo.");
        }

        return trimmed;
    }

    /// <summary>"Ana T.": nombre y la inicial del apellido.</summary>
    public static string RealDisplayName(string firstName, string lastName)
    {
        var last = lastName.Trim();
        var initial = last.Length > 0 ? $" {char.ToUpperInvariant(last[0])}." : string.Empty;
        return $"{firstName.Trim()}{initial}";
    }
}
