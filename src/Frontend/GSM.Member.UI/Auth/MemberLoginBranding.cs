using System.Text.Json;
using GSM.Client.Generated;
using Microsoft.JSInterop;

namespace GSM.Member.UI.Auth;

/// <summary>
/// La marca del gym en las pantallas de entrada del miembro (<c>/app/entrar</c>, <c>/app/registro</c>), que
/// todavía no tienen sesión ni <c>GymContext</c>. El gym se sabe por <c>?gym=slug</c> (se pide la marca pública
/// a la API) o porque el teléfono recuerda el último al que entró; sin ninguno, queda la marca de GSM.
/// </summary>
public sealed class MemberLoginBranding(IJSRuntime js, IPublicGymsClient gyms)
{
    private const string StorageKey = "gsm.member.gym";

    public MeGym? Gym { get; private set; }

    /// <summary>Slug del gym, si se conoce (viene de <c>?gym=</c>); lo usa el correo del código para nombrar el gym.</summary>
    public string? Slug { get; private set; }

    public event Action? Changed;

    /// <summary>Con <paramref name="slug"/> consulta la marca pública del gym; sin él, usa el último gym recordado.</summary>
    public async Task LoadAsync(string? slug, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(slug))
        {
            try
            {
                var branding = await gyms.BrandingAsync(slug.Trim(), cancellationToken);
                Set(
                    new MeGym
                    {
                        Name = branding.Name,
                        LogoUrl = branding.LogoUrl,
                        PrimaryColor = branding.PrimaryColor,
                        SecondaryColor = branding.SecondaryColor,
                    },
                    slug.Trim().ToLowerInvariant());
                await StoreAsync(cancellationToken);
                return;
            }
            catch (GsmApiException)
            {
                // Slug desconocido o API caída: se sigue con lo recordado, o con la marca de GSM.
            }
        }

        var stored = await ReadAsync(cancellationToken);
        if (stored is not null)
        {
            Set(
                new MeGym
                {
                    Name = stored.Name,
                    LogoUrl = stored.LogoUrl,
                    PrimaryColor = stored.PrimaryColor,
                    SecondaryColor = stored.SecondaryColor,
                },
                stored.Slug);
        }
    }

    /// <summary>Recuerda el gym al que la persona acaba de entrar, para mostrar su marca la próxima vez.</summary>
    public async Task RememberAsync(MeGym? gym, CancellationToken cancellationToken = default)
    {
        if (gym is null)
        {
            return;
        }

        // El slug conocido solo sirve si sigue siendo el mismo gym.
        var slug = Gym is not null && Gym.Name == gym.Name ? Slug : null;
        Set(gym, slug);
        await StoreAsync(cancellationToken);
    }

    private void Set(MeGym gym, string? slug)
    {
        Gym = gym;
        Slug = slug;
        Changed?.Invoke();
    }

    private async Task StoreAsync(CancellationToken cancellationToken)
    {
        if (Gym is null)
        {
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(
                new Stored(Slug, Gym.Name, Gym.LogoUrl, Gym.PrimaryColor, Gym.SecondaryColor));
            await js.InvokeVoidAsync("localStorage.setItem", cancellationToken, StorageKey, json);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or JSDisconnectedException)
        {
            // Sin almacenamiento (ventana privada, datos bloqueados): la marca solo dura esta visita.
        }
    }

    private async Task<Stored?> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var raw = await js.InvokeAsync<string?>("localStorage.getItem", cancellationToken, StorageKey);
            return string.IsNullOrEmpty(raw) ? null : JsonSerializer.Deserialize<Stored>(raw);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or JSDisconnectedException
                                       or JsonException)
        {
            return null;
        }
    }

    private sealed record Stored(string? Slug, string Name, string? LogoUrl, string PrimaryColor, string SecondaryColor);
}
