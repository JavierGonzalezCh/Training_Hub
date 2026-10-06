namespace GSM.UI.Shared.Formatting;

/// <summary>
/// Valor del ítem "Todos" de los selects de filtro.
/// <para>
/// No puede ser cadena vacía: MudBlazor da el campo por vacío y deja la etiqueta flotante
/// sin encoger, pintada encima del texto seleccionado ("Estado" sobre "Todos").
/// </para>
/// </summary>
public static class FilterValues
{
    public const string All = "all";

    /// <summary>Tolera también el vacío, por si algún filtro llega sin valor.</summary>
    public static bool IsAll(string? value) => string.IsNullOrEmpty(value) || value == All;
}
