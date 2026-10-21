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
| B5 | Frontend | Cuatro clientes del API sin registrar en DI | Alta | **Corregido** (V1) |
| B6 | Routines | "Rutinas por revisar" con fecha del año 1 | Media | **Corregido** (V1) |
| B7 | UI (CSS) | `gsm.css` tiene 3 llaves sin cerrar: todo lo posterior a `.gsm-m-split` deja de aplicarse | Alta | **Corregido** (2026-10-20) |
| B8 | Member UI | El registro por invitación sale bien pero la pantalla vuelve al formulario vacío | Media | **Corregido** (2026-10-20) |
| B9 | Kiosco | Tras emparejar, el kiosco muestra "Se ha producido un error inesperado" | Media | **Corregido** (2026-10-20) |
| B10 | Member UI | Registrar series: el campo Reps viene precargado y lo escrito se añade detrás | Media | **Corregido** (2026-10-20) |
| B11 | Payments (UI) | La barra de caja no se actualiza al registrar un cobro | Baja | **Corregido** (2026-10-20) |
| B12 | UI | `MudSelect` sin `ToStringFunc`: GUID del plan y "1, 3" en los días de la clase | Baja | **Corregido** (2026-10-20) |
| B13 | UI | B3 incompleto: precios con coma de miles en tres pantallas | Baja | **Corregido, sin verificar en navegador** (2026-10-20) |
| B14 | Members (UI) | Ficha anonimizada: las tarjetas dicen "sin membresía" y "sin pagos" aunque existen | Baja | **Corregido** (2026-10-20) |
| B15 | SuperAdmin | Una impersonación sin finalizar queda "En curso" para siempre | Baja | **Corregido** (2026-10-20) |
| B16 | UI | Defectos menores de texto y navegación (ver detalle) | Baja | **Parcial** (2026-10-20) |

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

---

Del B7 en adelante: recorrido V1 del 2026-10-20 (navegador, escritorio y 375 px, base `gsm` en
`V2_Q7`). Lo que no es defecto sino mejora quedó en `docs/V1-RECORRIDO.md`.

## B7 — `gsm.css` con tres llaves sin cerrar

- **Qué pasa:** en `GSM.UI.Shared/wwwroot/gsm.css` (hacia la línea 3361) la regla `.gsm-m-split {`
  dentro de `@media (min-width: 960px)` no cierra, y tampoco cierran ese `@media` ni el
  `@media (max-width: 959.95px)` de AP7. Con el anidamiento de CSS, todo lo posterior queda como
  `.gsm-m-split …` y solo en escritorio: las reglas de celular de AP7 (pestañas de configuración que se
  desplazan, tablas apiladas, editor de rutinas) y las de "Mi progreso" (L7: pestañas, calendario de
  racha, leyenda).
- **Visto:** `/app/logros` a 375 px sin estilos: pestañas "RachaLogrosRanking" pegadas, calendario
  como texto corrido y desborde horizontal. En `/settings` a 375 px, `overflow-x` de las pestañas
  queda en `hidden` en vez de `auto`.
- **Origen:** commit `64c772e` (AP6).
- **Test sugerido:** uno que cuente llaves en `gsm.css` (sin comentarios) y exija saldo 0.

## B8 — El registro por invitación no confirma

- **Qué pasa:** en `/app/registro` el `POST /api/members/register` responde 201 y la sesión queda
  iniciada, pero la página se vuelve a montar al cambiar el estado de autenticación y se pierde
  `_done` (`Register.razor`): el miembro ve el formulario vacío en lugar de "Listo. Ya estás dentro".
  Si reintenta, recibe "La invitación ya se usó o expiró".
- **Relacionado:** al cargar la marca del gym, los campos se recrean y se pierde lo que se escribió en
  los primeros segundos.

## B9 — El kiosco muestra un error al emparejarse

- **Qué pasa:** después de emparejar, `GET /api/auth/me` responde 403 a la cuenta de kiosco y
  `SessionService.BootstrapAsync` (desde `MainLayout` del panel) no lo controla: aparece "Se ha
  producido un error inesperado" sobre la pantalla del QR. El QR funciona.
- **Relacionado:** en `KioskPair.razor`, un fallo de red (`HttpRequestException`) tampoco se controla
  (solo se atrapa `GsmApiException`).

## B10 — Registrar series: Reps precargado

- **Qué pasa:** en `/app/rutina`, el campo Reps de cada serie viene con la meta mínima (8) y lo que el
  miembro escribe se añade detrás: se guardaron 810 y 88 repeticiones. Una serie que no se hizo queda
  con 8 reps y cuenta como cumplida ("3 de 3 series cumplidas" con dos hechas). No hay tope
  razonable.
