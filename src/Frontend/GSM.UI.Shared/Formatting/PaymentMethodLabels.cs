namespace GSM.UI.Shared.Formatting;

/// <summary>
/// <c>PaymentMethod</c> del backend (<c>Cash=0, Card=1, Nequi=2, Daviplata=3, Breb=4, BankTransfer=5,
/// BankDeposit=6, Other=7</c>) en los dos sentidos que necesita la UI: la etiqueta en español para mostrar, y
/// el valor entero para filtros por query string (el cuerpo JSON viaja como string vía
/// <c>JsonStringEnumConverter</c>, pero los filtros de listado son parámetros de query, que el binder de
/// ASP.NET acepta como nombre o como número). Es el orden de la spec de Payments v2, §3.
/// </summary>
public static class PaymentMethodLabels
{
    public static readonly (int Value, string Label)[] All =
    [
        (0, "Efectivo"),
        (1, "Tarjeta"),
        (2, "Nequi"),
        (3, "Daviplata"),
        (4, "Bre-B"),
        (5, "Transferencia bancaria"),
        (6, "Consignación"),
        (7, "Otro"),
    ];

    public static string Label(string method) => method switch
    {
        "Cash" => "Efectivo",
        "Card" => "Tarjeta",
        "Nequi" => "Nequi",
        "Daviplata" => "Daviplata",
        "Breb" => "Bre-B",
        "BankTransfer" => "Transferencia bancaria",
        "BankDeposit" => "Consignación",
        "Other" => "Otro",
        _ => method,
    };
}
