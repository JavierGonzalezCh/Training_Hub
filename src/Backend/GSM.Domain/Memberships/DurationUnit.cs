namespace GSM.Domain.Memberships;

/// <summary>Unidad en que se expresa la duración de un plan por tiempo (Memberships v2, §3).</summary>
public enum DurationUnit
{
    /// <summary>Días corridos: la membresía dura exactamente N días.</summary>
    Days,

    /// <summary>Meses del calendario: de un día al mismo día del mes siguiente, menos uno.</summary>
    Months,
}
