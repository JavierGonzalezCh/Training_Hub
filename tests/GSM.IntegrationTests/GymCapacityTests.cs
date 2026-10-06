using GSM.Application.Gyms;
using GSM.Application.Gyms.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

public sealed class GymCapacityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Owner_can_set_clear_and_is_validated_on_the_gym_capacity()
    {
        Guid gymId;
        using (var scope = factory.CreateScopeWithMigration())
        {
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var gym = Gym.Create("Aforo Gym", $"af-{Guid.NewGuid():N}"[..16]);
            db.Add(gym);
            await db.SaveChangesAsync();
            gymId = gym.Id;
        }

        GymResponse Update(int? capacity)
        {
            using var scope = factory.CreateScopeWithMigration();
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), gymId, UserRole.Owner);
            return scope.ServiceProvider.GetRequiredService<UpdateGymHandler>().HandleAsync(
                new UpdateGymRequest("Aforo Gym", null, null, null, null, "#B50016", "#0AAFCA", capacity), default)
                .GetAwaiter().GetResult();
        }

        Assert.Equal(80, Update(80).Capacity);
        Assert.Null(Update(null).Capacity);
        Assert.Throws<ValidationDomainException>(() => Update(0));
        Assert.Throws<ValidationDomainException>(() => Update(Gym.MaxCapacity + 1));
    }
}
