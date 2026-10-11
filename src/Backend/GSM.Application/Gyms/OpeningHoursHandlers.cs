using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Abstractions;
using GSM.Application.Gyms.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Gyms;

/// <summary>Horario semanal del gym del token y cómo está ahora mismo.</summary>
public sealed class GetOpeningHoursHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymScheduleRepository schedule)
{
    public async Task<OpeningHoursResponse> HandleAsync(CancellationToken cancellationToken)
    {
        currentUser.RequireGymId();
        var weekly = await schedule.GetWeeklySlotsAsync(cancellationToken);
        var local = clock.ToBusinessLocal(clock.UtcNow);
        var today = DateOnly.FromDateTime(local);
        var specials = await schedule.ListSpecialDaysAsync(today, today.AddDays(OpeningSchedule.LookAheadDays), cancellationToken);
        var status = OpeningHoursSupport.BuildSchedule(weekly, specials).Evaluate(today, TimeOnly.FromDateTime(local));

        return new OpeningHoursResponse(
            weekly.Count > 0, OpeningHoursSupport.ToDays(weekly), OpeningHoursSupport.ToDto(status, today));
    }
}

/// <summary>Reemplaza el horario completo: los 7 días y el de festivos, cada uno con al menos un tramo.</summary>
public sealed class UpdateOpeningHoursHandler(
    ICurrentUserService currentUser,
    IGymScheduleRepository schedule,
    IUnitOfWork unitOfWork,
    GetOpeningHoursHandler reader,
    ILogger<UpdateOpeningHoursHandler> logger)
{
    public async Task<OpeningHoursResponse> HandleAsync(
        UpdateOpeningHoursRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var byDay = new Dictionary<OpeningDay, IReadOnlyList<TimeSlot>>();
        foreach (var day in request.Days)
        {
            if (!Enum.TryParse<OpeningDay>(day.Day, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                throw new ValidationDomainException($"Día desconocido: {day.Day}.");
            }

            if (byDay.ContainsKey(parsed))
            {
                throw new ValidationDomainException($"El día {day.Day} viene repetido.");
            }

            byDay[parsed] = OpeningSlots.Normalize(OpeningHoursSupport.ToSlots(day.Slots), DayLabel(parsed));
        }

        var missing = OpeningHoursSupport.AllDays.Where(d => !byDay.ContainsKey(d)).ToList();
        if (missing.Count > 0)
        {
            throw new ValidationDomainException(
                $"Falta el horario de: {string.Join(", ", missing.Select(DayLabel))}. Todos los días llevan al menos un tramo.");
        }

        foreach (var existing in await schedule.GetWeeklySlotsAsync(cancellationToken))
        {
            schedule.Remove(existing);
        }

        foreach (var (day, slots) in byDay)
        {
            foreach (var slot in slots)
            {
                schedule.Add(GymOpeningSlot.Create(gymId, day, slot));
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Horario de atención del gym {GymId} actualizado", gymId);

        return await reader.HandleAsync(cancellationToken);
    }

    internal static string DayLabel(OpeningDay day) => day switch
    {
        OpeningDay.Monday => "Lunes",
        OpeningDay.Tuesday => "Martes",
        OpeningDay.Wednesday => "Miércoles",
        OpeningDay.Thursday => "Jueves",
        OpeningDay.Friday => "Viernes",
        OpeningDay.Saturday => "Sábado",
        OpeningDay.Sunday => "Domingo",
        _ => "Festivos",
    };
}

/// <summary>Festivos de Colombia del año con el horario que de verdad aplica a cada uno.</summary>
public sealed class ListHolidaysHandler(
    ICurrentUserService currentUser,
    IGymScheduleRepository schedule)
{
    public const int MinYear = 2000;

    public const int MaxYear = 2100;

    public async Task<IReadOnlyList<HolidayResponse>> HandleAsync(int year, CancellationToken cancellationToken)
    {
        currentUser.RequireGymId();
        if (year is < MinYear or > MaxYear)
        {
            throw new ValidationDomainException($"El año debe estar entre {MinYear} y {MaxYear}.");
        }

        var weekly = await schedule.GetWeeklySlotsAsync(cancellationToken);
        var holidaySlots = weekly.Where(s => s.Day == OpeningDay.Holiday).Select(s => s.Slot).OrderBy(s => s.Opens).ToList();
        var specials = (await schedule.ListSpecialDaysAsync(new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), cancellationToken))
            .ToDictionary(d => d.Date);

        return ColombianHolidays.ForYear(year).Select(h =>
        {
            if (specials.TryGetValue(h.Date, out var adjusted))
            {
                return new HolidayResponse(
                    h.Date, h.Name, true, adjusted.IsClosed, OpeningHoursSupport.ToDto(adjusted.TimeSlots), adjusted.Note);
            }

            return new HolidayResponse(h.Date, h.Name, false, false, OpeningHoursSupport.ToDto(holidaySlots), null);
        }).ToList();
    }
}

/// <summary>Excepciones por fecha desde un día en adelante (por defecto, hoy).</summary>
public sealed class ListSpecialDaysHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymScheduleRepository schedule)
{
    public async Task<IReadOnlyList<SpecialDayResponse>> HandleAsync(
        DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        currentUser.RequireGymId();
        var start = from ?? clock.Today;
        var end = to ?? start.AddYears(1);
        if (end < start)
        {
            throw new ValidationDomainException("El rango de fechas es inválido.");
        }

        var days = await schedule.ListSpecialDaysAsync(start, end, cancellationToken);
        return days.Select(OpeningHoursSupport.ToResponse).ToList();
    }
}

/// <summary>Crea o reemplaza la excepción de una fecha. Cerrar el día completo exige confirmación explícita.</summary>
public sealed class UpsertSpecialDayHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymScheduleRepository schedule,
    IUnitOfWork unitOfWork,
    ILogger<UpsertSpecialDayHandler> logger)
{
    public async Task<SpecialDayResponse> HandleAsync(
        DateOnly date, UpsertSpecialDayRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        if (date < clock.Today)
        {
            throw new ValidationDomainException("No se pueden cargar excepciones en fechas pasadas.");
        }

        if (request.IsClosed && !request.ConfirmClosed)
        {
            throw new ValidationDomainException(
                "Cerrar el día completo exige confirmación: ese día nadie podrá entrar y las clases se cancelan.");
        }

        var slots = request.IsClosed ? [] : OpeningHoursSupport.ToSlots(request.Slots);

        var existing = await schedule.GetSpecialDayAsync(date, cancellationToken);
        GymSpecialDay day;
        if (existing is null)
        {
            day = GymSpecialDay.Create(gymId, date, request.IsClosed, slots, request.Note);
            schedule.Add(day);
        }
        else
        {
            // Los tramos viejos se reemplazan por nuevos: se eliminan los anteriores y se agregan los nuevos.
            schedule.Remove(existing);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            day = GymSpecialDay.Create(gymId, date, request.IsClosed, slots, request.Note);
            schedule.Add(day);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Excepción del {Date} cargada en el gym {GymId} ({State})",
            date, gymId, request.IsClosed ? "cerrado" : "otro horario");

        return OpeningHoursSupport.ToResponse(day);
    }
}

