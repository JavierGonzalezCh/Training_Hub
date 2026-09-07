using Npgsql;

namespace GSM.Infrastructure.Persistence;

/// <summary>
/// Traductor de nombres para enums nativos de Postgres: cada label es el nombre CLR en minúsculas
/// (<c>SuperAdmin</c> → <c>superadmin</c>), sin snake_case, para que coincida con el valor del claim.
/// </summary>
public sealed class LowerCaseNameTranslator : INpgsqlNameTranslator
{
    /// <summary>
    /// Instancia compartida. **Usar siempre esta, nunca <c>new</c>:** el translator forma parte de
    /// las opciones del DbContext, y EF Core cachea su contenedor interno usando esas opciones como
    /// clave, comparando el translator por referencia. Una instancia nueva por DbContext genera una
    /// clave distinta cada vez, EF construye un <c>IServiceProvider</c> nuevo por petición y a los 20
    /// aborta con <c>ManyServiceProvidersCreatedWarning</c>, dejando la API en 500 permanente.
    /// </summary>
    public static readonly LowerCaseNameTranslator Instance = new();

    public string TranslateTypeName(string clrName) => clrName.ToLowerInvariant();

    public string TranslateMemberName(string clrName) => clrName.ToLowerInvariant();
}
