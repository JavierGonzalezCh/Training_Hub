using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Members;
using GSM.UI.Shared.Formatting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>M1 — formulario de la ficha (validación de menor), historial de cambios y reglas de la ficha.</summary>
public class MemberProfileUiTests : TestContext
{
    /// <summary>Cliente de miembros de mentira: devuelve lo configurado y recuerda las llamadas de escritura.</summary>
    public class MembersStub : DispatchProxy
    {
        public MemberDetail? Detail { get; set; }
        public PagedResultOfMemberChangeItem? Changes { get; set; }
        public List<string> Writes { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod!.Name;
            if (name is "MembersPUTAsync" or "MembersPOSTAsync")
            {
                Writes.Add(name);
            }

            object? result = name switch
            {
                "MembersGET2Async" or "MembersPUTAsync" => Detail,
                "ChangesAsync" => Changes,
                _ => null,
            };

            var returnType = targetMethod.ReturnType;
            if (returnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            var inner = returnType.GetGenericArguments()[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [result]);
        }
    }

    private MembersStub Register(MemberDetail? detail = null, PagedResultOfMemberChangeItem? changes = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        this.AddTestAuthorization().SetAuthorized("ana");
        var client = DispatchProxy.Create<IMembersClient, MembersStub>();
        var stub = (MembersStub)(object)client;
        stub.Detail = detail;
        stub.Changes = changes;
        Services.AddSingleton(client);
        return stub;
    }

    private static MemberDetail Detail(DateOnly? birthDate, string? guardianName = null) => new()
    {
        Id = Guid.NewGuid(),
        FirstName = "Ana",
        LastName = "Prueba",
        DocumentType = "cc",
        DocumentNumber = "1001",
        BirthDate = birthDate,
        Phone = "+573001234567",
        Status = "Active",
        JoinedAt = new DateOnly(2026, 1, 1),
        GuardianName = guardianName,
        Tags = [],
    };

    private async Task<IRenderedComponent<MudDialogProvider>> OpenEditAsync(MemberDetail detail)
    {
        RenderComponent<MudPopoverProvider>();
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<MemberFormDialog> { { x => x.MemberId, detail.Id } };
        await provider.InvokeAsync(() => dialogs.ShowAsync<MemberFormDialog>("Editar miembro", parameters));
        provider.WaitForAssertion(() => Assert.Contains("Nombres", provider.Markup));
        return provider;
    }

    [Fact]
    public async Task Editing_a_minor_shows_the_guardian_section_and_blocks_saving_without_it()
    {
        var child = Detail(DateOnly.FromDateTime(DateTime.Today).AddYears(-10));
        var stub = Register(child);

        var provider = await OpenEditAsync(child);

        Assert.Contains("Datos del acudiente", provider.Markup);
        Assert.Contains("menor de edad", provider.Markup);

        await provider.InvokeAsync(() => provider.FindAll("button").First(b => b.TextContent.Contains("Guardar")).Click());

        provider.WaitForAssertion(() => Assert.Contains("El nombre del acudiente es obligatorio.", provider.Markup));
        Assert.Empty(stub.Writes);
    }

    [Fact]
    public async Task Editing_an_adult_does_not_show_the_guardian_section_and_saves()
    {
        var adult = Detail(new DateOnly(1990, 5, 5));
        var stub = Register(adult);

        var provider = await OpenEditAsync(adult);

        Assert.DoesNotContain("Datos del acudiente", provider.Markup);

        await provider.InvokeAsync(() => provider.FindAll("button").First(b => b.TextContent.Contains("Guardar")).Click());

        provider.WaitForAssertion(() => Assert.Equal(["MembersPUTAsync"], stub.Writes));
    }

