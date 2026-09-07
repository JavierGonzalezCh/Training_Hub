namespace GSM.Domain.Common.Exceptions;

/// <summary>Recurso solicitado inexistente (o fuera del tenant actual). → HTTP 404.</summary>
public sealed class NotFoundException : DomainException
{
    public NotFoundException(string message) : base(message)
    {
    }

    public static NotFoundException For(string entity, object key)
        => new($"{entity} '{key}' no encontrado.");
}
