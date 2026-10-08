using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Abstractions;
using GSM.Application.Gyms.Contracts;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.Gyms;

/// <summary>
/// "Mi gimnasio" en la app del miembro: ubicación, contacto y redes del gym del token. Siempre el
/// gym de quien pregunta (nunca un id de la ruta), así que un miembro no puede leer el de otro.
/// </summary>
public sealed class GetMyGymHandler(
    ICurrentUserService currentUser,
    IGymRepository gyms)
{
    public async Task<MemberGymResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var gym = await gyms.GetByIdAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym del usuario no existe.");

        return new MemberGymResponse(
            gym.Name, gym.LogoUrl, gym.PrimaryColor, gym.SecondaryColor,
            gym.Address, gym.City, gym.Latitude, gym.Longitude,
            gym.Phone, gym.WhatsAppPhone,
            gym.InstagramUrl, gym.FacebookUrl, gym.TikTokUrl, gym.WebsiteUrl);
    }
}
