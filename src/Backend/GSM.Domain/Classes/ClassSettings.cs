using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Classes;

/// <summary>
/// Reglas de reserva del gym (Classes v2, C3). Una fila por gym; si no hay fila valen las de
/// <see cref="Default"/> y no se persisten hasta el primer cambio (patrón perezoso, como
/// <c>checkin_settings</c>). La lista de espera (C4) sumará aquí el tiempo para confirmar una oferta.
/// </summary>
public sealed class ClassSettings : BaseEntity, ITenantEntity
{
    public const int DefaultCancelDeadlineHours = 2;
    public const int DefaultNoShowLimit = 3;
    public const int DefaultNoShowWindowDays = 30;
    public const int DefaultNoShowBlockDays = 7;
    public const int DefaultBookingWindowDays = 7;
    public const int DefaultMaxActiveBookings = 5;
    public const int DefaultWaitlistOfferMinutes = 60;

    public const int MaxCancelDeadlineHours = 72;
    public const int MaxNoShowLimit = 20;
    public const int MaxNoShowWindowDays = 180;
    public const int MaxNoShowBlockDays = 90;
    public const int MaxBookingWindowDays = 60;
    public const int MaxActiveBookingsLimit = 50;
    public const int MinWaitlistOfferMinutes = 5;
    public const int MaxWaitlistOfferMinutes = 720;

    private ClassSettings()
    {
    }

    public Guid GymId { get; private set; }

    /// <summary>Horas antes del inicio hasta las que el miembro puede cancelar sin que cuente como no-show.</summary>
    public int CancelDeadlineHours { get; private set; }

    /// <summary>Inasistencias (o cancelaciones tardías) que disparan el bloqueo, dentro de <see cref="NoShowWindowDays"/>. 0 = regla apagada: nunca se penaliza.</summary>
    public int NoShowLimit { get; private set; }

    public int NoShowWindowDays { get; private set; }

    /// <summary>Días sin poder reservar una vez alcanzado el límite.</summary>
    public int NoShowBlockDays { get; private set; }

    /// <summary>Las reservas de una sesión abren esta cantidad de días antes.</summary>
    public int BookingWindowDays { get; private set; }

    /// <summary>Reservas futuras activas que un miembro puede tener a la vez. 0 = sin tope.</summary>
    public int MaxActiveBookings { get; private set; }

    /// <summary>C4: minutos que tiene un miembro para confirmar el cupo que se le ofrece (nunca más allá del inicio de la clase).</summary>
    public int WaitlistOfferMinutes { get; private set; }

    public static ClassSettings Default(Guid gymId)
    {
        if (gymId == Guid.Empty)
        {
            throw new ValidationDomainException("Gym inválido.");
        }

        return new ClassSettings
        {
            GymId = gymId,
            CancelDeadlineHours = DefaultCancelDeadlineHours,
            NoShowLimit = DefaultNoShowLimit,
            NoShowWindowDays = DefaultNoShowWindowDays,
            NoShowBlockDays = DefaultNoShowBlockDays,
            BookingWindowDays = DefaultBookingWindowDays,
            MaxActiveBookings = DefaultMaxActiveBookings,
            WaitlistOfferMinutes = DefaultWaitlistOfferMinutes,
        };
    }

    public void Update(
        int cancelDeadlineHours, int noShowLimit, int noShowWindowDays,
        int noShowBlockDays, int bookingWindowDays, int maxActiveBookings, int? waitlistOfferMinutes = null)
    {
        Check(cancelDeadlineHours, 0, MaxCancelDeadlineHours, "El plazo de cancelación");
        Check(noShowLimit, 0, MaxNoShowLimit, "El límite de inasistencias");
        Check(noShowWindowDays, 1, MaxNoShowWindowDays, "La ventana de inasistencias");
        Check(noShowBlockDays, 1, MaxNoShowBlockDays, "Los días de bloqueo");
        Check(bookingWindowDays, 1, MaxBookingWindowDays, "La ventana de reserva");
        Check(maxActiveBookings, 0, MaxActiveBookingsLimit, "El máximo de reservas activas");
        var offerMinutes = waitlistOfferMinutes ?? WaitlistOfferMinutes;
        Check(offerMinutes, MinWaitlistOfferMinutes, MaxWaitlistOfferMinutes, "El tiempo para confirmar una oferta");

        CancelDeadlineHours = cancelDeadlineHours;
        NoShowLimit = noShowLimit;
        NoShowWindowDays = noShowWindowDays;
        NoShowBlockDays = noShowBlockDays;
        BookingWindowDays = bookingWindowDays;
        MaxActiveBookings = maxActiveBookings;
        WaitlistOfferMinutes = offerMinutes;
    }

    private static void Check(int value, int min, int max, string what)
    {
        if (value < min || value > max)
        {
            throw new ValidationDomainException($"{what} debe estar entre {min} y {max}.");
        }
    }
}
