using System.Text;
using Npgsql;

namespace GSM.Infrastructure.Persistence;

/// <summary>
/// Traductor snake_case para enums cuyos miembros son de varias palabras
/// (<c>RoutineSessions</c> → <c>routine_sessions</c>). Para los de una sola palabra da el mismo
/// resultado que <see cref="LowerCaseNameTranslator"/>.
/// </summary>
/// <remarks>
/// <para>
/// Convive con <see cref="LowerCaseNameTranslator"/> a propósito. Los enums anteriores
/// (<c>UserRole</c>, <c>GymStatus</c>…) tienen sus labels ya escritos en la base con la regla
/// "todo en minúsculas, sin separador": pasarlos a snake_case convertiría <c>superadmin</c> en
/// <c>super_admin</c> y exigiría migrar datos y claims del JWT. Los enums con miembros
/// multi-palabra usan este traductor, que permite escribir el nombre CLR en PascalCase —como
/// manda la convención del proyecto— sin que el detalle de persistencia se filtre al enum de
/// Domain ni al contrato JSON de la API.
/// </para>
/// <para>
/// **Usar siempre <see cref="Instance"/>, nunca <c>new</c>:** el traductor forma parte de las
/// opciones del DbContext y EF Core cachea su contenedor interno comparándolo por referencia. Una
/// instancia nueva por contexto genera un <c>IServiceProvider</c> por petición y a las 20 la API
/// queda en 500 permanente.
/// </para>
/// </remarks>
public sealed class SnakeCaseNameTranslator : INpgsqlNameTranslator
{
    public static readonly SnakeCaseNameTranslator Instance = new();

    private SnakeCaseNameTranslator()
    {
    }

    public string TranslateTypeName(string clrName) => ToSnakeCase(clrName);

    public string TranslateMemberName(string clrName) => ToSnakeCase(clrName);

    private static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var builder = new StringBuilder(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];

            // Un guion bajo antes de cada mayúscula que abre palabra, nunca al principio
            // ni duplicando uno ya escrito.
            if (char.IsUpper(c) && i > 0 && name[i - 1] != '_')
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }
}
