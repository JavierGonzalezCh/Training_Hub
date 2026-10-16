using GSM.Application.Common.Abstractions;
using GSM.Application.Routines;
using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;
using GSM.Domain.Routines;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// R6: imagen y video de un ejercicio. El staff sube los de los ejercicios de su gym, el SuperAdmin los del sistema.
/// Cada test crea sus ejercicios (prefijo "R6 ") y los borra en un <c>finally</c>; el catálogo sembrado no se toca.
/// </summary>
public sealed class ExerciseMediaTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Staff_uploads_image_and_video_to_a_gym_exercise_and_replacing_deletes_the_previous_file()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gymId = Guid.CreateVersion7();
        var storage = new FakeStorage();
        try
        {
            using var scope = Staff(gymId);
            var id = await CreateAsync(scope, suffix);

            var first = await Video(scope, storage).HandleAsync(id, Mp4(), default);
            Assert.StartsWith("https://files.test/exercise-videos/", first.VideoUrl);
            Assert.Empty(storage.Deleted);

            var second = await Video(scope, storage).HandleAsync(id, Mp4(), default);
            Assert.NotEqual(first.VideoUrl, second.VideoUrl);
            Assert.Equal(first.VideoUrl, Assert.Single(storage.Deleted));

            var withImage = await Image(scope, storage).HandleAsync(id, Png(), default);
            Assert.StartsWith("https://files.test/exercises/", withImage.ImageUrl);
            Assert.Equal(second.VideoUrl, withImage.VideoUrl);   // subir una no pisa la otra
            Assert.Equal("R6 Ejercicio " + suffix, withImage.Name);
        }
        finally
        {
            await DeleteExercisesAsync(suffix);
        }
    }

    [Fact]
    public async Task Video_and_image_rules_reject_wrong_types_and_oversize_files()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var storage = new FakeStorage();
        try
        {
            using var scope = Staff(Guid.CreateVersion7());
            var id = await CreateAsync(scope, suffix);

            await Assert.ThrowsAsync<ValidationDomainException>(() =>
                Video(scope, storage).HandleAsync(id, new FileToStore(new MemoryStream([1]), "a.mov", "video/quicktime", 1), default));
            await Assert.ThrowsAsync<ValidationDomainException>(() =>
                Video(scope, storage).HandleAsync(id, new FileToStore(new MemoryStream([1]), "a.png", "image/png", 1), default));
            await Assert.ThrowsAsync<ValidationDomainException>(() =>
                Video(scope, storage).HandleAsync(id, new FileToStore(new MemoryStream([1]), "a.mp4", "video/mp4", 50L * 1024 * 1024 + 1), default));
            await Assert.ThrowsAsync<ValidationDomainException>(() =>
                Video(scope, storage).HandleAsync(id, new FileToStore(new MemoryStream(), "a.mp4", "video/mp4", 0), default));
            await Assert.ThrowsAsync<ValidationDomainException>(() =>
                Image(scope, storage).HandleAsync(id, Mp4(), default));

            Assert.Empty(storage.Saved);
        }
        finally
        {
            await DeleteExercisesAsync(suffix);
        }
    }

    [Fact]
    public async Task Staff_cannot_upload_media_to_a_system_exercise_or_to_another_gyms_exercise()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var storage = new FakeStorage();
        try
        {
            Guid systemId;
            using (var scope = SuperAdmin())
            {
                systemId = await CreateAsync(scope, suffix);
            }

            Guid gymExerciseId;
            using (var scope = Staff(Guid.CreateVersion7()))
            {
                gymExerciseId = await CreateAsync(scope, suffix + "g");

                await Assert.ThrowsAsync<ForbiddenDomainException>(() =>
                    Video(scope, storage).HandleAsync(systemId, Mp4(), default));
                await Assert.ThrowsAsync<ForbiddenDomainException>(() =>
                    Image(scope, storage).HandleAsync(systemId, Png(), default));
            }

            // Otro gym ni siquiera ve el ejercicio ajeno.
            using (var scope = Staff(Guid.CreateVersion7()))
            {
                await Assert.ThrowsAsync<NotFoundException>(() =>
                    Video(scope, storage).HandleAsync(gymExerciseId, Mp4(), default));
            }

            // El superadmin sube el del sistema, pero no el de un gym.
            using (var scope = SuperAdmin())
            {
                var uploaded = await Video(scope, storage).HandleAsync(systemId, Mp4(), default);
                Assert.StartsWith("https://files.test/exercise-videos/", uploaded.VideoUrl);
                await Assert.ThrowsAsync<ForbiddenDomainException>(() =>
                    Video(scope, storage).HandleAsync(gymExerciseId, Mp4(), default));
            }
        }
        finally
        {
            await DeleteExercisesAsync(suffix);
            await DeleteExercisesAsync(suffix + "g");
        }
    }

    [Fact]
    public async Task A_file_shared_with_a_duplicated_exercise_is_not_deleted_when_the_original_replaces_it()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gymId = Guid.CreateVersion7();
        var storage = new FakeStorage();
        try
        {
            using var scope = Staff(gymId);
            var id = await CreateAsync(scope, suffix, videoUrl: "https://files.test/exercise-videos/shared.mp4");

            var copy = await scope.ServiceProvider.GetRequiredService<DuplicateExerciseHandler>().HandleAsync(
                id, new DuplicateExerciseRequest(), default);
            Assert.Equal("https://files.test/exercise-videos/shared.mp4", copy.VideoUrl);

            // El original reemplaza el video: el archivo compartido sigue vivo porque la copia lo usa.
            await Video(scope, storage).HandleAsync(id, Mp4(), default);
            Assert.Empty(storage.Deleted);

            // Cuando la copia lo reemplaza, ya nadie lo usa y se borra.
            await Video(scope, storage).HandleAsync(copy.Id, Mp4(), default);
            Assert.Equal("https://files.test/exercise-videos/shared.mp4", Assert.Single(storage.Deleted));
        }
        finally
        {
            await DeleteExercisesAsync(suffix);
        }
    }

    // --- Ayudas ---

    private static async Task<Guid> CreateAsync(IServiceScope scope, string suffix, string? videoUrl = null)
        => (await scope.ServiceProvider.GetRequiredService<CreateExerciseHandler>().HandleAsync(
            new CreateExerciseRequest("R6 Ejercicio " + suffix, null, Muscle.Back, null, null, videoUrl, null), default)).Id;

    private static UploadExerciseImageHandler Image(IServiceScope scope, IFileStorage storage)
        => new(
            scope.ServiceProvider.GetRequiredService<ICurrentUserService>(),
            scope.ServiceProvider.GetRequiredService<IExerciseRepository>(),
            scope.ServiceProvider.GetRequiredService<ExerciseEquipmentResolver>(),
            storage,
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>());

    private static UploadExerciseVideoHandler Video(IServiceScope scope, IFileStorage storage)
        => new(
            scope.ServiceProvider.GetRequiredService<ICurrentUserService>(),
            scope.ServiceProvider.GetRequiredService<IExerciseRepository>(),
            scope.ServiceProvider.GetRequiredService<ExerciseEquipmentResolver>(),
            storage,
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>());

    private static FileToStore Png() => new(new MemoryStream([1, 2, 3]), "a.png", "image/png", 3);

    private static FileToStore Mp4() => new(new MemoryStream([1, 2, 3]), "a.mp4", "video/mp4", 3);

    private async Task DeleteExercisesAsync(string suffix)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var like = $"R6 Ejercicio {suffix}%";   // incluye las copias ("... (copia)")
        await db.Database.ExecuteSqlRawAsync("delete from routines.exercises where name like {0}", like);
    }

    private IServiceScope SuperAdmin()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), null, UserRole.SuperAdmin);
        return scope;
    }

    private IServiceScope Staff(Guid gymId)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), gymId, UserRole.Staff);
        return scope;
    }

    private sealed class FakeStorage : IFileStorage
    {
        public List<string> Saved { get; } = [];

        public List<string> Deleted { get; } = [];

        public Task<string> SaveAsync(FileToStore file, string folder, CancellationToken cancellationToken = default)
        {
            var url = $"https://files.test/{folder}/{Guid.NewGuid():N}";
            Saved.Add(url);
            return Task.FromResult(url);
        }

        public Task DeleteAsync(string publicUrl, CancellationToken cancellationToken = default)
        {
            Deleted.Add(publicUrl);
            return Task.CompletedTask;
        }
    }
}
