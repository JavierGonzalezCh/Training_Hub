namespace GSM.Domain.Equipment;

/// <summary>
/// Tipos de equipo. Conjunto cerrado; el detalle fino ("la máquina Hammer del segundo piso") va en la
/// descripción del ejercicio o en la máquina del inventario, no aquí. Pertenecía a Routines con el
/// nombre <c>Equipment</c>; pasó al módulo Equipment (E1) sin cambiar valores, así que el JSON de la
/// API (<c>Smith_machine</c>…) y los textos de Postgres (<c>smith_machine</c>…) siguen igual.
/// </summary>
public enum EquipmentType
{
    Barbell,
    Dumbbell,
    Kettlebell,
    Machine,
    Cable,
    Smith_machine,
    Bench,
    Rack,
    Resistance_band,
    Trx,
    Medicine_ball,
    Mat,
    Box,
    Jump_rope,
    Bodyweight,
    Cardio_machine,
}
