using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.Member.UI.Pages;
using GSM.Panel.UI.Pages.Communications;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using CommunicationsIndex = GSM.Panel.UI.Pages.Communications.Index;

namespace GSM.Web.Tests;

/// <summary>CO4: programar la publicación, imagen del anuncio en el panel y en el feed del miembro.</summary>
public class AnnouncementSchedulingUiTests : TestContext
{
    private static readonly DateTimeOffset Later = DateTimeOffset.UtcNow.AddDays(3);

    private sealed class FakeAnnouncements : IAnnouncementsClient
    {
        public List<AnnouncementResponse> Items { get; set; } = [];

        public List<(Guid Id, ScheduleAnnouncementRequest Body)> Scheduled { get; } = [];

        public List<Guid> Unscheduled { get; } = [];

        public List<Guid> Published { get; } = [];

        public List<UpdateAnnouncementRequest> Updated { get; } = [];

        public Task<AnnouncementResponse> ScheduleAnnouncementAsync(Guid id, ScheduleAnnouncementRequest body, CancellationToken cancellationToken = default)
        {
            Scheduled.Add((id, body));
            return Task.FromResult(Response(id, "Scheduled"));
        }

        public Task<AnnouncementResponse> UnscheduleAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Unscheduled.Add(id);
            return Task.FromResult(Response(id, "Draft"));
        }