/// <summary>Quita la excepción de una fecha: ese día vuelve al horario que le toca.</summary>
public sealed class DeleteSpecialDayHandler(
    ICurrentUserService currentUser,
    IGymScheduleRepository schedule,
    IUnitOfWork unitOfWork,
    ILogger<DeleteSpecialDayHandler> logger)
{
    public async Task HandleAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var existing = await schedule.GetSpecialDayAsync(date, cancellationToken)
            ?? throw new NotFoundException("Ese día no tiene excepción.");

        schedule.Remove(existing);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Excepción del {Date} retirada del gym {GymId}", date, gymId);
    }
}

/// <summary>Horario del gym para la app del miembro.</summary>
public sealed class GetMyGymHoursHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymScheduleRepository schedule)
{
    public async Task<MemberGymHoursResponse> HandleAsync(CancellationToken cancellationToken)
    {
        currentUser.RequireGymId();
        var weekly = await schedule.GetWeeklySlotsAsync(cancellationToken);
        var local = clock.ToBusinessLocal(clock.UtcNow);
        var today = DateOnly.FromDateTime(local);
        var specials = await schedule.ListSpecialDaysAsync(today, today.AddDays(60), cancellationToken);
        var status = OpeningHoursSupport.BuildSchedule(weekly, specials).Evaluate(today, TimeOnly.FromDateTime(local));

        return new MemberGymHoursResponse(
            OpeningHoursSupport.ToDto(status, today),
            OpeningHoursSupport.ToDays(weekly),
            specials.Select(OpeningHoursSupport.ToResponse).ToList());
    }
}
