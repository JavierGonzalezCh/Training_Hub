namespace GSM.Domain.Common.Exceptions;

/// <summary>El usuario autenticado no puede operar sobre este recurso (rol o tenant ajeno). → HTTP 403.</summary>
public sealed class ForbiddenDomainException : DomainException
{
    public ForbiddenDomainException(string message) : base(message)
    {
    }
}
