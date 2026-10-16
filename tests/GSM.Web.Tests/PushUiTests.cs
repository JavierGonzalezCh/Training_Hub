using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.Member.UI.Pages;
using GSM.Panel.UI.Pages.Communications;
using GSM.UI.Shared.Components;
using GSM.UI.Shared.Push;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>N2: pedido de permiso de push en contexto y casilla de "anuncio importante" del editor.</summary>
public class PushUiTests : TestContext
{
    private sealed class FakeDevice : IPushNotifications
    {
        public PushPermissionState State { get; set; } = PushPermissionState.Default;

        public bool Installed { get; set; } = true;

        public bool Dismissed { get; set; }

        public string? NextToken { get; set; } = "tok-1";

        public int Requests { get; private set; }

        public Task<PushPermissionState> GetStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(State);

        public Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default) => Task.FromResult(Installed);

        public Task<string?> RequestTokenAsync(PushConfigResponse config, CancellationToken cancellationToken = default)
        {
            Requests++;
            return Task.FromResult(NextToken);
        }

        public Task<string?> GetTokenAsync(PushConfigResponse config, CancellationToken cancellationToken = default)
            => Task.FromResult(NextToken);

        public Task ForgetTokenAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> IsPromptDismissedAsync(CancellationToken cancellationToken = default) => Task.FromResult(Dismissed);

        public Task DismissPromptAsync(CancellationToken cancellationToken = default)
        {
            Dismissed = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePushClient : IPushClient
    {
        public bool Enabled { get; set; } = true;

        public List<RegisterPushDeviceRequest> Registered { get; } = [];

        public Task<PushConfigResponse> GetPushConfigAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new PushConfigResponse
            {
                Enabled = Enabled, ProjectId = "gsm", ApiKey = "k", AppId = "a", MessagingSenderId = "1", VapidKey = "v",
            });

        public Task<PushDeviceResponse> RegisterPushDeviceAsync(RegisterPushDeviceRequest body, CancellationToken cancellationToken = default)
        {
            Registered.Add(body);
            return Task.FromResult(new PushDeviceResponse { Id = Guid.NewGuid(), Platform = body.Platform });
        }

        public Task UnregisterPushDeviceAsync(UnregisterPushDeviceRequest body, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeAnnouncements : IAnnouncementsClient
    {
        public List<CreateAnnouncementRequest> Created { get; } = [];

        public List<UpdateAnnouncementRequest> Updated { get; } = [];

        public Task<AnnouncementResponse> AnnouncementsPOSTAsync(CreateAnnouncementRequest body, CancellationToken cancellationToken = default)
        {
            Created.Add(body);
            return Task.FromResult(new AnnouncementResponse { Id = Guid.NewGuid(), Title = body.Title, Body = body.Body, Status = "Draft" });
        }

        public Task<AnnouncementResponse> AnnouncementsPUTAsync(Guid id, UpdateAnnouncementRequest body, CancellationToken cancellationToken = default)
        {
            Updated.Add(body);
            return Task.FromResult(new AnnouncementResponse { Id = id, Title = body.Title, Body = body.Body, Status = "Draft" });
        }

        public Task<EmailAudienceResponse> EmailAudienceAsync(EmailAudienceRequest body, CancellationToken cancellationToken = default)
            => Task.FromResult(new EmailAudienceResponse { Recipients = 3 });

        public Task<PagedResultOfAnnouncementResponse> AnnouncementsGETAsync(string? status = null, int? page = null, int? pageSize = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<CommunicationsSettingsResponse> ReminderSettingsGETAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<CommunicationsSettingsResponse> ReminderSettingsPUTAsync(UpdateCommunicationsSettingsRequest body, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<BirthdaySettingsResponse> BirthdaySettingsGETAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<BirthdaySettingsResponse> BirthdaySettingsPUTAsync(UpdateBirthdaySettingsRequest body, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<AnnouncementResponse> AnnouncementsGET2Async(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<AnnouncementResponse> PublishAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<AnnouncementResponse> ArchiveAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<AnnouncementResponse> PinAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<AnnouncementResponse> UnpinAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<AnnouncementResponse> ScheduleAnnouncementAsync(Guid id, ScheduleAnnouncementRequest body, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<AnnouncementResponse> UnscheduleAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<AnnouncementResponse> ImagePOSTAsync(Guid id, FileParameter file = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<AnnouncementResponse> ImageDELETEAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<AudiencePreviewResponse> AudiencePreviewAsync(AnnouncementAudienceDto body, CancellationToken cancellationToken = default)
            => Task.FromResult(new AudiencePreviewResponse { Count = 3, Truncated = false, Members = [] });

        public Task<AudienceTagsResponse> AudienceTagsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AudienceTagsResponse { Tags = [] });

        public Task<AudiencePreviewResponse> RecipientsAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(new AudiencePreviewResponse { Count = 0, Truncated = false, Members = [] });
    }

    /// <summary>Planes y clases vacíos para el selector de audiencia del editor.</summary>
    private sealed class EmptyListHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json"),
            });
    }

    private readonly FakeDevice _device = new();
    private readonly FakePushClient _push = new();
    private readonly FakeAnnouncements _announcements = new();

    public PushUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IPushNotifications>(_device);
        Services.AddSingleton<IPushClient>(_push);
        Services.AddSingleton<IAnnouncementsClient>(_announcements);
        var emptyLists = new HttpClient(new EmptyListHandler()) { BaseAddress = new Uri("http://localhost") };
        Services.AddSingleton<IMembershipPlansClient>(new MembershipPlansClient(emptyLists));
        Services.AddSingleton<IClassesClient>(new ClassesClient(emptyLists));
        Services.AddScoped<PushRegistrationService>();
    }

    // ---------- pedido de permiso en contexto ----------

    [Fact]
    public void The_prompt_explains_why_and_enabling_it_registers_the_device_with_its_platform()
    {
        var cut = RenderComponent<PushPermissionPrompt>(p => p
            .Add(x => x.Platform, PushPlatforms.WebMember)
            .Add(x => x.Message, "Activa los avisos para que te recordemos tu clase."));

        Assert.Contains("Activa los avisos para que te recordemos tu clase.", cut.Markup);

        cut.Find("[data-action=enable]").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".gsm-push-prompt")));
        var registered = Assert.Single(_push.Registered);
        Assert.Equal("tok-1", registered.Token);
        Assert.Equal("web_member", registered.Platform);
        Assert.Equal(1, _device.Requests);
    }

