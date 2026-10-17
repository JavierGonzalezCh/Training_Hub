using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Public;
using GSM.Application.Members.Abstractions;
using GSM.Application.Members.Contracts;
using GSM.Application.Members.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Members;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Members;

/// <summary>
/// Arma la ficha propia del miembro (M5). Lecturas del propio módulo y de Identity (nombre del entrenador).
/// La salud sale solo si hay autorización de datos; no se escribe en el registro de consultas de salud.
/// </summary>
public sealed class MyProfileComposer(
    IClock clock,
    IConsentRepository consents,
    IIdentityReader identity)
{
    public async Task<MyProfile> BuildAsync(Member m, CancellationToken cancellationToken)
    {
        var isMinor = m.IsMinor(clock.Today);
        var hasConsent = await MemberHealthRules.HasDataConsentAsync(consents, m.Id, cancellationToken);

        string? trainerName = null;
        if (m.TrainerUserId is { } trainerId)
        {
            trainerName = (await identity.GetUserAsync(trainerId, cancellationToken))?.FullName;
        }

        return new MyProfile(
            m.Id,
            m.FirstName,
            m.LastName,
            EnumText.Snake(m.DocumentType)!,
            m.DocumentNumber,
            m.BirthDate,
            EnumText.Snake(m.Gender),
            m.PhotoUrl,
            m.Phone,
            m.Email,
            m.Address,
            m.City,
            m.EmergencyContactName,
            m.EmergencyContactRelationship,
            m.EmergencyContactPhone,
            EnumText.Snake(m.Goal),
            m.GuardianName,
            m.GuardianPhone,
            m.GuardianRelationship,
            m.JoinedAt,
            isMinor,
            m.IsProfileIncomplete,
            m.TrainerUserId,
            trainerName,
            HealthConsentRequired: !hasConsent,
            CanEditHealth: hasConsent && !isMinor,
            Health: hasConsent ? MemberHealthRules.ToHealth(m) : null);
    }
}

/// <summary>Su ficha completa, incluida su propia salud (con el candado de la autorización de datos).</summary>
public sealed class GetMyProfileHandler(
    ICurrentUserService currentUser,
    IMemberReader reader,
    IMemberRepository members,
    MyProfileComposer composer)
{
    public async Task<MyProfile> HandleAsync(CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        var member = await MyMember.ResolveAsync(currentUser, reader, members, cancellationToken);
        return await composer.BuildAsync(member, cancellationToken);
    }
}

/// <summary>
/// El miembro edita sus datos de contacto, emergencia y salud. Sin cambios de identidad (403) ni de email
/// (422: pasa por la verificación). Cada campo que cambia deja su fila en el historial en la misma
/// transacción (rol <c>member</c>, origen <c>app</c>); los de salud, como filas sensibles. Los textos de salud
/// nunca se loguean.
/// </summary>
public sealed class UpdateMyProfileHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader reader,
    IMemberRepository members,
    IConsentRepository consents,
    MemberChangeRecorder changes,
    IUnitOfWork unitOfWork,
    MyProfileComposer composer,
    ILogger<UpdateMyProfileHandler> logger)
{
    public async Task<MyProfile> HandleAsync(UpdateMyProfileRequest request, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        var member = await MyMember.ResolveAsync(currentUser, reader, members, cancellationToken);
        var today = clock.Today;

        RejectIdentityChanges(member, request);
        RejectEmailChange(member, request);

        if (request.Health is not null)
        {
            if (member.IsMinor(today))
            {
                throw new ForbiddenDomainException(
                    "Eres menor de edad: tus datos de salud los actualiza el staff o tu acudiente en recepción.");
            }

            if (!await MemberHealthRules.HasDataConsentAsync(consents, member.Id, cancellationToken))
            {
                throw new ConflictException(
                    "Acepta la autorización de tratamiento de datos antes de guardar tus datos de salud.");
            }
        }

        var fieldChanges = new List<MemberFieldChange>(member.UpdateContact(
            request.Phone, request.Address, request.City,
            request.EmergencyContactName, request.EmergencyContactRelationship, request.EmergencyContactPhone,
            ParseGoal(request.Goal),
            today));

        if (request.Health is { } health)
        {
            fieldChanges.AddRange(member.UpdateHealth(
                new MemberHealthData(
                    health.MedicalConditions, health.Medications, health.TrainingRestrictions, health.MedicalClearanceDate),
                today));
        }

        changes.Record(member, fieldChanges);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Perfil del miembro {MemberId} actualizado por él mismo ({ChangedFields} campos)", member.Id, fieldChanges.Count);
        return await composer.BuildAsync(member, cancellationToken);
    }

    private static MemberGoal? ParseGoal(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return EnumText.TryParse<MemberGoal>(text, out var goal)
            ? goal
            : throw new ValidationDomainException($"Objetivo no válido: '{text}'.");
    }

    private static void RejectIdentityChanges(Member member, UpdateMyProfileRequest request)
    {
        var changed =
            Differs(request.FirstName, member.FirstName)
            || Differs(request.LastName, member.LastName)
            || Differs(request.DocumentNumber, member.DocumentNumber)
            || DocumentTypeDiffers(request.DocumentType, member.DocumentType)
            || (request.BirthDate is { } birth && birth != member.BirthDate);

        if (changed)
        {
            throw new ForbiddenDomainException(
                "Tu nombre, documento y fecha de nacimiento solo los cambia el staff del gimnasio.");
        }
    }

    private static void RejectEmailChange(Member member, UpdateMyProfileRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Email)
            && !string.Equals(request.Email.Trim(), member.Email, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationDomainException(
                "El correo no se cambia desde aquí: pide el cambio de correo para confirmar el nuevo con un código.");
        }
    }

    private static bool Differs(string? sent, string current)
        => !string.IsNullOrWhiteSpace(sent) && !string.Equals(sent.Trim(), current, StringComparison.Ordinal);

    private static bool DocumentTypeDiffers(string? sent, DocumentType current)
    {
        if (string.IsNullOrWhiteSpace(sent))
        {
            return false;
        }

        // Un tipo que no se reconoce no coincide con el guardado.
        return !(EnumText.TryParse<DocumentType>(sent, out var parsed) && parsed == current);
    }
}

