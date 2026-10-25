using Bunit;
using Bunit.TestDoubles;
using GSM.UI.Shared.Components;
using GSM.UI.Shared.Routines;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>
/// Aviso de cambios sin guardar de los editores de página completa (plantillas, rutina del miembro, "Crear mi
/// rutina"): la guarda pregunta antes de salir solo si hay algo pendiente, y la huella del borrador detecta lo que
/// se edita a clics (días, ejercicios), no solo lo que se escribe.
/// </summary>
public class UnsavedChangesGuardTests : TestContext
{
    private (IRenderedComponent<MudDialogProvider> Provider, FakeNavigationManager Nav) Setup(Func<bool> isDirty)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var provider = RenderComponent<MudDialogProvider>();
        var nav = Services.GetRequiredService<FakeNavigationManager>();
        nav.NavigateTo("/routine-templates/new");
        RenderComponent<UnsavedChangesGuard>(p => p.Add(x => x.IsDirty, isDirty).Add(x => x.What, "la plantilla"));
        return (provider, nav);
    }

    [Fact]
    public void Without_changes_leaving_does_not_ask()
    {
        var (provider, nav) = Setup(() => false);

        nav.NavigateTo("/routine-templates");

        Assert.EndsWith("/routine-templates", nav.Uri);
        Assert.DoesNotContain("Cambios sin guardar", provider.Markup);
    }

    [Fact]
    public void With_changes_leaving_asks_and_staying_keeps_the_editor_open()
    {
        var (provider, nav) = Setup(() => true);

        _ = provider.InvokeAsync(() => nav.NavigateTo("/routine-templates"));

        provider.WaitForAssertion(() => Assert.Contains("se pierden los cambios de la plantilla", provider.Markup), TimeSpan.FromSeconds(5));
        provider.FindAll("button").First(b => b.TextContent.Contains("Seguir editando")).Click();
        // La navegación termina después de cerrar el diálogo y no vuelve a renderizar nada: se espera la URL.
        Assert.True(SpinWait.SpinUntil(() => nav.Uri.EndsWith("/routine-templates/new", StringComparison.Ordinal), TimeSpan.FromSeconds(5)), nav.Uri);
    }

    [Fact]
    public void With_changes_leaving_without_saving_goes_through()
    {
        var (provider, nav) = Setup(() => true);

        _ = provider.InvokeAsync(() => nav.NavigateTo("/routine-templates"));

        provider.WaitForAssertion(() => Assert.Contains("Cambios sin guardar", provider.Markup), TimeSpan.FromSeconds(5));
        provider.FindAll("button").First(b => b.TextContent.Contains("Salir sin guardar")).Click();
        // La navegación termina después de cerrar el diálogo y no vuelve a renderizar nada: se espera la URL.
        Assert.True(SpinWait.SpinUntil(() => nav.Uri.EndsWith("/routine-templates", StringComparison.Ordinal), TimeSpan.FromSeconds(5)), nav.Uri);
    }

    [Fact]
    public void The_draft_fingerprint_changes_with_click_edits_and_not_without_them()
    {
        var draft = RoutineDraft.Empty();
        var loaded = draft.Fingerprint();

        Assert.Equal(loaded, draft.Fingerprint());

        draft.AddDay();
        Assert.NotEqual(loaded, draft.Fingerprint());

        draft.Days.RemoveAt(draft.Days.Count - 1);
        Assert.Equal(loaded, draft.Fingerprint());

        draft.Name = "Fuerza";
        Assert.NotEqual(loaded, draft.Fingerprint());
    }
}
