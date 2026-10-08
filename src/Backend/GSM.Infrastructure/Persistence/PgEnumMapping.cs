using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Npgsql.NameTranslation;

namespace GSM.Infrastructure.Persistence;

/// <summary>
/// Un enum nativo de Postgres: se declara una sola vez y sirve para el modelo de EF
/// (<c>HasPostgresEnum</c>) y para el proveedor Npgsql (<c>MapEnum</c>).
/// </summary>
public sealed record PgEnumMapping(
    Action<ModelBuilder> ConfigureModel,
    Action<NpgsqlDbContextOptionsBuilder> ConfigureProvider)
{
    public static PgEnumMapping Of<TEnum>(string name, string schema, INpgsqlNameTranslator translator)
        where TEnum : struct, Enum =>
        new(
            model => model.HasPostgresEnum<TEnum>(schema, name, translator),
            npgsql => npgsql.MapEnum<TEnum>(name, schema, translator));
}

/// <summary>
/// Cada módulo con enums de Postgres declara una clase que implementa esta interfaz en su
/// carpeta de Infrastructure; <see cref="PgEnumMappings"/> las descubre. Así registrar un enum
/// nuevo no obliga a editar archivos centrales.
/// </summary>
public interface IModuleEnums
{
    IEnumerable<PgEnumMapping> Enums { get; }
}

public static class PgEnumMappings
{
    private static readonly Lazy<IReadOnlyList<PgEnumMapping>> All = new(() =>
        typeof(IModuleEnums).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IModuleEnums).IsAssignableFrom(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .SelectMany(t => ((IModuleEnums)Activator.CreateInstance(t)!).Enums)
            .ToList());

    public static void ConfigureModel(ModelBuilder modelBuilder)
    {
        foreach (var mapping in All.Value)
        {
            mapping.ConfigureModel(modelBuilder);
        }
    }

    public static void ConfigureProvider(NpgsqlDbContextOptionsBuilder npgsql)
    {
        foreach (var mapping in All.Value)
        {
            mapping.ConfigureProvider(npgsql);
        }
    }
}
