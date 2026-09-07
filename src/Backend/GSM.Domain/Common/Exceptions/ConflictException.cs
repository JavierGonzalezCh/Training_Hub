namespace GSM.Domain.Common.Exceptions;

/// <summary>Estado actual incompatible con la operación (duplicado, transición inválida). → HTTP 409.</summary>
public sealed class ConflictException : DomainException
{
    public ConflictException(string message) : base(message)
    {
    }
}