- **Relacionado:** al abrir el ejercicio, el aviso "El gym no tiene el equipo…" sale dos veces.

## B11 — La barra de caja no se actualiza al cobrar

- **Qué pasa:** con la caja abierta (base 50.000), tras registrar un cobro en efectivo de 8.000 la
  barra sigue diciendo "efectivo esperado 50.000 COP". Al recargar dice 58.000.

## B12 — `MudSelect` sin `ToStringFunc`

- "Nuevo gym" (`GymFormDialog`) y "Editar plan/precio" (`SubscriptionFormDialog`): el selector de
  plan muestra el GUID al abrir el diálogo, hasta que algo lo vuelve a pintar.
- "Nueva clase": "Días de la semana" muestra "1, 3" en vez de "Martes, Jueves" (se guarda bien).

## B13 — Formato de precio con coma (B3 incompleto)

- El paso Planes del onboarding ("15,000"), el selector de "Asignar membresía" ("Día — 15,000 COP",
  con "15.000 COP" en el resumen del mismo diálogo) y el precio de "Asignar plan de grupo"
  ("150,000").

## B14 — Ficha anonimizada con tarjetas vacías

- Tras "Anonimizar ahora", las tarjetas de la cabecera dicen "Sin membresía activa" y "Sin pagos
  todavía", pero la membresía sigue activa y el pago existe (las pestañas Membresías y Pagos los
  muestran). El aviso promete que se conservan.

## B15 — Impersonación eterna

- En `/admin/impersonations`, una impersonación del 13 sept 2026 (Gimnasio Demo, lectura) sigue
  "En curso" aunque el token dura 15 minutos. Las que no se cierran con "Finalizar" deberían darse por
  terminadas al vencer.

## B16 — Defectos menores de texto y navegación

- `/kiosk` con sesión de staff: "Configurar kiosco" lleva a Configuración → Gym, no a Entrada al gym
  → Kioscos.
- El chip del logro desbloqueado en el check-in (`MudChip` `Color.Info`) tiene texto blanco sobre
  #EEEBE6: ilegible.
- Staff recién dado de alta sale "ACTIVO" aunque no ha activado su cuenta (los Owners sí muestran
  "Pendiente").
- Asistencia repite la fecha ("20 oct 2026 · 20 oct 2026, 20:34").
- Plurales: "1 cupos", "Racha 1 días", "1 seleccionados".
- El panel de errores agrupa como uno solo dos errores distintos de Postgres (`release_at` y
  `required_permissions`): la huella parece ser solo el tipo de excepción.

---

## Correcciones del 2026-10-20

- **B7:** cerradas las llaves (`.gsm-m-split`, su `@media` y el de AP7). Test `StylesheetTests` cuenta llaves.
- **B8:** `Register.razor` navega a `/app` al terminar en vez de pintar un "Listo" que el remonte del router borraba. Sin test: bUnit no reproduce el remonte. El detalle de "se pierde lo escrito mientras carga la marca" no se atacó.
- **B9:** `MainLayout` no pide `/api/auth/me` a la cuenta de kiosco; `KioskPair` atrapa `HttpRequestException`.
- **B10:** las series arrancan vacías (el objetivo va de placeholder), Reps con tope 500, y guardar sin anotar nada avisa y no envía. Tests en `ExerciseAlternativesUiTests`. El aviso de equipo duplicado ("El gym no tiene el equipo…") **no** se revisó.
- **B11:** `Payments/Index` recarga `CashDrawerBar` tras registrar el pago.
- **B12:** `ToStringFunc` en los selectores de plan (`GymFormDialog`, `SubscriptionFormDialog`) y en los días de `ClassFormDialog`.
- **B13:** el código ya usaba `es-CO` + `N0`; la causa probable es que Blazor WASM no trae los datos de es-CO por defecto. Se añadió `BlazorWebAssemblyLoadAllGlobalizationData` a `GSM.Web.csproj`. **Sin verificar en navegador.**
- **B14:** `POST /api/members/{id}/anonymize` devolvía el `MemberDetail` sin membresía ni pagos; ahora lo compone igual que `GET`. Test en `MemberAnonymizationTests`.
- **B15:** `GET /api/admin/impersonations` da por terminada (derivado, no persistido) una sesión sin cerrar pasados los 15 min del token. Test en `ImpersonationTests`.
- **B16, hecho:** plurales de cupos/seleccionados/racha, chip del logro (`Color.Primary`), fecha repetida en Asistencia (vista móvil). **Pendiente:** "Configurar kiosco" no abre la pestaña Kioscos (Settings no admite enlace a pestaña), staff recién creado sale "ACTIVO" (`StaffResponse` no trae un campo de cuenta pendiente) y el agrupado de errores por huella.
