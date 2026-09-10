using GSM.Application.Common.Events;
using GSM.Domain.Gyms;

namespace GSM.Application.Gyms.Events;

/// <summary>
/// Se publica tras crear un gym (y su owner). Achievements lo consume para sembrar logros y
/// SuperAdmin para dar de alta su suscripción de prueba.
/// </summary>
public sealed record GymCreated(Guid GymId, string Name, GymPlan Plan) : IDomainEvent;
