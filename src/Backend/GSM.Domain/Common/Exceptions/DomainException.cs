namespace GSM.Domain.Common.Exceptions;

/// <summary>
/// Base de la jerarquía de errores de dominio. El middleware global de la API
/// traduce cada tipo concreto a su código HTTP. Cualquier excepción que no derive
/// de aquí se trata como 500.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message)
    {
    }

    protected DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