        public Task<AnnouncementResponse> PublishAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Published.Add(id);
            return Task.FromResult(Response(id, "Published"));
        }

        public Task<AnnouncementResponse> AnnouncementsPUTAsync(Guid id, UpdateAnnouncementRequest body, CancellationToken cancellationToken = default)
        {
            Updated.Add(body);
            return Task.FromResult(Response(id, "Scheduled"));
        }

        public Task<PagedResultOfAnnouncementResponse> AnnouncementsGETAsync(string? status = null, int? page = null, int? pageSize = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new PagedResultOfAnnouncementResponse { Items = Items, Page = 1, PageSize = 50, Total = Items.Count });

        public Task<EmailAudienceResponse> EmailAudienceAsync(int? category = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new EmailAudienceResponse { Recipients = 3 });

        public Task<AudiencePreviewResponse> AudiencePreviewAsync(AnnouncementAudienceDto body, CancellationToken cancellationToken = default)
            => Task.FromResult(new AudiencePreviewResponse { Count = 3, Truncated = false, Members = [] });

        public Task<AudienceTagsResponse> AudienceTagsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AudienceTagsResponse { Tags = [] });

        public Task<AudiencePreviewResponse> RecipientsAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(new AudiencePreviewResponse { Count = 0, Truncated = false, Members = [] });

        public Task<AnnouncementResponse> AnnouncementsPOSTAsync(CreateAnnouncementRequest body, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<CommunicationsSettingsResponse> ReminderSettingsGETAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<CommunicationsSettingsResponse> ReminderSettingsPUTAsync(UpdateCommunicationsSettingsRequest body, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<BirthdaySettingsResponse> BirthdaySettingsGETAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<BirthdaySettingsResponse> BirthdaySettingsPUTAsync(UpdateBirthdaySettingsRequest body, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<AnnouncementResponse> AnnouncementsGET2Async(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<AnnouncementResponse> ArchiveAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<AnnouncementResponse> PinAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<AnnouncementResponse> UnpinAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<AnnouncementResponse> ImagePOSTAsync(Guid id, FileParameter file = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<AnnouncementResponse> ImageDELETEAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        private static AnnouncementResponse Response(Guid id, string status)
            => new() { Id = id, Title = "Titulo", Body = "Texto", Status = status, Category = "GymNews" };
    }

    private sealed class EmptyListHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json"),
            });
    }

    private sealed class ImageFeedApi : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            string json;
            if (path.EndsWith("/feed"))
            {
                json = """
                    {"items":[
                      {"id":"0190c0de-0000-7000-8000-000000000001","title":"Con foto","body":"x","publishedAt":"2026-10-06T12:00:00Z","pinned":false,"important":false,"imageUrl":"https://files.test/announcements/a.png","reactions":{"like":0,"celebrate":0,"strong":0},"myReaction":null},
                      {"id":"0190c0de-0000-7000-8000-000000000002","title":"Sin foto","body":"y","publishedAt":"2026-10-06T11:00:00Z","pinned":false,"important":false,"imageUrl":null,"reactions":{"like":0,"celebrate":0,"strong":0},"myReaction":null}],
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

    private readonly FakeAnnouncements _announcements = new();

    public AnnouncementSchedulingUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IAnnouncementsClient>(_announcements);
        var emptyLists = new HttpClient(new EmptyListHandler()) { BaseAddress = new Uri("http://localhost") };
        Services.AddSingleton<IMembershipPlansClient>(new MembershipPlansClient(emptyLists));
        Services.AddSingleton<IClassesClient>(new ClassesClient(emptyLists));
    }

    private IRenderedComponent<MudDialogProvider> OpenEditor(AnnouncementResponse existing)
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("ana");
        auth.SetRoles("staff");

        RenderComponent<MudPopoverProvider>();
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        provider.InvokeAsync(() => dialogs.ShowAsync<AnnouncementEditorDialog>(
            "Anuncio", new DialogParameters<AnnouncementEditorDialog> { { x => x.Existing, existing } }));
        return provider;
    }

    private static AnnouncementResponse Draft() => new()
    {
        Id = Guid.NewGuid(), Title = "Festivo", Body = "No abrimos el lunes.", Status = "Draft", Category = "GymNews",
    };

    private static AnnouncementResponse Scheduled() => new()
    {
        Id = Guid.NewGuid(), Title = "Festivo", Body = "No abrimos el lunes.", Status = "Scheduled", Category = "GymNews",
        ScheduledFor = Later,
    };

    // ---------- editor ----------

    [Fact]
    public void Turning_the_schedule_on_shows_the_date_and_time_and_the_button_becomes_Programar()
    {
        var provider = OpenEditor(Draft());

        provider.WaitForAssertion(() => Assert.Equal("Publicar", provider.Find("[data-testid=primary-action]").TextContent.Trim()));
        Assert.Empty(provider.FindAll("[data-testid=schedule-hint]"));

        provider.Find("input[data-testid=schedule-switch]").Change(true);

        provider.WaitForAssertion(() =>
        {
            Assert.Equal("Programar", provider.Find("[data-testid=primary-action]").TextContent.Trim());
            Assert.Contains("nunca antes", provider.Find("[data-testid=schedule-hint]").TextContent);
        });
    }

    [Fact]
    public void Scheduling_without_a_day_and_hour_asks_for_them_and_calls_nothing()
    {
        var provider = OpenEditor(Draft());
        provider.WaitForAssertion(() => Assert.NotEmpty(provider.FindAll("input[data-testid=schedule-switch]")));
        provider.Find("input[data-testid=schedule-switch]").Change(true);

        provider.Find("[data-testid=primary-action]").Click();

        provider.WaitForAssertion(() => Assert.Contains("Elige el día y la hora", provider.Markup));
        Assert.Empty(_announcements.Scheduled);
        Assert.Empty(_announcements.Published);
    }

    [Fact]
    public void A_scheduled_announcement_opens_with_the_schedule_on_and_saving_keeps_the_time()
    {
        var existing = Scheduled();
        var provider = OpenEditor(existing);

        provider.WaitForAssertion(() => Assert.Equal("Programar", provider.Find("[data-testid=primary-action]").TextContent.Trim()));
        provider.FindAll("button").First(b => b.TextContent.Contains("Guardar cambios")).Click();

        provider.WaitForAssertion(() => Assert.Single(_announcements.Scheduled));
        Assert.Single(_announcements.Updated);
        var (id, body) = _announcements.Scheduled[0];
        Assert.Equal(existing.Id, id);
        // Se conserva el instante (a la precisión del selector: minutos).
        Assert.True(Math.Abs((body.ScheduledFor - Later).TotalMinutes) < 1);
        Assert.Empty(_announcements.Unscheduled);
    }

    [Fact]
    public void Turning_the_schedule_off_on_a_scheduled_announcement_and_saving_cancels_it()
    {
        var existing = Scheduled();
        var provider = OpenEditor(existing);
        provider.WaitForAssertion(() => Assert.NotEmpty(provider.FindAll("input[data-testid=schedule-switch]")));

        provider.Find("input[data-testid=schedule-switch]").Change(false);
        provider.FindAll("button").First(b => b.TextContent.Contains("Guardar cambios")).Click();

        provider.WaitForAssertion(() => Assert.Equal([existing.Id], _announcements.Unscheduled));
        Assert.Empty(_announcements.Scheduled);
        Assert.Empty(_announcements.Published);
    }

    [Fact]
    public void The_editor_shows_the_current_image_and_lets_you_remove_it()
    {
        var existing = Draft();
        existing.ImageUrl = "https://files.test/announcements/a.png";
        var provider = OpenEditor(existing);

        provider.WaitForAssertion(() =>
            Assert.Equal("https://files.test/announcements/a.png", provider.Find("[data-testid=image-current]").GetAttribute("src")));

        provider.Find("[data-testid=image-remove]").Click();

        provider.WaitForAssertion(() => Assert.Empty(provider.FindAll("[data-testid=image-current]")));
    }

    // ---------- lista del panel ----------

    [Fact]
    public void The_list_marks_scheduled_announcements_with_their_time_and_shows_the_image()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("ana");
        auth.SetRoles("staff");
        _announcements.Items =
        [
            new AnnouncementResponse
            {
                Id = Guid.NewGuid(), Title = "Programado", Body = "x", Status = "Scheduled", Category = "GymNews",
                ScheduledFor = Later, ImageUrl = "https://files.test/announcements/a.png",
            },
            new AnnouncementResponse { Id = Guid.NewGuid(), Title = "Normal", Body = "y", Status = "Published", Category = "GymNews" },
        ];
        RenderComponent<MudPopoverProvider>();

        var cut = RenderComponent<CommunicationsIndex>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("article.gsm-announcement").Count));
        var cards = cut.FindAll("article.gsm-announcement");
        Assert.Contains("Programado", cards[0].QuerySelector(".gsm-tag--scheduled")!.TextContent);
        Assert.Contains("Sale el", cards[0].QuerySelector("[data-testid=scheduled-for]")!.TextContent);
        Assert.Equal(
            "https://files.test/announcements/a.png",
            cards[0].QuerySelector("[data-testid=announcement-image]")!.GetAttribute("src"));
        Assert.Null(cards[1].QuerySelector("[data-testid=announcement-image]"));
        Assert.Null(cards[1].QuerySelector("[data-testid=scheduled-for]"));
    }

    // ---------- feed del miembro ----------

    [Fact]
    public void The_member_feed_shows_the_image_only_when_the_announcement_has_one()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("ana");
        auth.SetRoles("member");
        var http = new HttpClient(new ImageFeedApi()) { BaseAddress = new Uri("http://localhost") };
        Services.AddSingleton<IMeClient>(new MeClient(http));
        var gym = new GSM.UI.Shared.Auth.GymContext();
        gym.Set(new MeResponse { FeatureFlags = new MeFeatureFlags { ShowCommunity = true } }, impersonating: false);
        Services.AddSingleton(gym);
        Services.AddSingleton<GSM.Member.UI.NewsUnreadState>();
        RenderComponent<MudPopoverProvider>();

        var cut = RenderComponent<News>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("article.gsm-feed-card").Count));
        var cards = cut.FindAll("article.gsm-feed-card");
        Assert.Equal(
            "https://files.test/announcements/a.png",
            cards[0].QuerySelector("[data-testid=feed-image]")!.GetAttribute("src"));
        Assert.Null(cards[1].QuerySelector("[data-testid=feed-image]"));
    }
}
