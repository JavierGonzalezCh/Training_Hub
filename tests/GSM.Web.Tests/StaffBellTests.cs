using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.Panel.UI.Layout;
using GSM.UI.Shared.Auth;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace GSM.Web.Tests;

public class StaffBellTests : TestContext
{
    private static StaffNotificationItem Notice(
        string title, bool action = true, DateTimeOffset? resolvedAt = null, string? resolvedBy = null, string priority = "Normal")
        => new()
        {
            Id = Guid.NewGuid(),
            Type = "CheckinRejected",
            Title = title,
            Body = "Ana Pérez · Sin membresía",
            Link = "/members/1",
            Priority = priority,
            RequiresAction = action,
            CreatedAt = new DateTimeOffset(2026, 10, 6, 17, 0, 0, TimeSpan.Zero),
            ResolvedAt = resolvedAt,
            ResolvedByName = resolvedBy,
        };

    [Fact]
    public void An_attended_notice_says_who_and_when_and_has_no_attend_button()
    {
        var at = new DateTimeOffset(2026, 10, 6, 11, 12, 0, TimeSpan.FromHours(-5));
        var cut = RenderComponent<StaffNotificationList>(p => p.Add(
            x => x.Items, [Notice("Entrada rechazada", resolvedAt: at, resolvedBy: "Ana Recepción")]));

        Assert.Contains("Atendido por Ana Recepción", cut.Markup);
        Assert.DoesNotContain("Atender", cut.Markup.Replace("Atendido", string.Empty));
        Assert.DoesNotContain("Atender todo", cut.Markup);
    }

    [Fact]
    public void An_open_action_notice_offers_to_attend_it_and_attend_all()
    {
        StaffNotificationItem? resolved = null;
        var open = Notice("Entrada rechazada");
        var cut = RenderComponent<StaffNotificationList>(p => p
            .Add(x => x.Items, [open])
            .Add(x => x.OnResolve, item => resolved = item));

        cut.Find("button.gsm-bell-item__action").Click();

        Assert.Same(open, resolved);
        Assert.Contains("Atender todo", cut.Markup);
    }

    [Fact]
    public void An_informational_notice_is_marked_as_read_not_attended()
    {
        StaffNotificationItem? read = null;
        var info = Notice("Tu clase empieza pronto", action: false);
        var cut = RenderComponent<StaffNotificationList>(p => p
            .Add(x => x.Items, [info])
            .Add(x => x.OnRead, item => read = item));

        cut.Find("button.gsm-bell-item__action").Click();

        Assert.Same(info, read);
        Assert.DoesNotContain("Atender todo", cut.Markup);
    }

    private sealed class FakeStaffNotifications(int open) : IStaffNotificationsClient
    {
        public Task<PagedResultOfStaffNotificationItem> Notifications2Async(
            bool? openOnly = null, int? page = null, int? pageSize = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new PagedResultOfStaffNotificationItem { Items = [], Page = 1, PageSize = 8, Total = 0 });

        public Task<StaffNotificationCount> OpenCountAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new StaffNotificationCount { Count = open });

        public Task<StaffNotificationItem> ResolveAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ResolveAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Read2Async(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private IRenderedComponent<StaffBell> RenderBell(int open)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IStaffNotificationsClient>(new FakeStaffNotifications(open));
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("Ana");
        auth.SetPolicies(GsmPolicies.RequireStaff);
        RenderComponent<MudBlazor.MudPopoverProvider>();
        return RenderComponent<StaffBell>();
    }

    [Fact]
    public void The_bell_shows_the_open_counter()
    {
        var cut = RenderBell(open: 3);

        cut.WaitForAssertion(() => Assert.Contains("Avisos sin atender: 3", cut.Markup));
        Assert.Equal("3", cut.Find(".gsm-bell__count").TextContent);
    }

    [Fact]
    public void The_bell_shows_no_counter_when_nothing_is_open()
    {
        var cut = RenderBell(open: 0);

        cut.WaitForAssertion(() => Assert.Contains("Avisos sin atender: 0", cut.Markup));
        Assert.Empty(cut.FindAll(".gsm-bell__count"));
    }
}