    [Fact]
    public void Dismissing_the_prompt_hides_it_and_it_is_not_offered_again()
    {
        var first = RenderComponent<PushPermissionPrompt>(p => p.Add(x => x.Platform, PushPlatforms.WebMember));
        first.Find("[data-action=dismiss]").Click();

        first.WaitForAssertion(() => Assert.Empty(first.FindAll(".gsm-push-prompt")));
        Assert.True(_device.Dismissed);
        Assert.Empty(_push.Registered);

        var again = RenderComponent<PushPermissionPrompt>(p => p.Add(x => x.Platform, PushPlatforms.WebMember));
        Assert.Empty(again.FindAll(".gsm-push-prompt"));
    }

    [Fact]
    public void A_denied_permission_does_not_register_anything_and_explains_how_to_change_it()
    {
        _device.NextToken = null; // el navegador no dio permiso

        var cut = RenderComponent<PushPermissionPrompt>(p => p.Add(x => x.Platform, PushPlatforms.WebMember));
        cut.Find("[data-action=enable]").Click();

        cut.WaitForAssertion(() => Assert.Contains("No diste el permiso", cut.Markup));
        Assert.Empty(_push.Registered);
    }

    [Theory]
    [InlineData(false, true, true)] // Firebase sin configurar en el servidor: no se ofrece
    [InlineData(true, false, true)] // el panel solo lo pide con la app instalada
    [InlineData(true, true, false)] // ya activado o negado: nada que ofrecer
    public void The_prompt_is_not_shown_when_it_makes_no_sense(bool firebase, bool installed, bool askable)
    {
        _push.Enabled = firebase;
        _device.Installed = installed;
        _device.State = askable ? PushPermissionState.Default : PushPermissionState.Granted;

        var cut = RenderComponent<PushPermissionPrompt>(p => p
            .Add(x => x.Platform, PushPlatforms.WebPanel)
            .Add(x => x.RequireInstalled, true));

        Assert.Empty(cut.FindAll(".gsm-push-prompt"));
    }

    [Fact]
    public void On_an_iphone_without_the_app_installed_it_shows_how_to_install_instead_of_asking()
    {
        _device.State = PushPermissionState.NeedsInstall;

        var cut = RenderComponent<PushPermissionPrompt>(p => p.Add(x => x.Platform, PushPlatforms.WebMember));

        Assert.Contains("Añadir a pantalla de inicio", cut.Markup);
        Assert.Empty(cut.FindAll("[data-action=enable]"));
    }

