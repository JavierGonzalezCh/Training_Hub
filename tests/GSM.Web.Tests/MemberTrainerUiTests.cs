using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Members;
using GSM.UI.Shared.Formatting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>M1b — el panel de entrenador asignado en la ficha del miembro.</summary>
public class MemberTrainerUiTests : TestContext
{
    public class MembersStub : DispatchProxy
    {
        public List<TrainerOption> Trainers { get; set; } = [];
        public List<AssignTrainerRequest> Assigned { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            object? result = null;
            switch (targetMethod!.Name)
            {
                case "ListMemberTrainersAsync":
                    result = (ICollection<TrainerOption>)Trainers;
                    break;
                case "AssignMemberTrainerAsync":
                    var request = (AssignTrainerRequest)args![1]!;
                    Assigned.Add(request);
                    result = new MemberDetail
                    {
                        Id = (Guid)args[0]!,
                        TrainerUserId = request.TrainerUserId,
                        TrainerName = Trainers.FirstOrDefault(t => t.UserId == request.TrainerUserId)?.FullName,
                        Tags = [],
                    };
                    break;
            }

            var inner = targetMethod.ReturnType.GetGenericArguments()[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [result]);
        }
    }

    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Coach = Guid.NewGuid();

    private MembersStub Register()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var client = DispatchProxy.Create<IMembersClient, MembersStub>();
        var stub = (MembersStub)(object)client;
        stub.Trainers =
        [
            new TrainerOption { UserId = Owner, FullName = "Olga Dueña", IsOwner = true },
            new TrainerOption { UserId = Coach, FullName = "Carlos Coach", IsOwner = false },
        ];
        Services.AddSingleton(client);
        return stub;
    }

    private static MemberDetail Member(Guid? trainer = null, string? trainerName = null)
        => new() { Id = Guid.NewGuid(), FirstName = "Ana", LastName = "Prueba", TrainerUserId = trainer, TrainerName = trainerName, Tags = [] };

    [Fact]
    public void Shows_no_trainer_and_lists_the_candidates_with_the_owner_marked()
    {
        Register();

        var cut = RenderComponent<MemberTrainerPanel>(p => p.Add(x => x.Member, Member()));

        cut.WaitForAssertion(() => Assert.Contains("Carlos Coach", cut.Markup));
        Assert.Contains("Sin entrenador", cut.Markup);
        Assert.Contains("Olga Dueña (dueño)", cut.Markup);
    }

    [Fact]
    public void Shows_the_name_of_the_assigned_trainer()
    {
        Register();

        var cut = RenderComponent<MemberTrainerPanel>(p => p.Add(x => x.Member, Member(Coach, "Carlos Coach")));

        cut.WaitForAssertion(() => Assert.Contains("Carlos Coach", cut.Markup));
    }

    [Fact]
    public void Save_is_disabled_until_the_selection_changes()
    {
        Register();
        var cut = RenderComponent<MemberTrainerPanel>(p => p.Add(x => x.Member, Member(Coach, "Carlos Coach")));
        cut.WaitForAssertion(() => Assert.Contains("Olga Dueña", cut.Markup));

        Assert.True(cut.Find("button").HasAttribute("disabled"));

        cut.Find("#member-trainer-select").Change(Owner.ToString());

        Assert.False(cut.Find("button").HasAttribute("disabled"));
    }

    [Fact]
    public void Choosing_a_trainer_and_saving_assigns_it_and_notifies_the_parent()
    {
        var stub = Register();
        MemberDetail? notified = null;
        var cut = RenderComponent<MemberTrainerPanel>(p => p
            .Add(x => x.Member, Member())
            .Add(x => x.OnChanged, (MemberDetail m) => notified = m));
        cut.WaitForAssertion(() => Assert.Contains("Carlos Coach", cut.Markup));

        cut.Find("#member-trainer-select").Change(Coach.ToString());
        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Equal(Coach, notified?.TrainerUserId));
        Assert.Equal(Coach, Assert.Single(stub.Assigned).TrainerUserId);
    }

    [Fact]
    public void Choosing_no_trainer_removes_it()
    {
        var stub = Register();
        var cut = RenderComponent<MemberTrainerPanel>(p => p.Add(x => x.Member, Member(Coach, "Carlos Coach")));
        cut.WaitForAssertion(() => Assert.Contains("Olga Dueña", cut.Markup));

        cut.Find("#member-trainer-select").Change(string.Empty);
        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Single(stub.Assigned));
        Assert.Null(stub.Assigned[0].TrainerUserId);
    }

    [Fact]
    public void The_history_labels_the_trainer_field()
        => Assert.Equal("Entrenador", MemberLabels.FieldLabel("trainer"));
}
