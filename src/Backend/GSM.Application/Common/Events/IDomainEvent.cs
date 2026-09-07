namespace GSM.Application.Common.Events;

/// <summary>
/// Marca un mensaje que un módulo publica tras completar (y commitear) un caso de uso.
/// Los eventos son records inmutables y viven en <c>GSM.Application/{Módulo}/Events/</c>.
/// </summary>
public interface IDomainEvent;
