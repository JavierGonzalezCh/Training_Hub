# Bugs conocidos

Defectos encontrados durante el replanteamiento del sistema (revisión iniciada el 2026-10-07).
**No se corrigen durante la revisión:** se resuelven cuando empiece el desarrollo, cada uno con un
test que lo reproduzca antes del arreglo.

Solo entran aquí los **defectos**: lo que hace algo distinto de lo que debería. Lo que falta o lo que
se decide cambiar va en la especificación del módulo.

| Id | Módulo | Resumen | Severidad | Estado |
|---|---|---|---|---|
| B1 | Routines | Editar una rutina borra todo el historial de entrenamiento del miembro | Alta | **Corregido en R1** (v2) |
| B2 | Dashboard | "Ingresos del mes" compara el mes en curso contra el mes anterior completo | Media | **Corregido en P3** (v2) |
| B3 | Memberships (UI) | El precio del plan sale con formato distinto según la pantalla | Baja | **Corregido** (v2, tarea B3) |
| B4 | Gyms (UI) | El campo "Aforo" promete medir qué tan lleno está el gym en tiempo real, y nada lo usa | Baja | **Corregido en A5** (v2) |

---

## B1 — Editar una rutina borra el historial de entrenamiento

- **Encontrado:** 2026-10-07, revisión de Routines. Confirmado leyendo el código y las migraciones;
  todavía sin un test que lo reproduzca.
- **Qué pasa:** cualquier `PUT /api/routines/{id}`, aunque solo corrija el nombre, borra todos los
  `routine_exercise_logs` que el miembro registró con esa rutina, incluido el detalle por serie.
- **Causa:** `UpdateRoutineHandler` (`GSM.Application/Routines/RoutineHandlers.cs`) llama a
  `RemoveDaysOf(routine)` y después a `routine.Replace(draft)`, que vuelve a crear días y ejercicios
  con ids nuevos. Las llaves foráneas borran en cascada: `routine_days` → `routine_exercises`
  (`Etapa09_2_Routines`) → `routine_exercise_logs` (`Etapa09_3_RoutineLogs`,
  `OnDelete(DeleteBehavior.Cascade)` en `RoutineLogConfiguration`).
- **Impacto:** se pierde el progreso del miembro sin ningún aviso. También puede afectar a la racha y
  a los logros que dependen de lo registrado.
- **Arreglo propuesto:** editar en el lugar. Los ejercicios que no cambian conservan su id, los que
  se quitan se marcan como retirados en vez de borrarse, y la llave del registro deja de borrar en
  cascada (`Restrict`). Se decide junto con el rediseño de Routines (`GSM-modulo-routines.md`).
- **Corregido en R1 (2026-10-08, rama `v2`):** edición en el lugar con `retired_on`, FK `Restrict`,
  `exercise_id` en los registros. Test: `RoutineEditInPlaceTests`.
- **Hallazgo al reproducirlo:** sobre el código anterior, el `PUT` fallaba **siempre** con
  `DbUpdateConcurrencyException` (los hijos recreados traen Guid v7 desde el dominio y EF los trataba
  como existentes), con o sin registros. Es decir, editar una rutina devolvía 500 en lugar de borrar el
  historial; el borrado en cascada solo lo respaldaba el esquema. El `PUT` no tenía ningún test.

## B2 — El comparativo de ingresos del dashboard engaña

- **Encontrado:** 2026-10-07, recorrido del panel. El 7 de octubre mostraba "−81 % vs mes anterior".
- **Causa:** `GetDashboardHandler` compara `GetMonthlyRevenueAsync` del mes en curso, que va por el
  día 7, contra el **mes anterior completo**.
- **Arreglo propuesto:** comparar contra los mismos días del mes anterior (del 1 al 7 de
  septiembre), usando `IClock.BusinessDayRange`.

- **Corregido en P3 (2026-10-14, rama `v2`):** el dashboard compara contra los mismos días del mes anterior (`IPaymentReader.GetRevenueBetweenDaysAsync`, recortado si el mes anterior es más corto), con `DashboardRevenueComparisonTests`.

