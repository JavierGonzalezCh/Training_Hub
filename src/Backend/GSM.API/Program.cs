using GSM.API.Auth;
using GSM.API.Filters;
using GSM.API.Middleware;
using GSM.Application;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Logs;
using GSM.API.Seed;
using GSM.Infrastructure;
using Scalar.AspNetCore;
using Serilog;

// Los mensajes por defecto de FluentValidation salen en el idioma del servidor y con el nombre de la propiedad de C#
// ("'Password' no debería estar vacío"); el cliente los muestra tal cual, así que se fijan en español y con nombres legibles.
FluentValidation.ValidatorOptions.Global.LanguageManager.Culture = new System.Globalization.CultureInfo("es");
FluentValidation.ValidatorOptions.Global.DisplayNameResolver = (_, member, _) => member?.Name switch
{
    "Email" => "Email",
    "Password" => "Contraseña",
    "NewPassword" => "Contraseña nueva",
    "CurrentPassword" => "Contraseña actual",
    "FullName" => "Nombre completo",
    "FirstName" => "Nombres",
    "LastName" => "Apellidos",
    "Phone" => "Teléfono",
    "Name" => "Nombre",
    _ => member?.Name,
};

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

const string CorsPolicy = "gsm-clients";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? [];

builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ICurrentUserService, CurrentUser>();
builder.Services.AddSingleton<ITraceContext, HttpTraceContext>();
builder.Services.AddSingleton<IClientInfo, HttpClientInfo>();

builder.Services.AddGsmAuth(builder.Configuration);
builder.Services.AddGsmRateLimiting();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services
    .AddControllers(options =>
    {
        options.Filters.Add<PasswordChangeGateFilter>();
        options.Filters.Add<SubscriptionGateFilter>();
        options.Filters.Add<ValidationActionFilter>();
        options.Filters.Add<TraceIdResultFilter>();
    })
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context => TraceIds.Stamp(context.HttpContext, context.ProblemDetails));
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddExceptionHandler<UnhandledExceptionHandler>();
builder.Services.AddOpenApi();

// Detrás de Traefik (Dokploy) la API recibe HTTP y la IP del proxy. Sin estas cabeceras el limitador de peticiones vería
// a todos los usuarios con la misma IP y la redirección a HTTPS podría entrar en bucle. Las listas de redes y proxies
// de confianza se vacían porque el contenedor solo es alcanzable por el proxy de la red interna de Docker: no se
// publica el puerto 8080 al exterior (ver docker-compose.yml).
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// Comando de un solo uso para crear el SuperAdmin en producción (DevSeeder no corre fuera de Development y crea un
// gym demo). Lee Seed:SuperAdmin (variables Seed__SuperAdmin__Email / __Password / __FullName), es idempotente y
// termina sin levantar el servidor: `dotnet GSM.API.dll --seed-superadmin`.
if (args.Contains("--seed-superadmin"))
{
    await using var seedScope = app.Services.CreateAsyncScope();
    await GSM.Infrastructure.Identity.IdentityDataSeeder.SeedAsync(seedScope.ServiceProvider);
    return;
}

app.UseForwardedHeaders();
app.UseMiddleware<TraceIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();

    await using var scope = app.Services.CreateAsyncScope();
    await DevSeeder.SeedAsync(scope.ServiceProvider);
}

// En desarrollo el emulador de Android entra por http://10.0.2.2:5175; redirigirlo a HTTPS lo manda a
// un certificado de "localhost" que no puede validar.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseCors(CorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ImpersonationAuditMiddleware>();

app.MapControllers();

app.Run();

public partial class Program;
