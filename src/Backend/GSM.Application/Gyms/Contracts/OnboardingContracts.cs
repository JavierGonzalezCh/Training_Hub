namespace GSM.Application.Gyms.Contracts;

/// <summary>
/// Dónde va el onboarding del gym (Gyms §5). <c>IsComplete</c> manda sobre el panel: mientras sea <c>false</c>
/// el frontend lleva todo a <c>/onboarding</c>. Una vez completo (o omitido) no vuelve a bloquear.
/// </summary>
/// <param name="IsComplete">Los pasos obligatorios (1 a 5) se cumplieron alguna vez, o el SuperAdmin lo omitió.</param>
/// <param name="WasSkipped">El SuperAdmin lo omitió (soporte).</param>
/// <param name="NextStep">Clave del primer paso obligatorio pendiente; <c>null</c> si no falta ninguno.</param>
/// <param name="Steps">Los 7 pasos, en orden, con si cada uno ya tiene su dato.</param>
/// <param name="ToComplete">Lo opcional que sigue pendiente, para la lista "Para completar" del dashboard.</param>
public sealed record OnboardingResponse(
    bool IsComplete,
    bool WasSkipped,
    string? SkipReason,
    DateTimeOffset? CompletedAt,
    string? NextStep,
    IReadOnlyList<OnboardingStepResponse> Steps,
    IReadOnlyList<OnboardingTodoResponse> ToComplete);

/// <param name="Key"><c>gym</c>, <c>location</c>, <c>hours</c>, <c>equipment</c>, <c>plans</c>, <c>entry</c> o <c>team</c>.</param>
/// <param name="Order">1 a 7.</param>
/// <param name="Required">Pasos 1 a 5.</param>
/// <param name="Done">Su dato existe de verdad (no basta con haber pulsado "Siguiente").</param>
public sealed record OnboardingStepResponse(string Key, int Order, bool Required, bool Done);

/// <param name="Key"><c>entry</c> (emparejar el kiosco), <c>team</c> (invitar staff) o <c>members</c> (primeros miembros).</param>
public sealed record OnboardingTodoResponse(string Key);

public sealed record SkipOnboardingRequest(string Reason);
