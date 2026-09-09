namespace GSM.API.Filters;

/// <summary>
/// Marca una acción o controller como exento del <see cref="SubscriptionGateFilter"/>: se sirve
/// aunque el gym esté <c>disabled</c> o <c>suspended</c>. Reservado a <c>/health</c> y al refresh
/// de token (para no dejar sesiones colgadas a mitad de una operación legítima).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class SubscriptionGateExemptAttribute : Attribute;
