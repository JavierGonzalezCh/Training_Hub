using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;

namespace GSM.Infrastructure.Migrations;

/// <summary>
/// Acceso a los scripts <c>Migrations/Scripts/V2_&lt;id&gt;.sql</c> embebidos en el ensamblado.
/// </summary>
public static class MigrationScripts
{
    private static readonly Assembly Assembly = typeof(MigrationScripts).Assembly;

    /// <summary>Nombre lógico del recurso embebido para un id (p. ej. <c>MS1_estado</c>).</summary>
    public static string ResourceName(string id) => $"GSM.Migrations.Scripts.V2_{id}.sql";

    /// <summary>Lee el script <c>V2_&lt;id&gt;.sql</c>; falla si no existe.</summary>
    public static string Read(string id)
    {
        using var stream = Assembly.GetManifestResourceStream(ResourceName(id))
            ?? throw new InvalidOperationException(
                $"No existe el script de migración 'V2_{id}.sql' (Migrations/Scripts, recurso embebido).");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Ejecuta dentro de la migración el script <c>V2_&lt;id&gt;.sql</c>.</summary>
    public static OperationBuilder<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation> SqlScript(
        this MigrationBuilder migrationBuilder, string id) =>
        migrationBuilder.Sql(Read(id));
}
