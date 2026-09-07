namespace GSM.Domain.Common.Exceptions;

/// <summary>Credenciales ausentes o inválidas (login fallido, refresh token no válido). → HTTP 401.</summary>
public sealed class UnauthorizedDomainException : DomainException
{
    public UnauthorizedDomainException(string message) : base(message)
    {
    }
}
