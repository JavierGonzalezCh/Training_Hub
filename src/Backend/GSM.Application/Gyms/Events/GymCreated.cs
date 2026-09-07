using GSM.Application.Common.Events;

namespace GSM.Application.Gyms.Events;

/// <summary>Se publica tras crear un gym (y su owner). Achievements lo consumirá para sembrar logros.</summary>
public sealed record GymCreated(Guid GymId, string Name) : IDomainEvent;
