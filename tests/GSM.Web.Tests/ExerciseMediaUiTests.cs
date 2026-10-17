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

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            var withMedia = request.Method == HttpMethod.Get;
            var json = "{\"id\":\"" + ExerciseId + "\",\"name\":\"Remo\",\"primaryMuscle\":\"Back\",\"secondaryMuscles\":[],\"equipment\":[],"
                + "\"videoUrl\":" + (withMedia ? "\"https://files.test/v.mp4\"" : "null") + ","
                + "\"imageUrl\":" + (withMedia ? "\"https://files.test/i.png\"" : "null") + ","
                + "\"exerciseType\":\"Strength\",\"trackingType\":\"RepsWeight\",\"isUnilateral\":false,\"difficulty\":\"Intermediate\"}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static readonly Guid ExerciseId = Guid.Parse("00000000-0000-0000-0000-0000000000e1");

    [Fact]
    public async Task Editing_an_exercise_with_media_offers_remove_buttons_that_call_the_api_and_empty_the_field()
    {
        var handler = new RecordingHandler();
        Services.AddSingleton<IExercisesClient>(
            new ExercisesClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") }));
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogs.ShowAsync<ExerciseFormDialog>(
            "Editar", new DialogParameters { ["ExerciseId"] = (Guid?)ExerciseId }));

        provider.WaitForAssertion(() => Assert.NotNull(provider.Find("[data-testid='image-remove']")));
        Assert.NotNull(provider.Find("[data-testid='video-remove']"));

        provider.Find("[data-testid='image-remove']").Click();

        provider.WaitForAssertion(() =>
        {
            Assert.Contains($"DELETE /api/exercises/{ExerciseId}/image", handler.Calls);
            Assert.Empty(provider.FindAll("[data-testid='image-remove']"));
        });
        Assert.NotNull(provider.Find("[data-testid='video-remove']"));

        provider.Find("[data-testid='video-remove']").Click();

        provider.WaitForAssertion(() =>
        {
            Assert.Contains($"DELETE /api/exercises/{ExerciseId}/video", handler.Calls);
            Assert.Empty(provider.FindAll("[data-testid='video-remove']"));
        });
    }
}