/// <summary>Sube su propia foto (reemplaza la anterior y la borra del almacenamiento).</summary>
public sealed class UploadMyPhotoHandler(
    ICurrentUserService currentUser,
    IMemberReader reader,
    IMemberRepository members,
    IFileStorage storage,
    MemberChangeRecorder changes,
    IUnitOfWork unitOfWork,
    MyProfileComposer composer)
{
    public async Task<MyProfile> HandleAsync(FileToStore file, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        ImageUploadRules.EnsureValid(file);

        var member = await MyMember.ResolveAsync(currentUser, reader, members, cancellationToken);

        var previous = member.PhotoUrl;
        var url = await storage.SaveAsync(file, "member-photos", cancellationToken);

        member.SetPhoto(url);
        changes.Record(member, [new MemberFieldChange("photo_url", previous, url)]);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(previous))
        {
            await storage.DeleteAsync(previous, cancellationToken);
        }

        return await composer.BuildAsync(member, cancellationToken);
    }
}

// --- Mediciones propias (mismo candado que las del staff: autorización de datos; el menor no escribe) ---

/// <summary>Sus mediciones con el IMC. Sin la autorización de datos llegan bloqueadas (<c>ConsentRequired</c>).</summary>
public sealed class ListMyMeasurementsHandler(
    ICurrentUserService currentUser,
    IMemberReader reader,
    IMemberRepository members,
    IConsentRepository consents,
    IIdentityReader identity)
{
    public async Task<MemberMeasurementsView> HandleAsync(CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        var member = await MyMember.ResolveAsync(currentUser, reader, members, cancellationToken);

        if (!await MemberHealthRules.HasDataConsentAsync(consents, member.Id, cancellationToken))
        {
            return new MemberMeasurementsView(ConsentRequired: true, Items: []);
        }

        var rows = await members.GetMeasurementsAsync(member.Id, cancellationToken);
        var names = await AuthorNames.ResolveAsync(identity, rows.Select(r => r.RecordedByUserId), cancellationToken);
        return new MemberMeasurementsView(ConsentRequired: false, MeasurementMapper.ToItems(rows, names));
    }
}

/// <summary>
/// Añade una medición propia (peso y altura). 409 sin la autorización de datos; 403 si es menor de edad;
/// 422 con rangos o fecha inválidos. Los pesos y alturas no se loguean. Borrar una medición sigue siendo
/// del staff.
/// </summary>
public sealed class AddMyMeasurementHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader reader,
    IMemberRepository members,
    IConsentRepository consents,
    IIdentityReader identity,
    IUnitOfWork unitOfWork,
    ILogger<AddMyMeasurementHandler> logger)
{
    public async Task<MemberMeasurementItem> HandleAsync(
        AddMemberMeasurementRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var member = await MyMember.ResolveAsync(currentUser, reader, members, cancellationToken);
        var today = clock.Today;

        if (member.IsMinor(today))
        {
            throw new ForbiddenDomainException(
                "Eres menor de edad: tus mediciones las registra el staff o tu acudiente en recepción.");
        }

        if (!await MemberHealthRules.HasDataConsentAsync(consents, member.Id, cancellationToken))
        {
            throw new ConflictException(
                "Acepta la autorización de tratamiento de datos antes de guardar tus mediciones.");
        }

        var measurement = MemberMeasurement.Add(
            gymId, member.Id, request.MeasuredOn ?? today, today, request.WeightKg, request.HeightCm,
            currentUser.UserId, "member");
        members.Add(measurement);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Medición propia añadida por el miembro {MemberId}", member.Id);

        // El IMC y la altura vigente dependen de las demás filas: se relee la lista completa.
        var rows = await members.GetMeasurementsAsync(member.Id, cancellationToken);
        var names = await AuthorNames.ResolveAsync(identity, [measurement.RecordedByUserId], cancellationToken);
        return MeasurementMapper.ToItems(rows, names).First(i => i.Id == measurement.Id);
    }
}
