using GSM.Client.Generated;

namespace GSM.Member.UI.Auth;

/// <summary>
/// Bootstrap de la app del miembro. <c>/api/auth/me</c> no trae el <c>memberId</c> (solo el
/// <c>userId</c>), así que se resuelve en el primer arranque llamando a <c>/api/me/streak</c>
/// (server-resuelve "el miembro actual" y devuelve su <c>MemberId</c>) y desde ahí se pide la ficha
/// completa por <c>GET /api/members/{id}</c>.
/// </summary>
public sealed class MemberContext
{
    public MemberDetail? Detail { get; private set; }

    public Guid? MemberId => Detail?.Id;

    public bool IsLoaded => Detail is not null;

    public event Action? Changed;

    public void Set(MemberDetail detail)
    {
        Detail = detail;
        Changed?.Invoke();
    }

    public void Clear()
    {
        Detail = null;
        Changed?.Invoke();
    }
}
