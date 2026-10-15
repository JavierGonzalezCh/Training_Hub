namespace GSM.Domain.Routines;

/// <summary>
/// Tipo de serie. <see cref="Normal"/> va primero a propósito: es el valor por defecto de los registros
/// guardados antes de R4, que no traían tipo.
/// </summary>
public enum SetType
{
    Normal,
    Warmup,
    Drop,
    Failure,
}

public static class SetTypes
{
    public static string ToText(SetType type) => type switch
    {
        SetType.Normal => "normal",
        SetType.Warmup => "warmup",
        SetType.Drop => "drop",
        SetType.Failure => "failure",
        _ => "normal",
    };

    /// <summary>Texto de la API a tipo; nulo o vacío = normal; otro valor es <c>null</c> (inválido).</summary>
    public static SetType? Parse(string? text) => (text ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "" or "normal" => SetType.Normal,
        "warmup" => SetType.Warmup,
        "drop" => SetType.Drop,
        "failure" => SetType.Failure,
        _ => null,
    };
}
