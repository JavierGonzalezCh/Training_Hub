using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace GSM.API.Auth;

public static class RateLimitPolicies
{
    public const string Login = "auth-login";

    /// <summary>Ventana fija por IP para los endpoints anónimos de auth: frena fuerza bruta sin molestar al uso normal.</summary>
    public static IServiceCollection AddGsmRateLimiting(this IServiceCollection services)
        => services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = SecurityEventLogging.OnRateLimited;

            options.AddPolicy(Login, context =>
            {
                var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });
        });
}
