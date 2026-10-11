using GSM.Application.Identity.Abstractions;
using Microsoft.Extensions.Options;

namespace GSM.Infrastructure.Identity;

/// <summary>Configuración de los enlaces públicos de la cuenta (sección <c>Identity</c>).</summary>
public sealed class AccountLinkOptions
{
    public const string SectionName = "Identity";

    /// <summary>
    /// Dirección pública del panel web (por ejemplo <c>https://panel.midominio.com</c>), sin barra final. Vacía,
    /// los enlaces salen como rutas relativas (<c>/restablecer?…</c>) y no sirven en un correo: es obligatoria
    /// en producción.
    /// </summary>
    public string PublicWebBaseUrl { get; init; } = string.Empty;
}

public sealed class AccountLinkBuilder(IOptions<AccountLinkOptions> options) : IAccountLinkBuilder
{
    private readonly string _baseUrl = options.Value.PublicWebBaseUrl.TrimEnd('/');

    public string ResetUrl(string email, string code) => Build("/restablecer", email, code);

    public string ActivationUrl(string email, string? code) => Build("/activar", email, code);

    private string Build(string path, string email, string? code)
    {
        var url = $"{_baseUrl}{path}?email={Uri.EscapeDataString(email)}";
        return code is null ? url : $"{url}&code={Uri.EscapeDataString(code)}";
    }
}
