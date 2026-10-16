using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using GSM.Client;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Members;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>R9 — aviso de salud en el editor de rutinas: genérico para todo el staff, detalle solo con permiso.</summary>
public class RoutineHealthNoticeUiTests : TestContext
{
    private (FakeClient Fake, Guid MemberId) Setup(bool hasAlerts, bool withPermission, bool consentRequired = false)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("ana");
        auth.SetRoles("staff");
        if (withPermission)
        {
            auth.SetClaims(new Claim("perm", GsmPermissions.ViewMemberHealth));
        }

        var (members, fake) = Fake<IMembersClient>();
        fake.Handlers["MembersGET2Async"] = _ => new MemberDetail { HasHealthAlerts = hasAlerts };
        fake.Handlers["GetMemberHealthAsync"] = _ => new MemberHealthView
        {
            ConsentRequired = consentRequired,
            Health = consentRequired ? null : new MemberHealth { MedicalConditions = "Asma leve", TrainingRestrictions = "Sin saltos" },
        };
        Services.AddSingleton(members);
        return (fake, Guid.NewGuid());
    }

    [Fact]
    public void Staff_without_the_permission_sees_only_the_generic_notice_and_never_asks_for_the_detail()
    {
        var (fake, id) = Setup(hasAlerts: true, withPermission: false);

        var cut = RenderComponent<RoutineHealthNotice>(p => p.Add(x => x.MemberId, id));

        Assert.Contains("tiene alertas de salud", cut.Find("[data-testid='routine-health-notice']").TextContent);
        Assert.DoesNotContain("Asma", cut.Markup);
        Assert.DoesNotContain(fake.Calls, c => c.Method == "GetMemberHealthAsync");
    }

    [Fact]
    public void With_the_permission_the_detail_is_shown()
    {
        var (fake, id) = Setup(hasAlerts: true, withPermission: true);

        var cut = RenderComponent<RoutineHealthNotice>(p => p.Add(x => x.MemberId, id));

        Assert.Contains("Asma leve", cut.Find(".gsm-routine-health__conditions").TextContent);
        Assert.Contains("Sin saltos", cut.Find(".gsm-routine-health__restrictions").TextContent);
        Assert.Single(fake.Calls, c => c.Method == "GetMemberHealthAsync");
    }

    [Fact]
    public void Without_alerts_nothing_is_shown_and_health_is_not_queried()
    {
        var (fake, id) = Setup(hasAlerts: false, withPermission: true);

        var cut = RenderComponent<RoutineHealthNotice>(p => p.Add(x => x.MemberId, id));

        Assert.Empty(cut.FindAll("[data-testid='routine-health-notice']"));
        Assert.DoesNotContain(fake.Calls, c => c.Method == "GetMemberHealthAsync");
    }

    [Fact]
    public void Without_the_data_authorization_only_the_generic_notice_is_shown()
    {
        var (_, id) = Setup(hasAlerts: true, withPermission: true, consentRequired: true);

        var cut = RenderComponent<RoutineHealthNotice>(p => p.Add(x => x.MemberId, id));

        Assert.Contains("tiene alertas de salud", cut.Markup);
        Assert.Empty(cut.FindAll(".gsm-routine-health__conditions"));
    }
}
