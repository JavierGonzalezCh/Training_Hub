using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Public;
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

public sealed class AccountLinkBuilder(IOptions<AccountLinkOptions> options) : IAccountLinkBuilder, IMemberRegistrationLinks
{
    private readonly string _baseUrl = options.Value.PublicWebBaseUrl.TrimEnd('/');

    public string RegistrationUrl(string code, string? gymSlug = null)
    {
        var url = $"{_baseUrl}/app/registro?code={Uri.EscapeDataString(code)}";
        return string.IsNullOrWhiteSpace(gymSlug) ? url : $"{url}&gym={Uri.EscapeDataString(gymSlug.Trim())}";
    }

    public string ResetUrl(string email, string code) => Build("/restablecer", email, code);

    public string ActivationUrl(string email, string? code) => Build("/activar", email, code);

    private string Build(string path, string email, string? code)
    {
        var url = $"{_baseUrl}{path}?email={Uri.EscapeDataString(email)}";
        return code is null ? url : $"{url}&code={Uri.EscapeDataString(code)}";
    }
}
