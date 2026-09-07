using Npgsql;

namespace GSM.Infrastructure.Persistence;

/// <summary>
/// Traductor de nombres para enums nativos de Postgres: cada label es el nombre CLR en minúsculas
/// (<c>SuperAdmin</c> → <c>superadmin</c>), sin snake_case, para que coincida con el valor del claim.
/// </summary>
public sealed class LowerCaseNameTranslator : INpgsqlNameTranslator
{
    public string TranslateTypeName(string clrName) => clrName.ToLowerInvariant();

    public string TranslateMemberName(string clrName) => clrName.ToLowerInvariant();
}
