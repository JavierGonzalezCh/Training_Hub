namespace GSM.Application.Routines.Contracts;

/// <summary>
/// Crear o editar una plantilla (R11). <c>Routine</c> es el cuerpo de una rutina (mismas reglas que al asignarla:
/// modo rotativo o semanal, días, líneas y series); los ids de días y líneas se ignoran. El nombre de la plantilla
/// es <c>Name</c>; el de <c>Routine</c> es el que llevará la rutina al asignarla.
/// </summary>
/// <summary><c>ImageUrl</c>: portada elegida de la galería (<c>GET /api/routine-templates/images</c>); nula = sin portada.</summary>
public sealed record SaveRoutineTemplateRequest(
    string Name, string? Description, SaveRoutineRequest Routine, string? ImageUrl = null);

/// <summary>Una imagen de la galería de portadas.</summary>
public sealed record RoutineImageResponse(string Key, string Title, string Url);

/// <summary>Guardar una rutina ya armada (de un miembro) como plantilla.</summary>
public sealed record SaveRoutineAsTemplateRequest(string Name, string? Description);

/// <summary>
/// <c>ImageUrl</c>: portada (nula = genérica). <c>BaseKey</c>: plantilla base de la que salió (RT1), nula si la creó
/// el gym. <c>UnavailableHere</c>: nombres de los ejercicios que hoy no se pueden hacer en este gym (le falta la
/// máquina o el equipo, Equipment §5) o que ya no son visibles; vacío si todo se puede hacer.
/// </summary>
public sealed record RoutineTemplateListItem(
    Guid Id,
    string Name,
    string? Description,
    string ScheduleMode,
    int DayCount,
    int ExerciseCount,
    DateTimeOffset CreatedAt,
    string? ImageUrl = null,
    string? BaseKey = null,
    IReadOnlyList<string>? UnavailableHere = null);

/// <summary>
/// <c>Routine</c> trae el contenido con la forma de una rutina (nombres de ejercicio, series, grupos musculares) para
/// reutilizar el editor; su <c>Id</c> es el de la plantilla, <c>MemberId</c> es vacío y los ids de días y líneas son
/// transitorios. <c>UnavailableExerciseCount</c> cuenta las líneas cuyo ejercicio ya no es visible para el gym
/// (aparecen con nombre "?"): hay que reemplazarlas antes de asignar.
/// </summary>
public sealed record RoutineTemplateResponse(
    Guid Id,
    string Name,
    string? Description,
    string ScheduleMode,
    int DayCount,
    int ExerciseCount,
    int UnavailableExerciseCount,
    RoutineResponse Routine,
    DateTimeOffset CreatedAt,
    string? ImageUrl = null,
    IReadOnlyList<string>? UnavailableHere = null);
