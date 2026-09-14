namespace GSM.UI.Shared.Components;

/// <summary>
/// Página homogénea para <see cref="ServerTable{TItem}"/>. El cliente generado produce un tipo
/// <c>PagedResultOfX</c> distinto por cada listado (NSwag no comparte un <c>PagedResult&lt;T&gt;</c>
/// genérico), así que cada llamada adapta su respuesta a esta forma común.
/// </summary>
public sealed record ServerTablePage<TItem>(IReadOnlyList<TItem> Items, int TotalItems);
