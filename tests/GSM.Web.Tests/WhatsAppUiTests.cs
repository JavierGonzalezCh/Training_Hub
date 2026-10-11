using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Communications;
using GSM.Panel.UI.Pages.Settings;
using GSM.UI.Shared.Formatting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace GSM.Web.Tests;

public class WhatsAppUiTests : TestContext
{
    private sealed class FakeWhatsAppClient : IWhatsAppClient
    {
        public List<(Guid MemberId, string Kind)> Messages { get; } = [];

        public List<RegisterWhatsAppContactRequest> Contacts { get; } = [];

        public List<(string Kind, string Text)> Saved { get; } = [];

        public ICollection<WhatsAppMemberStateResponse> States { get; set; } = [];

        public Task<WhatsAppTemplatesResponse> TemplatesGETAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new WhatsAppTemplatesResponse
            {
                Variables = ["{nombre}", "{gym}"],
                Templates =
                [
                    new() { Kind = "ExpiringSoon", Text = "Hola {nombre}, vence pronto", DefaultText = "Hola {nombre}, tu plan en {gym} vence el {vence}.", IsDefault = false },
                    new() { Kind = "Expired", Text = "Venció", DefaultText = "Venció", IsDefault = true },
                ],
            });

        public Task<WhatsAppTemplateResponse> TemplatesPUTAsync(string kind, UpdateWhatsAppTemplateRequest body, CancellationToken cancellationToken = default)
        {
            Saved.Add((kind, body.Text));
            return Task.FromResult(new WhatsAppTemplateResponse { Kind = kind, Text = body.Text, DefaultText = "x", IsDefault = false });
        }

        public Task<WhatsAppMessageResponse> MessageAsync(Guid? memberId = null, string? kind = null, CancellationToken cancellationToken = default)
        {
            Messages.Add((memberId!.Value, kind!));
            return Task.FromResult(new WhatsAppMessageResponse { Url = "https://wa.me/573001234567?text=Hola", Message = "Hola", Phone = "+573001234567" });
        }

        public Task<WhatsAppContactResponse> ContactsAsync(RegisterWhatsAppContactRequest body, CancellationToken cancellationToken = default)
        {
            Contacts.Add(body);
            return Task.FromResult(new WhatsAppContactResponse
            {
                MemberId = body.MemberId, Kind = body.Kind, ContactedByUserId = Guid.NewGuid(),
                ContactedByName = "Ana Staff", ContactedAt = DateTimeOffset.UtcNow,
            });
        }

        public Task<ICollection<WhatsAppMemberStateResponse>> StatesAsync(IEnumerable<Guid>? memberIds = null, CancellationToken cancellationToken = default)
            => Task.FromResult(States);
    }

    private FakeWhatsAppClient Arrange()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var api = new FakeWhatsAppClient();
        Services.AddSingleton<IWhatsAppClient>(api);
        return api;
    }

    [Fact]
    public void A_member_without_a_valid_phone_gets_a_disabled_button_saying_the_phone_is_missing()
    {
        var api = Arrange();
        var id = Guid.NewGuid();
        api.States = [new WhatsAppMemberStateResponse { MemberId = id, HasValidPhone = false }];

        var cut = RenderComponent<WhatsAppButton>(p => p.Add(x => x.MemberId, id).Add(x => x.Kind, "ExpiringSoon"));

        Assert.NotNull(cut.Find($"[title='{WhatsAppLabels.MissingPhone}']"));
        Assert.All(cut.FindAll("button"), b => Assert.True(b.HasAttribute("disabled")));
    }

    [Fact]
    public void Clicking_the_button_builds_the_message_opens_whatsapp_and_registers_the_contact()
    {
        var api = Arrange();
        var id = Guid.NewGuid();
        api.States = [new WhatsAppMemberStateResponse { MemberId = id, HasValidPhone = true }];

        var cut = RenderComponent<WhatsAppButton>(p => p.Add(x => x.MemberId, id).Add(x => x.Kind, "ExpiringSoon").Add(x => x.Compact, true));
        var button = cut.Find("button");
        Assert.False(button.HasAttribute("disabled"));

        button.Click();

        cut.WaitForAssertion(() => Assert.Single(api.Contacts));
        Assert.Equal((id, "ExpiringSoon"), Assert.Single(api.Messages));
        Assert.Equal("ExpiringSoon", api.Contacts[0].Kind);
        var open = Assert.Single(JSInterop.Invocations, i => i.Identifier == "open");
        Assert.Equal("https://wa.me/573001234567?text=Hola", open.Arguments[0]);
        cut.WaitForAssertion(() => Assert.Contains("Ana contactó por WhatsApp · plantilla por vencer", cut.Markup));
    }

    [Fact]
    public void The_last_contact_is_shown_as_who_which_template_and_how_long_ago()
    {
        var api = Arrange();
        var id = Guid.NewGuid();
        api.States =
        [
            new WhatsAppMemberStateResponse
            {
                MemberId = id, HasValidPhone = true,
                LastContact = new WhatsAppContactResponse
                {
                    MemberId = id, Kind = "ExpiringSoon", ContactedByName = "Ana Staff",
                    ContactedAt = DateTimeOffset.UtcNow.AddDays(-2).AddMinutes(-5),
                },
            },
        ];

        var cut = RenderComponent<WhatsAppButton>(p => p.Add(x => x.MemberId, id).Add(x => x.Kind, "ExpiringSoon"));

        Assert.Contains("Ana contactó por WhatsApp · plantilla por vencer · hace 2 días", cut.Markup);
    }

    [Fact]
    public void Contact_note_wording_covers_minutes_hours_and_days()
    {
        var now = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal("hace 5 min", WhatsAppLabels.Ago(now.AddMinutes(-5), now));
        Assert.Equal("hace 3 h", WhatsAppLabels.Ago(now.AddHours(-3), now));
        Assert.Equal("hace 1 día", WhatsAppLabels.Ago(now.AddDays(-1), now));
        Assert.Equal("hace 2 días", WhatsAppLabels.Ago(now.AddDays(-2), now));
        Assert.Equal("Expired", WhatsAppLabels.KindForStatus("grace"));
        Assert.Equal("ExpiringSoon", WhatsAppLabels.KindForStatus("expiring"));
        Assert.Null(WhatsAppLabels.KindForStatus("current"));
    }

    [Fact]
    public void The_template_editor_shows_the_four_texts_and_saves_the_edited_one()
    {
        var api = Arrange();

        var cut = RenderComponent<WhatsAppTemplatesEditor>();

        cut.WaitForAssertion(() => Assert.Contains("Membresía por vencer", cut.Markup));
        Assert.Contains("Texto por defecto", cut.Markup); // la plantilla "Expired" no se ha editado
        Assert.Contains("{nombre}", cut.Markup);

        var area = cut.FindAll("textarea").First();
        area.Input("Hola {nombre}, renueva ya");
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Guardar").Click();

        cut.WaitForAssertion(() => Assert.Equal(("ExpiringSoon", "Hola {nombre}, renueva ya"), Assert.Single(api.Saved)));
    }
}
