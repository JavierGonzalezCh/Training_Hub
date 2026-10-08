# Bugs conocidos

Defectos encontrados durante el replanteamiento del sistema (revisión iniciada el 2026-10-07).
**No se corrigen durante la revisión:** se resuelven cuando empiece el desarrollo, cada uno con un
test que lo reproduzca antes del arreglo.

Solo entran aquí los **defectos**: lo que hace algo distinto de lo que debería. Lo que falta o lo que
se decide cambiar va en la especificación del módulo.

| Id | Módulo | Resumen | Severidad | Estado |
|---|---|---|---|---|
| B1 | Routines | Editar una rutina borra todo el historial de entrenamiento del miembro | Alta | Abierto |
| B2 | Dashboard | "Ingresos del mes" compara el mes en curso contra el mes anterior completo | Media | Abierto |
| B3 | Memberships (UI) | El precio del plan sale con formato distinto según la pantalla | Baja | Por confirmar |
| B4 | Gyms (UI) | El campo "Aforo" promete medir qué tan lleno está el gym en tiempo real, y nada lo usa | Baja | Abierto |

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

## B2 — El comparativo de ingresos del dashboard engaña

- **Encontrado:** 2026-10-07, recorrido del panel. El 7 de octubre mostraba "−81 % vs mes anterior".
- **Causa:** `GetDashboardHandler` compara `GetMonthlyRevenueAsync` del mes en curso, que va por el
  día 7, contra el **mes anterior completo**.
- **Arreglo propuesto:** comparar contra los mismos días del mes anterior (del 1 al 7 de
  septiembre), usando `IClock.BusinessDayRange`.

## B3 — Formato del precio distinto según la pantalla

- **Encontrado:** 2026-10-07, diálogo "Asignar membresía".
- **Qué se vio:** el selector de plan mostraba `1,100,000 COP`, mientras la ficha del miembro muestra
  `120.000 COP`. El campo "Monto" muestra `1100000`, sin separadores.
- **Por confirmar:** el código de la opción usa `GsmFormat.Money`, igual que la ficha. La diferencia
  puede venir de cómo `MudSelect` pinta el valor elegido, o de la cultura con que se formatea.
  Reproducir antes de arreglar.

## B4 — El aforo promete una función que no existe

- **Encontrado:** 2026-10-07, revisión de Attendance.
- **Qué pasa:** en Configuración, el campo "Aforo aproximado" dice: "Con este dato calcularemos qué
  tan lleno está el gimnasio en tiempo real" (`GSM.Web/Pages/Settings/Index.razor`). `Gym.Capacity` se
  guarda (migración `Etapa12_7_GymCapacity`), pero **ningún caso de uso ni pantalla lo lee**.
- **Impacto:** el dueño llena un dato esperando algo que no pasa.
- **Arreglo propuesto:** construir la ocupación, que se decide en la revisión de Attendance, o quitar
  la promesa del texto mientras tanto.

