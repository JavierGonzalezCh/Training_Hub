namespace GSM.UI.Shared.Formatting;

/// <summary>
/// <c>PaymentMethod</c> del backend (<c>Cash=0, Transfer=1, Card=2, Other=3</c>) en los dos
/// sentidos que necesita la UI: la etiqueta en español para mostrar, y el valor entero para
/// filtros por query string (el cuerpo JSON viaja como string vía <c>JsonStringEnumConverter</c>,
/// pero los filtros de listado son parámetros de query, que el binder de ASP.NET acepta como
/// nombre o como número).
/// </summary>
public static class PaymentMethodLabels
{
    public static readonly (int Value, string Label)[] All =
    [
        (0, "Efectivo"),
        (1, "Transferencia"),
        (2, "Tarjeta"),
        (3, "Otro"),
    ];

    public static string Label(string method) => method switch
    {
        "Cash" => "Efectivo",
        "Transfer" => "Transferencia",
        "Card" => "Tarjeta",
        "Other" => "Otro",
        _ => method,
    };
}