    // ---------- casilla del anuncio importante ----------

    private IRenderedComponent<MudDialogProvider> OpenEditor(params string[] roles)
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("ana");
        auth.SetRoles(roles);

        RenderComponent<MudPopoverProvider>();
        var provider = RenderComponent<MudDialogProvider>();
        var draft = new AnnouncementResponse
        {
            Id = Guid.NewGuid(), Title = "Cierre por mantenimiento", Body = "Cerramos el lunes.", Status = "Draft",
            Category = "GymNews", Important = false,
        };
        var dialogs = Services.GetRequiredService<IDialogService>();
        provider.InvokeAsync(() => dialogs.ShowAsync<AnnouncementEditorDialog>(
            "Anuncio", new DialogParameters<AnnouncementEditorDialog> { { x => x.Existing, draft } }));
        return provider;
    }

    [Fact]
    public void The_owner_sees_the_important_checkbox_and_it_goes_in_the_request()
    {
        var provider = OpenEditor("owner");

        provider.WaitForAssertion(() => Assert.NotEmpty(provider.FindAll(".gsm-important-switch")));
        Assert.Empty(provider.FindAll(".gsm-important-hint"));

        provider.Find(".gsm-important-switch input").Change(true);
        provider.WaitForAssertion(() => Assert.Contains("Quedará registrado que lo marcaste tú", provider.Markup));

        provider.FindAll("button").First(b => b.TextContent.Contains("Guardar borrador")).Click();

        provider.WaitForAssertion(() => Assert.Single(_announcements.Updated));
        Assert.True(_announcements.Updated[0].Important);
    }

    [Fact]
    public void Staff_does_not_see_the_important_checkbox_and_never_sends_the_flag()
    {
        var provider = OpenEditor("staff");

        provider.WaitForAssertion(() => Assert.Contains("Guardar borrador", provider.Markup));
        Assert.Empty(provider.FindAll(".gsm-important-switch"));

        provider.FindAll("button").First(b => b.TextContent.Contains("Guardar borrador")).Click();

        provider.WaitForAssertion(() => Assert.Single(_announcements.Updated));
        Assert.Null(_announcements.Updated[0].Important);
    }

    // ---------- feed del miembro ----------

    private sealed class FeedApi : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            string json;
            if (path.EndsWith("/feed"))
            {
                json = """
                    {"items":[
                      {"id":"0190c0de-0000-7000-8000-000000000001","title":"Cerrado","body":"x","publishedAt":"2026-10-06T12:00:00Z","pinned":false,"important":true,"reactions":{"like":0,"celebrate":0,"strong":0},"myReaction":null},
                      {"id":"0190c0de-0000-7000-8000-000000000002","title":"Normal","body":"y","publishedAt":"2026-10-06T11:00:00Z","pinned":false,"important":false,"reactions":{"like":0,"celebrate":0,"strong":0},"myReaction":null}],
                     "page":1,"pageSize":30,"totalCount":2,"total":2}
                    """;
            }
            else if (path.Contains("unread"))
            {
                json = """{"announcements":0,"notifications":0}""";
            }
            else
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    [Fact]
    public void An_important_announcement_is_tagged_in_the_member_feed_and_comes_first()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("ana");
        auth.SetRoles("member");
        var http = new HttpClient(new FeedApi()) { BaseAddress = new Uri("http://localhost") };
        Services.AddSingleton<IMeClient>(new MeClient(http));
        var gym = new GSM.UI.Shared.Auth.GymContext();
        gym.Set(new MeResponse { FeatureFlags = new MeFeatureFlags { ShowCommunity = true } }, impersonating: false);
        Services.AddSingleton(gym);
        Services.AddSingleton<GSM.Member.UI.NewsUnreadState>();
        RenderComponent<MudPopoverProvider>();

        var cut = RenderComponent<News>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("article.gsm-feed-card").Count));
        var cards = cut.FindAll("article.gsm-feed-card");
        Assert.Equal("true", cards[0].GetAttribute("data-important"));
        Assert.Contains("Importante", cards[0].TextContent);
        Assert.Equal("false", cards[1].GetAttribute("data-important"));
        Assert.DoesNotContain("Importante", cards[1].TextContent);
    }
}