## B3 — Formato del precio distinto según la pantalla

- **Encontrado:** 2026-10-07, diálogo "Asignar membresía".
- **Qué se vio:** el selector de plan mostraba `1,100,000 COP`, mientras la ficha del miembro muestra
  `120.000 COP`. El campo "Monto" muestra `1100000`, sin separadores.
- **Por confirmar:** el código de la opción usa `GsmFormat.Money`, igual que la ficha. La diferencia
  puede venir de cómo `MudSelect` pinta el valor elegido, o de la cultura con que se formatea.
  Reproducir antes de arreglar.
- **Reproducido y corregido (2026-10-08, rama `v2`):** nada fijaba la cultura, así que cada campo usaba
  la del navegador. `MudNumericField` formateaba sin separadores (o con los de `en-US`), y el `MudSelect`
  del plan no tenía `ToStringFunc`, así que el valor elegido no pasaba por `GsmFormat.Money`. Arreglo:
  los campos de dinero usan `Culture = GsmFormat.Colombia` y `Format = "N0"`, y el selector usa
  `GsmFormat.Money`. Test: `MoneyFormatUiTests` (con la cultura del hilo en `en-US`).

## B4 — El aforo promete una función que no existe

- **Encontrado:** 2026-10-07, revisión de Attendance.
- **Qué pasa:** en Configuración, el campo "Aforo aproximado" dice: "Con este dato calcularemos qué
  tan lleno está el gimnasio en tiempo real" (`GSM.Web/Pages/Settings/Index.razor`). `Gym.Capacity` se
  guarda (migración `Etapa12_7_GymCapacity`), pero **ningún caso de uso ni pantalla lo lee**.
- **Impacto:** el dueño llena un dato esperando algo que no pasa.
- **Corregido en A5 (2026-10-15):** ocupación = personas distintas con entrada no anulada en las últimas 2 h (permanencia configurable), comparada con el aforo; tarjeta en el dashboard y nivel en `/app`. Test `OccupancyTests` (primero el que reproducía el bug).
- **Arreglo propuesto:** construir la ocupación, que se decide en la revisión de Attendance, o quitar
  la promesa del texto mientras tanto.


## B5 — Cuatro clientes del API sin registrar en DI

- **Encontrado:** 2026-10-19, recorrido en navegador (V1, escritorio).
- **Qué pasa:** `/routine-templates` se caía con "There is no registered service of type `IRoutineTemplatesClient`". Tampoco estaban registrados `IMembershipGroupsClient`, `IRoutineSettingsClient` ni `IPublicCommunicationsClient`, así que fallaban también los diálogos de grupos de membresía, guardar/asignar plantilla y la pestaña de ajustes de Rutinas. Los bUnit no lo vieron porque cada test registra sus propios dobles.
- **Corregido (2026-10-19):** registros en `GsmClientServiceCollectionExtensions`. Test `ClientRegistrationTests`: falla si cualquier interfaz `I*Client` generada queda sin registrar.

## B6 — "Rutinas por revisar" con fecha del año 1

- **Encontrado:** 2026-10-19, recorrido en navegador (V1, escritorio).
- **Qué pasa:** las rutinas que ya existían antes de R8 mostraban "1 ene 0001 · 739907 d de atraso". Npgsql escribe `DateOnly.MinValue` (el valor por defecto de `AddColumn`) como `'-infinity'`, no como `0001-01-01`, así que el `UPDATE` de `V2_R8.sql` no encontraba filas. En `gsm_test` no aparecía porque los tests crean rutinas nuevas.
- **Corregido (2026-10-19):** `V2_R8.sql` cubre los dos valores; la base local `gsm` se reparó a mano (9 filas). Nada estaba desplegado, así que no hace falta migración nueva.
- **Lección:** un relleno tras `AddColumn<DateOnly>` no nulo debe buscar `'-infinity'`.
