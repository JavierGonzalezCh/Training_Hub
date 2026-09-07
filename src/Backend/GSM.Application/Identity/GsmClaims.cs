namespace GSM.Application.Identity;

/// <summary>Nombres de los claims que viajan en el access token de GSM.</summary>
public static class GsmClaims
{
    public const string Subject = "sub";
    public const string GymId = "gym_id";
    public const string Role = "role";
    public const string Name = "name";
}
