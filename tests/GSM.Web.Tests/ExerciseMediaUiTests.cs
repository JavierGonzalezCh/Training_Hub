using System.Net;
using System.Text;
using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Exercises;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>R6: el formulario del ejercicio ofrece subir imagen y video además de las URLs.</summary>
public class ExerciseMediaUiTests : TestContext
{
    private sealed class EmptyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            });
    }

    public ExerciseMediaUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IExercisesClient>(
            new ExercisesClient(new HttpClient(new EmptyHandler()) { BaseAddress = new Uri("http://localhost") }));
    }

    [Fact]
    public async Task Exercise_form_offers_image_and_video_upload_next_to_the_url_fields()
    {
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogs.ShowAsync<ExerciseFormDialog>("Nuevo ejercicio"));

        Assert.NotNull(provider.Find("[data-testid='video-upload']"));
        Assert.NotNull(provider.Find("[data-testid='image-upload']"));
        Assert.Contains("Subir video", provider.Markup);
        Assert.Contains("Subir imagen", provider.Markup);
        Assert.Contains("MP4, hasta 50 MB", provider.Markup);
        Assert.Contains("hasta 5 MB", provider.Markup);
    }
}