    [Fact]
    public void Form_model_requires_the_five_guardian_fields_only_for_minors()
    {
        var today = new DateOnly(2026, 10, 8);
        var model = new MemberFormModel { BirthDate = new DateTime(2015, 1, 1) };

        Assert.True(model.IsMinor(today));
        Assert.Equal(
            ["nombre", "número de documento", "teléfono", "parentesco"],
            model.MissingGuardianFields(today));

        model.GuardianName = "Pedro";
        model.GuardianDocumentNumber = "9";
        model.GuardianPhone = "3001112233";
        model.GuardianRelationship = "Padre";
        Assert.Empty(model.MissingGuardianFields(today));

        model.BirthDate = new DateTime(2008, 10, 8); // cumple 18 hoy: ya no es menor
        model.GuardianName = null;
        Assert.False(model.IsMinor(today));
        Assert.Empty(model.MissingGuardianFields(today));
    }

    [Fact]
    public void Form_model_builds_a_request_with_normalized_tags_and_no_empty_strings()
    {
        var model = new MemberFormModel
        {
            FirstName = "Ana",
            LastName = "Uno",
            DocumentNumber = "1",
            BirthDate = new DateTime(1990, 5, 5),
            Phone = "  ",
            TagsText = "crossfit, mañanas ; vip,",
        };

        var request = model.ToCreateRequest();

        Assert.Null(request.Phone);
        Assert.Null(request.GuardianName);
        Assert.Null(request.GuardianDocumentType);
        Assert.Equal(["crossfit", "mañanas", "vip"], request.Tags);
        Assert.Equal(new DateOnly(1990, 5, 5), request.BirthDate);
    }

    [Fact]
    public void History_panel_lists_each_change_with_label_old_and_new_value_and_author()
    {
        var memberId = Guid.NewGuid();
        var stub = Register(changes: new PagedResultOfMemberChangeItem
        {
            Page = 1,
            PageSize = 20,
            Total = 3,
            Items =
            [
                new MemberChangeItem
                {
                    Id = Guid.NewGuid(), Field = "source", OldValue = null, NewValue = "walk_in",
                    ChangedByName = "Rita Recepción", ChangedByRole = "staff", Source = "panel",
                    ChangedAt = new DateTimeOffset(2026, 10, 8, 15, 0, 0, TimeSpan.Zero),
                },
                new MemberChangeItem
                {
                    Id = Guid.NewGuid(), Field = "status", OldValue = "inactive", NewValue = "active",
                    ChangedByName = "Dora Dueña", ChangedByRole = "owner", Source = "panel",
                    ChangedAt = new DateTimeOffset(2026, 10, 7, 15, 0, 0, TimeSpan.Zero),
                },
                new MemberChangeItem
                {
                    Id = Guid.NewGuid(), Field = "created", ChangedByName = null, ChangedByRole = "system", Source = "system",
                    ChangedAt = new DateTimeOffset(2026, 10, 6, 15, 0, 0, TimeSpan.Zero),
                },
            ],
        });
        Assert.NotNull(stub);
        RenderComponent<MudPopoverProvider>();

        var cut = RenderComponent<MemberHistoryPanel>(p => p.Add(x => x.MemberId, memberId));

        cut.WaitForAssertion(() => Assert.Contains("Cómo nos conoció", cut.Markup));
        var rows = cut.FindAll("tbody tr").Select(r => r.TextContent).ToList();
        Assert.Equal(3, rows.Count);
        Assert.Contains("Pasó por el gimnasio", rows[0]);
        Assert.Contains("Rita Recepción", rows[0]);
        Assert.Contains("Staff", rows[0]);
        Assert.Contains("Estado", rows[1]);
        Assert.Contains("Inactivo", rows[1]);
        Assert.Contains("Activo", rows[1]);
        Assert.Contains("Dueño", rows[1]);
        Assert.Contains("Ficha creada", rows[2]);
    }

    [Theory]
    [InlineData(null, "+573001234567", 1)]
    [InlineData("1990-05-05", "12345", 1)]
    [InlineData(null, "12345", 2)]
    [InlineData("1990-05-05", "+573001234567", 0)]
    [InlineData("1990-05-05", null, 0)]
    public void Incomplete_reasons_follow_the_birth_date_and_the_phone_format(string? birth, string? phone, int expected)
    {
        DateOnly? birthDate = birth is null ? null : DateOnly.Parse(birth);

        Assert.Equal(expected, MemberLabels.IncompleteReasons(birthDate, phone).Count);
    }
}
