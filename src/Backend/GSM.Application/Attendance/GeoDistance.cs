using GSM.Application.Gyms.Public;
using GSM.Domain.Attendance;

namespace GSM.Application.Attendance;

/// <summary>
/// Geocerca de la entrada con QR fijo (A6): distancia entre el teléfono del miembro y el gym. Solo se usa el
/// resultado; las coordenadas del miembro no se guardan ni se registran.
/// </summary>
public static class GeoDistance
{
    private const double EarthRadiusMeters = 6_371_000;

    /// <summary>Distancia en metros por la fórmula de haversine.</summary>
    public static double Meters(double lat1, double lon1, double lat2, double lon2)
    {
        static double Rad(double deg) => deg * Math.PI / 180;

        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2)
            + (Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Pow(Math.Sin(dLon / 2), 2));
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>
    /// Sin coordenadas (o inválidas) → <see cref="LocationCheck.Denied"/>; dentro del radio (inclusive) →
    /// <see cref="LocationCheck.Inside"/>; si no, <see cref="LocationCheck.Outside"/>.
    /// </summary>
    public static LocationCheck Evaluate(GymGeofence fence, double? latitude, double? longitude)
    {
        if (latitude is not { } lat || longitude is not { } lon
            || double.IsNaN(lat) || double.IsNaN(lon)
            || lat is < -90 or > 90 || lon is < -180 or > 180)
        {
            return LocationCheck.Denied;
        }

        return Meters(lat, lon, fence.Latitude, fence.Longitude) <= fence.RadiusMeters
            ? LocationCheck.Inside
            : LocationCheck.Outside;
    }

    /// <summary>Texto para el miembro cuando la entrada no cuenta para su racha ni sus logros.</summary>
    public static string? NoticeFor(LocationCheck? check) => check switch
    {
        LocationCheck.Denied => "Registramos tu entrada, pero sin tu ubicación no suma a tu racha ni a tus logros.",
        LocationCheck.Outside => "Registramos tu entrada, pero estás fuera del gimnasio, así que no suma a tu racha ni a tus logros.",
        _ => null,
    };
}
