using System.Net;
using System.Text;
using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Communications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>CO2: selector de audiencia del editor de anuncios ("Llegará a N miembros").</summary>
public class AnnouncementAudienceUiTests : TestContext
{
    private sealed class FakeAnnouncements : IAnnouncementsClient
    {
        public List<AnnouncementAudienceDto> Previews { get; } = [];

        public int ReachCount { get; set; } = 84;

        public Task<AudiencePreviewResponse> AudiencePreviewAsync(AnnouncementAudienceDto body, CancellationToken cancellationToken = default)
        {
            Previews.Add(body);
            return Task.FromResult(new AudiencePreviewResponse
            {
                Count = ReachCount,
                Truncated = false,
                Members = [new AudienceMemberItem { Id = Guid.NewGuid(), Name = "Ana Pérez" }],
            });
        }

        public Task<AudienceTagsResponse> AudienceTagsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AudienceTagsResponse { Tags = ["natación"] });

        public Task<AudiencePreviewResponse> RecipientsAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<AnnouncementResponse> AnnouncementsPOSTAsync(CreateAnnouncementRequest body, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<AnnouncementResponse> AnnouncementsPUTAsync(Guid id, UpdateAnnouncementRequest body, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<EmailAudienceResponse> EmailAudienceAsync(int? category = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<PagedResultOfAnnouncementResponse> AnnouncementsGETAsync(string? status = null, int? page = null, int? pageSize = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<CommunicationsSettingsResponse> ReminderSettingsGETAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<CommunicationsSettingsResponse> ReminderSettingsPUTAsync(UpdateCommunicationsSettingsRequest body, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<AnnouncementResponse> AnnouncementsGET2Async(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<AnnouncementResponse> PublishAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<AnnouncementResponse> ArchiveAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<AnnouncementResponse> PinAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<AnnouncementResponse> UnpinAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class ListsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            });
    }

    private readonly FakeAnnouncements _announcements = new();

    public AnnouncementAudienceUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IAnnouncementsClient>(_announcements);
        var http = new HttpClient(new ListsHandler()) { BaseAddress = new Uri("http://localhost") };
        Services.AddSingleton<IMembershipPlansClient>(new MembershipPlansClient(http));
        Services.AddSingleton<IClassesClient>(new ClassesClient(http));
    }

    [Fact]
    public void By_default_the_announcement_reaches_everyone_and_shows_how_many()
    {
        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<AnnouncementAudiencePicker>();

        Assert.Contains("84", cut.Find("[data-testid=audience-count]").TextContent);
        Assert.Contains("todos los activos", cut.Find("[data-testid=audience-count]").TextContent);
        Assert.Empty(cut.FindComponents<MudSelect<string>>());
        var preview = Assert.Single(_announcements.Previews);
        Assert.Empty(preview.Statuses);
    }

    [Fact]
    public void Turning_the_segment_on_shows_the_four_filters_and_recalculates_with_the_chosen_status()
    {
        RenderComponent<MudPopoverProvider>();
        AnnouncementAudienceDto? emitted = null;
        var cut = RenderComponent<AnnouncementAudiencePicker>(p => p
            .Add(x => x.AudienceChanged, EventCallback.Factory.Create<AnnouncementAudienceDto>(this, a => emitted = a)));

        cut.Find("[data-testid=audience-switch] input").Change(true);

        Assert.Equal(2, cut.FindComponents<MudSelect<string>>().Count); // estado y etiqueta
        Assert.Single(cut.FindComponents<MudSelect<Guid>>().Where(s => s.Instance.Label!.StartsWith("Plan")));

        var statuses = cut.FindComponents<MudSelect<string>>().First();
        _announcements.ReachCount = 12;
        cut.InvokeAsync(() => statuses.Instance.SelectedValuesChanged.InvokeAsync(["Expired", "Grace"]));

        cut.WaitForAssertion(() => Assert.Contains("12", cut.Find("[data-testid=audience-count]").TextContent));
        Assert.NotNull(emitted);
        Assert.Equal(["Expired", "Grace"], emitted!.Statuses.OrderBy(s => s));
        Assert.Equal(["Expired", "Grace"], _announcements.Previews.Last().Statuses.OrderBy(s => s));
    }

    [Fact]
    public void The_list_of_recipients_can_be_reviewed()
    {
        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<AnnouncementAudiencePicker>();
        cut.Find("[data-testid=audience-switch] input").Change(true);

        Assert.Empty(cut.FindAll("[data-testid=audience-list]"));
        cut.Find("[data-testid=audience-toggle-list]").Click();

        Assert.Contains("Ana Pérez", cut.Find("[data-testid=audience-list]").TextContent);
    }
}
