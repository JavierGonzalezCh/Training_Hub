using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Identity;

public sealed class UserProvisioning(
    UserManager<AppUser> userManager, GsmDbContext dbContext, IAccountLinkBuilder links) : IUserProvisioning
{
    public async Task<Guid> CreateGymUserAsync(
        string email,
        string fullName,
        string password,
        UserRole role,
        Guid gymId,
        CancellationToken cancellationToken = default)
    {
        if (role is UserRole.SuperAdmin or UserRole.Member)
        {
            throw new ArgumentException(
                "Usa CreateSuperAdminAsync o CreateMemberUserAsync para ese rol.", nameof(role));
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = await CreateAccountAsync(email, fullName, password);
        }
        else
        {
            EnsureUsable(user);
        }

        await GrantAsync(user.Id, gymId, role, cancellationToken);
        return user.Id;
    }

    public async Task<Guid> CreateMemberUserAsync(
        string email,
        string fullName,
        string password,
        Guid gymId,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = await CreateAccountAsync(email, fullName, password);
        }
        else
        {
            EnsureUsable(user);

            // La persona ya existe (es miembro de otro gym): se prueba que es ella con su contraseña.
            if (!await userManager.CheckPasswordAsync(user, password))
            {
                throw new UnauthorizedDomainException(
                    "Ese email ya tiene una cuenta. Ingresa su contraseña para agregar este gimnasio.");
            }
        }

        await GrantAsync(user.Id, gymId, UserRole.Member, cancellationToken);
        return user.Id;
    }

    public async Task<OwnerInvitation> InviteGymOwnerAsync(
        string email, string fullName, Guid gymId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is not null)
        {
            EnsureUsable(user);
            await GrantAsync(user.Id, gymId, UserRole.Owner, cancellationToken);

            // Ya tiene cuenta: el enlace (sin código) la lleva a entrar con su contraseña de siempre.
            return new OwnerInvitation(user.Id, links.ActivationUrl(user.Email!, null), HadAccount: true);
        }

        // Sin contraseña: no puede iniciar sesión hasta que la defina con el enlace de activación (72 h).
        user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = fullName,
            IsActive = true,
        };

        var result = await userManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            throw new ValidationDomainException(
                "No se pudo crear el usuario: " +
                string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        await GrantAsync(user.Id, gymId, UserRole.Owner, cancellationToken);

        var code = await userManager.GenerateUserTokenAsync(
            user, ActivationTokenProvider.ProviderName, ActivationTokenProvider.Purpose);
        return new OwnerInvitation(user.Id, links.ActivationUrl(user.Email!, code), HadAccount: false);
    }

    public async Task<Guid> CreateSuperAdminAsync(
        string email, string fullName, string password, CancellationToken cancellationToken = default)
    {
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            throw new ConflictException($"Ya existe un usuario con el email '{email}'.");
        }

        // Hay una sola cuenta de SuperAdmin (V5).
        if (await dbContext.Users.AnyAsync(u => u.IsSuperAdmin, cancellationToken))
        {
            throw new ConflictException("Ya existe una cuenta de SuperAdmin; solo puede haber una.");
        }

        try
        {
            var user = await CreateAccountAsync(email, fullName, password, isSuperAdmin: true);
            return user.Id;
        }
        catch (DbUpdateException ex) when (IsSingleSuperAdminViolation(ex))
        {
            // Carrera: otra petición creó el SuperAdmin entre la comprobación y el insert.
            throw new ConflictException("Ya existe una cuenta de SuperAdmin; solo puede haber una.");
        }
    }

    internal static bool IsSingleSuperAdminViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" } pg
        && pg.ConstraintName == AppUserConfiguration.SingleSuperAdminIndex;

    private async Task<AppUser> CreateAccountAsync(
        string email, string fullName, string password, bool isSuperAdmin = false)
    {
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = fullName,
            IsSuperAdmin = isSuperAdmin,
            IsActive = true,
        };

        var result = await userManager.CreateAsync(user, password);

        if (!result.Succeeded)
        {
            throw new ValidationDomainException(
                "No se pudo crear el usuario: " +
                string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        return user;
    }

    private static void EnsureUsable(AppUser user)
    {
        // Un SuperAdmin es global (sin accesos) y una cuenta desactivada no recibe accesos nuevos.
        if (user.IsSuperAdmin || !user.IsActive)
        {
            throw new ConflictException($"El email '{user.Email}' no puede recibir acceso a un gimnasio.");
        }
    }

    private async Task GrantAsync(Guid userId, Guid gymId, UserRole role, CancellationToken cancellationToken)
    {
        await GymAccessRules.EnsureNoAccessOfSameKindAsync(dbContext, userId, gymId, role, cancellationToken);

        dbContext.GymAccesses.Add(GymAccess.Grant(userId, gymId, role));
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Reglas compartidas de alta de accesos: como máximo uno de equipo y uno de miembro por persona y gym.</summary>
internal static class GymAccessRules
{
    public static async Task EnsureNoAccessOfSameKindAsync(
        GsmDbContext dbContext, Guid userId, Guid gymId, UserRole role, CancellationToken cancellationToken)
    {
        var isTeam = GymAccess.IsTeamRole(role);
        var exists = await dbContext.GymAccesses.AnyAsync(
            a => a.UserId == userId && a.GymId == gymId && a.IsTeam == isTeam, cancellationToken);

        if (exists)
        {
            throw new ConflictException(isTeam
                ? "Esa persona ya tiene un acceso de equipo (owner o staff) en este gimnasio."
                : "Esa persona ya es miembro de este gimnasio.");
        }
    }
}
