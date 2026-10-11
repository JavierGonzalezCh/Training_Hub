using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.Member.UI.Pages;
using GSM.Panel.UI.Pages.Settings;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>N4/CO1: pantalla de preferencias del miembro y horario sin avisos en la configuración.</summary>
public class NotificationPreferencesUiTests : TestContext
{
    private sealed class FakeApi : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.PathAndQuery;
            Requests.Add((request.Method, path, body));

            string json;
            if (path.StartsWith("/api/me/notification-preferences"))
            {
                json = JsonSerializer.Serialize(new
                {
                    items = new object[]
                    {
                        new { category = "Membership", label = "Mi membresía", push = true, email = true },
                        new { category = "Classes", label = "Mis clases", push = true, email = true },
                        new { category = "Progress", label = "Mi progreso", push = true, email = true },
                        new { category = "News", label = "Novedades del gym", push = true, email = true },
                        new { category = "Promotions", label = "Promociones", push = false, email = false },
                    },
                });
            }
            else
            {
                json = JsonSerializer.Serialize(new { quietHoursEnabled = true, quietStart = "21:00:00", quietEnd = "07:00:00" });
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private readonly FakeApi _api = new();

    public NotificationPreferencesUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var http = new HttpClient(_api) { BaseAddress = new Uri("http://localhost") };
        Services.AddSingleton<IMeClient>(new MeClient(http));
        Services.AddSingleton<INotificationSettingsClient>(new NotificationSettingsClient(http));
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("ana");
        auth.SetRoles("owner", "member");
    }

    [Fact]
    public void Member_sees_the_five_categories_with_promotions_off_and_can_accept_them()
    {
        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<NotificationPreferences>();

        Assert.Equal(5, cut.FindAll("li.gsm-pref").Count);
        Assert.Contains("Promociones", cut.Markup);
        Assert.Contains("Apagado.", cut.Markup); // Promociones empieza apagado y se ofrece aceptarlo

        var promotions = cut.Find("li[data-category='Promotions']");
        promotions.QuerySelectorAll("input[type=checkbox]")[0].Change(true); // push

        var put = _api.Requests.Single(r => r.Method == HttpMethod.Put);
        Assert.Contains("\"category\":\"Promotions\"", put.Body);
        Assert.Contains("\"push\":true", put.Body);
        Assert.Contains("\"email\":false", put.Body);
    }

    [Fact]
    public void Quiet_hours_tab_shows_the_legal_note_and_saves_the_schedule()
    {
        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<NotificationsTab>();

        Assert.Contains("Ley 2300", cut.Markup);
        Assert.Contains("abogado", cut.Markup);

        cut.FindAll("button").Single(b => b.TextContent.Contains("Guardar cambios")).Click();

        var put = _api.Requests.Single(r => r.Method == HttpMethod.Put);
        Assert.Equal("/api/notifications/settings", put.Path);
        Assert.Contains("\"quietHoursEnabled\":true", put.Body);
        Assert.Contains("\"quietStart\":\"21:00:00\"", put.Body);
        Assert.Contains("\"quietEnd\":\"07:00:00\"", put.Body);
    }
}
