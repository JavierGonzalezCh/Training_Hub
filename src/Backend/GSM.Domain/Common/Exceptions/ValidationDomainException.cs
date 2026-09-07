namespace GSM.Domain.Common.Exceptions;

/// <summary>Invariante de negocio violada al construir o mutar una entidad. → HTTP 422.</summary>
public sealed class ValidationDomainException : DomainException
{
    public ValidationDomainException(string message) : base(message)
    {
    }
}
