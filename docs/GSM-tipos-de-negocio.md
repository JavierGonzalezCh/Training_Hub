# Tipos de negocio y plantillas de configuración

> **Estado:** planificado el 2026-10-25. **T1 construida el 2026-10-26** (tipo de negocio en dominio, columna,
> DTOs, evento `GymCreated`, selector en `/admin`, columna y filtro en la cartera; sin plantillas todavía). T2–T7
> sin empezar. Depende de A8 (`GSM-modulo-attendance.md` §13), que sí está construido. Las referencias a código
> («hoy…») describen la rama `v2` al 2026-10-25.
>
> Nota de T1: no hay `V2_T1.sql`; la migración `V2_T1_GymType` de EF crea el enum `gyms.gym_type` y la columna
> `type` (`default 'gym'`) sin SQL a mano. `GymResponse`, `PortfolioItem` y `GymDetailResponse` llevan el tipo como
> `string` (regla del cliente NSwag para enums en respuestas); `GET /api/admin/gyms?type=` filtra la cartera.
>
> **T2 construida el 2026-10-26** (plantillas de módulos, entrada, membresías, rutinas y logros por tipo; marcas de
> «plantilla aplicada» en la ficha del SuperAdmin). Sin las opciones nuevas de T4/T5.
>
> **Revisión de código (2026-10-26):** `first_log` reacciona a `RoutineExerciseLogged` (cada ejercicio nuevo) y no solo al
> primero del día; `/api/auth/me` lee el modo de entrada en una sola consulta; la regla de calendario es única
> (`RoutineCalendar`); un día cuenta como hecho solo con registros de sus propias líneas; la siembra de logros es
> idempotente. Donde este documento dice `RoutineSessionLoggedCountsTheDayHandler`, hoy es
> `RoutineExerciseLoggedCountsTheDayHandler`.
>
> **T7 construida el 2026-10-26** (configuración recomendada, `BusinessNoun` y métricas por tipo). Con ella queda
> construido todo el plan (T1 a T7); lo que no entró está en «Pendiente» al final de §15.
>
> **T5 construida el 2026-10-26** (`ShowFrontDesk` y pasos obligatorios del onboarding derivados de las opciones).
> Quedan T7 (configuración recomendada, `BusinessNoun`, métricas por tipo).
>
> **T4 construida el 2026-10-26** (`RemoteTraining` y `TrainingEntryTrigger`, con la plantilla del entrenador personal).
> Sigue sin construir `ShowFrontDesk` y los pasos obligatorios por opciones (T5).
>
> **T3 construida el 2026-10-26** (planes sugeridos por tipo en el onboarding, textos del asistente para el entrenador
> personal y cambio de tipo por el Owner y por el SuperAdmin, sin el diálogo de «configuración recomendada», que es T7).
>
> **T6 construida el 2026-10-26** (cumplimiento de la rutina, §6, con las precisiones de abajo). Se hizo antes que
> T2–T5 porque es independiente.

## 1. Propósito

TrainingHub nació para gimnasios, pero lo quieren usar negocios que trabajan distinto: un box de entrenamiento
funcional gira alrededor de las clases, un estudio de yoga no usa rutinas, y un entrenador personal no tiene puerta ni
recepción porque sus clientes entrenan donde estén.

Hoy todos nacen con la misma configuración (la de un gimnasio) y el dueño tiene que descubrir qué apagar. Este
documento agrega un **tipo de negocio** al dar de alta el tenant y, según el tipo, una **plantilla**: los valores
iniciales de la configuración que ya existe, más unas pocas opciones nuevas para lo que hoy no se puede expresar.

## 2. Decisiones

| Tema | Decisión |
|---|---|
| Qué es el tipo | Una **plantilla de arranque**, no un modo. Decide los valores iniciales de la configuración y nada más. |
| El código no pregunta por el tipo | Ningún caso de uso, componente ni filtro hace `if (tipo == ...)`. Pregunta por la opción concreta («¿cuenta el día al registrar un ejercicio?»). Así cualquier negocio puede mezclar lo que necesite y un tipo nuevo es solo una plantilla nueva. |
| Única excepción | Lo que es **sobre el tipo** y no sobre el comportamiento: la etiqueta en el panel y en `/admin`, los filtros y métricas del SuperAdmin, y las **sugerencias** del onboarding (§8). |
| Después del alta | Todo se puede cambiar en Configuración, como hoy. La plantilla no queda «amarrada». |
| Cambiar el tipo | Se puede (§9), pero **no reaplica** la plantilla: pisaría lo que el dueño ya configuró. |
| «Self trainer» | **Fuera de alcance.** Una persona que entrena sola no es un negocio que gestiona miembros: no tiene staff, ni cobros, y sería dueño y miembro a la vez. Es un producto B2C distinto (§12). |
| Vocabulario | Se sigue diciendo **«miembro»** en todos los tipos (regla del proyecto). La palabra «gimnasio» en textos del panel se revisa en §10. |
| Gyms existentes | Quedan como tipo `gym`. La migración no cambia ninguna configuración. |

## 3. Catálogo de tipos

| Tipo (`gym_type`) | Etiqueta | Para quién | Centro de la operación |
|---|---|---|---|
| `gym` | Gimnasio | Sala de máquinas y pesas, con o sin clases. | La entrada en la puerta y las rutinas. |
| `box` | Box / entrenamiento funcional | CrossFit, funcional, HIIT. Grupos pequeños con coach. | Las clases (el WOD del día) y los récords. |
| `studio` | Estudio | Yoga, pilates, spinning, baile, barre. | Las clases con cupo y la tiquetera. |
| `personal_trainer` | Entrenador personal | Entrenador independiente o pequeño equipo, presencial u online, sin sede propia o con una sede que no controla. | Las rutinas personalizadas y su cumplimiento. |
| `academy` | Academia | Artes marciales, natación, escuelas deportivas. | Clases por nivel y la permanencia (meses como miembro). |
| `other` | Otro | Lo que no encaje. | Igual que `gym`. |

El orden en el selector es el de la tabla; `gym` va preseleccionado.

## 4. Qué configura la plantilla

Todo lo que sigue ya existe salvo las filas marcadas **(nueva)**, que se definen en §5. «Igual» quiere decir el valor
por defecto de hoy.

### 4.1 Módulos (`gyms.gym_feature_flags`)

| Opción | Hoy por defecto | gym | box | studio | personal_trainer | academy |
|---|---|---|---|---|---|---|
| `ShowRoutines` (Rutinas) | sí | sí | sí | **no** | sí | **no** |
| `ShowClasses` (Clases) | sí | sí | sí | sí | **no** | sí |
| `ShowProgress` (Progreso y racha) | sí | sí | sí | sí | sí | sí |
| `ShowGamification` (Logros) | sí | sí | sí | sí | sí | sí |
| `ShowCommunity` (Comunidad) | sí | sí | sí | sí | **no** | sí |
| `CheckinQr` (Check-in con QR) | sí | sí | sí | sí | **no** | sí |
| `MembersCanCreateRoutines` | no | no | no | no | no | no |
| `MembersCanReportEquipment` | sí | sí | sí | **no** | **no** | **no** |
| `ShowRanking` (Ranking de constancia) | sí | sí | sí | sí | **no** | sí |
| `ShowFrontDesk` **(nueva)** | — | sí | sí | sí | **no** | sí |

El entrenador personal apaga la comunidad y el ranking porque con pocos clientes, que además no se conocen entre sí,
un ranking público se siente vacío o expone a la persona. Los puede encender después.

### 4.2 Entrada (`attendance.checkin_settings`)

| Opción | Hoy por defecto | gym | box | studio | personal_trainer | academy |
|---|---|---|---|---|---|---|
| `Mode` (QR rotativo o cartel fijo) | rotativo | rotativo | **fijo** | **fijo** | — | **fijo** |
| `ByDocument` (documento en el kiosco) | no | no | no | no | no | no |
| `AssumedStayMinutes` (permanencia para el aforo) | 120 | 120 | **60** | **60** | — | **90** |
| `RemoteTraining` **(nueva)** | — | no | no | no | **sí** | no |
| `TrainingEntryTrigger` **(nueva)** | — | `start` | `start` | `start` | **`first_log`** | `start` |

Box, estudio y academia usan el **cartel fijo** porque sus miembros llegan todos juntos a la hora de la clase y un QR
que rota cada 30 s en una pantalla obliga a tener esa pantalla. La permanencia baja a la duración típica de una clase.

### 4.3 Membresías (`memberships.membership_settings`)

| Opción | Hoy por defecto | gym | box | studio | personal_trainer | academy |
|---|---|---|---|---|---|---|
| `GraceDays` | 3 | 3 | 3 | **0** | **5** | 3 |
| `ExpiringDays` | 7 | 7 | 7 | 7 | 7 | 7 |
| `EnrollmentFee` | 0 | 0 | 0 | 0 | 0 | 0 |

El estudio vende sobre todo tiqueteras, que no tienen gracia (Memberships §7); se deja en 0 para que las mensuales del
estudio se comporten igual. El entrenador personal cobra a mano y con menos rigor: 5 días.

### 4.4 Rutinas (`routines.routine_settings`)

| Opción | Hoy por defecto | gym | box | studio | personal_trainer | academy |
|---|---|---|---|---|---|---|
| `ReviewWeeks` (revisión de la rutina) | 6 | 6 | 6 | 6 | **4** | 6 |

### 4.5 Logros sembrados (`SeedDefaultAchievementsHandler`)

Desde AC1 (2026-11-07) el catálogo son cadenas por nivel con medalla (`GSM-modulo-achievements.md` §12). Por tipo:

| Cadena | gym | box | studio | personal_trainer | academy | other |
|---|---|---|---|---|---|---|
| Asistencia (6) | sí | sí | sí | **no** | sí | sí |
| Rachas (5) | sí | sí | sí | sí | sí | sí |
| Disciplina: sesiones completas (5) | sí | sí | **no** | sí | **no** | sí |
| Fuerza: récords, levantamientos y tonelaje (6) | sí | sí | **no** | sí | **no** | sí |
| Clases (5) | no | **sí** | **sí** | no | **sí** | no |
| Horarios (5) | sí | sí | sí | **no** | sí | sí |
| Ranking mensual (4) | sí | sí | sí | **no** | sí | sí |
| 1 año con nosotros y Primer amigo | sí | sí | sí | sí | sí | sí |

El entrenador personal no tiene puerta: sin visitas, horarios ni ranking (su plantilla apaga el ranking). Antes de AC1
se sembraban ocho logros (visitas, rachas, permanencia, primer récord, primer amigo y, según el tipo, clases, sesiones
de rutina y volumen).

### 4.6 Lo que la plantilla **no** crea

- **Planes de membresía.** Crear planes con precio a nombre del dueño es arriesgado (precios equivocados que alguien
  vende sin mirar). En su lugar, el paso «Planes» del onboarding **sugiere** planes según el tipo, ya rellenos, y el
  dueño los confirma (§8).
- **Horario, sede, equipos, clases.** Son datos del negocio, no configuración.

## 5. Opciones nuevas

Son opciones normales: viven en la configuración de su módulo, se ven en Configuración, cualquier tipo las puede
encender y tienen un valor por defecto que reproduce el comportamiento de hoy.

### 5.1 `RemoteTraining` — «Mis miembros entrenan fuera de mi sede» (Attendance)

Columna `remote_training boolean not null default false` en `attendance.checkin_settings`. Con ella encendida:

1. **No aplica el horario de apertura** al conteo del día por entrenamiento (A8). Hoy `CheckInHandler.RunAsync` llama a
   `EnsureGymOpenAsync` para todas las vías; con `RemoteTraining` se omite **solo** para el canal `Routine`. El QR,
   el documento y recepción siguen respetando el horario.
2. **No se muestra el aviso** «cualquiera podría marcarlo desde su casa» (`RoutineEntryRisk`): entrenar fuera es lo
   esperado. `RoutineEntryRisk.State` suma `RemoteTraining` y `AtRisk` exige que esté apagado.
3. **Aforo y horas pico** dejan de calcularse sobre las entradas `routine` (no son gente dentro de un local).
   `AttendanceReader.GetOccupancyAsync` y las horas pico excluyen ese método cuando la opción está encendida.

No cambia quién puede entrar: la membresía se sigue exigiendo igual.

Se configura en Configuración → Check-in, con un texto que explique las tres consecuencias.

### 5.2 `TrainingEntryTrigger` — cuándo cuenta el día sin entrada en la puerta (Attendance)

Enum de Postgres `attendance.training_entry_trigger` (`start`, `first_log`), columna en `checkin_settings` con
`start` por defecto (lo de hoy). Solo tiene efecto cuando el gym no tiene registro en la puerta
(`IsRoutineEntryAsync`).

- **`start`** (hoy): el día cuenta al tocar «Iniciar rutina» o «Registrar entrenamiento».
- **`first_log`**: el día cuenta al **registrar el primer ejercicio** del día. El botón «Iniciar rutina» desaparece
  y la tarjeta dice «Tu día cuenta en cuanto registres tu primer ejercicio». Tocar un botón ya no basta, así que es
  más difícil de fingir y el día contado coincide con un entrenamiento real.

Cómo se construye `first_log` sin escritura cruzada: Routines ya publica `RoutineSessionLogged` al guardar un registro.
Attendance agrega un handler `RoutineSessionLoggedCountsTheDayHandler` que, si el gym está en `first_log`, llama a
`CheckInHandler.HandleRoutineStartAsync` en nombre del miembro (con `GymExecutionScope` fijado al gym del evento). El
handler es idempotente porque el caso de uso ya lo es por día. Si falla (por ejemplo, membresía vencida → 409), el
registro del ejercicio no se pierde y el día simplemente no cuenta; el rechazo queda como intento, igual que con el QR.

Con `first_log`, «Registrar entrenamiento» del inicio (miembros sin rutina) **no se ofrece**: sin rutina no hay
ejercicio que registrar. Ese miembro cuenta sus días solo por recepción o clases. Se acepta: para un entrenador
personal, un cliente sin rutina asignada no está entrenando con él.

### 5.3 `ShowFrontDesk` — Recepción, kiosco y aforo (Gyms)

Columna `show_front_desk boolean not null default true` en `gyms.gym_feature_flags`. Apagada:

- El panel oculta del menú **Recepción** (check-in a mano), **Kiosco** y la tarjeta de **aforo** del dashboard.
  Hoy el menú lo declara cada `*PanelModule.cs`; `AttendancePanelModule` lee la bandera de `GymContext.FeatureFlags`.
- La app del miembro oculta «¿Qué tan lleno está?».
- **No** bloquea nada en la API: registrar una entrada a mano sigue funcionando (por ejemplo, si el entrenador quiere
  marcar una sesión presencial). Es una opción de **interfaz**, como `ShowCommunity`.
- La caja (Payments) **no** se oculta: el entrenador personal también cobra en efectivo.

### 5.4 Pasos obligatorios del onboarding (Gyms)

Hoy son obligatorios los cinco primeros pasos (`gym`, `location`, `hours`, `equipment`, `plans`). Para quien no tiene
sede no tiene sentido exigir ubicación, horario ni equipos. La regla nueva se deriva de las **opciones**, no del tipo:

| Paso | Obligatorio cuando |
|---|---|
| `gym` (datos) | siempre |
| `location` (ubicación) | `RemoteTraining` apagado |
| `hours` (horario) | `RemoteTraining` apagado |
| `equipment` (equipos) | `RemoteTraining` apagado **y** `MembersCanReportEquipment` o `ShowRoutines` encendido |
| `plans` (planes) | siempre |

`GetOnboardingStatusHandler` lee `RemoteTraining` por un reader de Attendance (no hay ciclo nuevo: Gyms ya lee de
Equipment, Memberships, Members e Identity para esto, Gyms §5). Un paso que deja de ser obligatorio sigue apareciendo
como opcional.

## 6. Cumplimiento de la rutina (Routines)

No depende del tipo, pero es lo que vuelve útil la plataforma para un entrenador personal: saber si su cliente **sigue
la rutina**, no solo si «vino». Se diseña aquí porque la plantilla `personal_trainer` lo pone en el centro, y se
construye como sub-etapa propia de Routines.

### 6.1 Qué se mide

Por miembro y por semana de negocio (lunes a domingo, `BusinessDayRange`):

- **Días planeados**: días de entrenamiento del calendario de la rutina en esa semana (rotativa o semanal; los
  descansos planificados no cuentan). Mismo calendario que ya usa la racha (`StreakCalculator`).
- **Días hechos**: días planeados con al menos un ejercicio registrado (`routine_exercise_logs`).
- **Ejercicios hechos / planeados** de esos días, para distinguir «vino y hizo 2 de 8» de «hizo todo».
- **Cumplimiento** = días hechos / días planeados (0–100 %). Una semana sin días planeados no tiene cumplimiento (nulo),
  no 100 %.
- Días **fuera de plan** (registró un día de descanso): se muestran aparte, no suben el porcentaje.

Las rutinas autoasignadas por el miembro (`SelfAssigned`) se miden igual pero se marcan, porque el entrenador no las
diseñó.

### 6.2 Dónde se ve

- **Ficha del miembro → Rutina**: las últimas 8 semanas como barras (días hechos sobre planeados) y el detalle de la
  semana en curso, día por día.
- **Listado nuevo «Cumplimiento»** en Rutinas (solo staff con permiso `trainer` u Owner): todos los miembros con rutina
  asignada, ordenados de menor a mayor cumplimiento en las últimas 2 semanas, con filtro «van atrasados» (< 50 %).
  Usa `ServerTable` con tarjetas en el celular, como el resto del panel.
- **App del miembro → Mi rutina**: «Esta semana: 2 de 4 días». Sin porcentaje ni comparación con otros.

### 11.1 Cómo quedó construido (T7)

- **Configuración recomendada (§9):** `GET /api/gym/recommended-configuration` (Owner) devuelve la plantilla del tipo del
  gym: módulos, entrada, membresías y rutinas. Se compone en el controller con `GymFeatureFlags.ForType`,
  `CheckinTemplates`, `MembershipTemplates` y `RoutineSettingsTemplates` (como el bloque `subscription`: Gyms no depende de
  los otros módulos) y **no escribe nada**. En el panel, Configuración → Datos del gym tiene «Ver la configuración
  recomendada» (y el aviso que sale al guardar un cambio de tipo): un diálogo con una fila por opción que difiere
  (hoy / recomendado), todo desmarcado al abrir. «Aplicar lo marcado» guarda por el endpoint normal de cada módulo
  (`FeatureFlagsPUT`, `Settings PUT` de check-in, membresías y rutinas), partiendo de lo que el servidor tiene hoy, así que
  solo cambia lo marcado. La lógica es `RecommendedConfigDiff` (pura, con pruebas). Los logros sembrados no se tocan.
- **`BusinessNoun` (§10):** `GSM.UI.Shared.Formatting.BusinessNoun` (`Bare`, `Your`, `YourCapitalized`); el tipo llega al
  panel en `MeGym.Type`. Se aplicó en el asistente de onboarding (título, pestaña, mensaje de cierre y «Falta el nombre
  de…»). **No** se tocaron los correos, los avisos de suscripción ni el login: el backend no arma esos textos con el
  tipo y el login es anónimo. Pendiente de aplicar donde el texto quede raro.
- **SuperAdmin (§11):** `AdminDashboardResponse.ByType` (`TypeMetricsRow`): gyms, pagando (al día o en mora), en prueba
  (en prueba o prueba terminada), MRR de los que pagan, miembros activos y avance prueba → pago (`pagando / (pagando +
  prueba)`) por tipo; la tabla «Cartera por tipo de negocio» va en el panel de plataforma. La columna y el filtro por
  tipo de la cartera son de T1. La barra de impersonación muestra el nombre y el tipo del gym.

### 5.6 Cómo quedó construido (T5)

- **`ShowFrontDesk`:** columna `show_front_desk` (default `true`) en `gyms.gym_feature_flags` (migración EF
  `V2_T5_ShowFrontDesk`; sin `V2_T5.sql`, como en T1/T4). `UpdateFeatureFlagsRequest.ShowFrontDesk` es opcional: un cliente
  anterior que no lo manda lo conserva. También viaja en `GymFeatureFlagsResponse`, `GymFeatureFlagsView` y
  `MeFeatureFlags`. La plantilla `personal_trainer` lo apaga; los demás tipos lo dejan encendido.
- **Panel:** `PanelMenuEntry.Feature` (`PanelFeatures.FrontDesk`) oculta del menú y de la barra inferior la entrada
  «Recepción»; el dashboard no dibuja la tarjeta de aforo; «Configurar kiosco» no sale en Check-in. Configuración →
  Módulos tiene el interruptor «Recepción, kiosco y aforo». La app del miembro oculta «¿Qué tan lleno está?».
- **No bloquea nada en la API:** registrar una entrada a mano, el aforo y el kiosco siguen respondiendo. La caja
  (Payments) no se oculta. La sección de Configuración «Kioscos» tampoco se oculta (solo su atajo desde Check-in).
- **Pasos obligatorios del onboarding (§5.4):** `GetOnboardingHandler.RequiredFor(remoteTraining, showRoutines,
  membersCanReportEquipment)`. Ubicación y horario exigen `RemoteTraining` apagado; equipos exige además rutinas o
  reporte de fallas encendidos. `RemoteTraining` se lee por `IAttendanceReader.IsRemoteTrainingAsync` (lectura, sin ciclo
  nuevo). Un paso que deja de ser obligatorio sigue en la lista como opcional; el gym se habilita con los obligatorios
  hechos. **No** se agregan a «Para completar» (esa lista sigue siendo entrada, equipo y primeros miembros).

### 5.5 Cómo quedó construido (T4)

- **Datos:** columnas `remote_training` (bool, `false`) y `training_entry_trigger` (enum `attendance.training_entry_trigger`:
  `start`/`first_log`, `start`) en `attendance.checkin_settings`. La migración es `V2_T4_RemoteTraining` de EF; como en T1,
  no hay `V2_T4.sql` a mano porque EF crea el enum y las columnas con los valores de hoy.
- **`RemoteTraining`:** en `CheckInHandler.RunAsync` el horario de apertura se omite **solo** para el canal `Routine`;
  QR, documento y recepción lo siguen respetando. El aforo (`OccupancyEstimator`) y las horas pico
  (`GetHourlyCountsAsync`) excluyen las entradas `routine`. `RoutineEntryRisk.State` suma `RemoteTraining` y `AtRisk` exige
  que esté apagado. La membresía se exige igual.
- **`first_log`:** `RoutineExerciseLoggedCountsTheDayHandler` (Attendance) reacciona a `RoutineExerciseLogged` (cada ejercicio nuevo) y llama a
  `CheckInHandler.HandleRoutineLogAsync`, que es idempotente por día. Si el miembro no puede entrar (membresía vencida →
  409) el registro del ejercicio se conserva, queda el intento y el handler solo lo anota (no es un fallo del evento).
  El botón (`POST /api/attendance/routine-start`) responde 403 cuando el gym está en `first_log`.
- **Contratos:** `CheckinSettingsResponse`/`UpdateCheckinSettingsRequest` suman `RemoteTraining` y `TrainingEntryTrigger`
  (`Start`/`FirstLog`, texto; sin valor = no cambia). `MeFeatureFlags.RoutineEntryFirstLog` lo lee la app.
- **Plantilla `personal_trainer`:** `RemoteTraining = true` y `first_log`.
- **Interfaz:** Configuración → Check-in tiene el interruptor y el selector con sus consecuencias; la app del miembro no
  ofrece «Registrar entrenamiento» en el inicio y Mi rutina dice «Tu día cuenta en cuanto registres tu primer ejercicio».
- **Sin tocar:** `ShowFrontDesk` y el onboarding por opciones (T5). Mi rutina no se refresca sola tras el primer
  ejercicio: la tarjeta pasa a «ya cuenta» al volver a abrir la pantalla.

### 4.7 Cómo quedó construido (T2)

- **Módulos:** `GymFeatureFlags.ForType(gymId, type)` (Domain) lo usa `CreateGymHandler` en la misma transacción del alta.
- **Cada módulo aplica su parte** reaccionando a `GymCreated`: `GymCreatedAppliesCheckinTemplateHandler`,
  `GymCreatedAppliesMembershipTemplateHandler`, `GymCreatedAppliesRoutineTemplateHandler`, y
  `SeedDefaultAchievementsHandler`, que ahora siembra `AchievementTemplates.For(type)`. Las tablas viven en
  `CheckinTemplates`, `MembershipTemplates`, `RoutineSettingsTemplates` y `AchievementTemplates`.
- **Siempre escriben la fila** de ajustes (aunque sea igual al valor por defecto) y solo si no existía, para que la
  existencia de la fila sea la marca de «plantilla aplicada». Los gyms anteriores no tienen fila: su ficha los muestra
  incompletos, pero operan con los valores por defecto de siempre.
- **Marcas:** `GymDetailResponse.Template` (`checkin`, `memberships`, `routines`, `achievements`) se lee por
  `IAttendanceReader.HasCheckinSettingsAsync`, `IMembershipReader.HasSettingsAsync`, `IRoutineReader.HasSettingsAsync` e
  `IAchievementReader.HasAchievementsAsync`.
- **Logros:** la siembra no es idempotente ante un reintento del evento (como antes); un reintento duplicado lo frena el
  índice único del nombre.

### 8.1 Cómo quedó construido (T3)

- **Planes sugeridos:** `PlanTemplate.For(tipo)` (cliente, `GSM.Panel.UI/Onboarding`). Box: Mensual y Tiquetera 12 clases
  (45 días); estudio: tiqueteras de 8 y 4 clases (30 días) y clase suelta (1 entrada, 7 días); entrenador personal:
  Mensual (30 días) y Paquete 12 sesiones (45 días); academia: Mensual y Trimestral. **Gimnasio y «otro» conservan las
  cuatro plantillas de siempre** (Día, Mensual, Trimestral, Anual), que incluyen las dos que pide §8. Las tiqueteras salen
  como plan `pass` con `Entries` y `ValidityDays = DurationValue`. Los precios nunca vienen rellenos.
- **Textos:** el paso de entrada del asistente se llama «Entrenamientos» y no habla del kiosco ni del QR para el
  entrenador personal. El título «Configura tu gimnasio» y demás textos del negocio quedan para `BusinessNoun` (T7).
- **Cambiar el tipo:** el Owner lo hace en Configuración → Datos del gym (`UpdateGymRequest.Type`, opcional: sin él se
  conserva); el SuperAdmin en su ficha (`PUT /api/admin/gyms/{id}/type`, `ChangeGymTypeHandler` del módulo Gyms, así que
  no hay escritura cruzada nueva). Ninguno reaplica la plantilla.
- **No incluido de §8:** las sugerencias de equipos del paso «Equipos» (no estaban en el alcance de T3 de §15).

### 6.2b Cómo quedó construido (T6)

- **Porcentaje sobre los días que ya tocaban.** `Cumplimiento` = días hechos / días planeados **que ya pasaron o que ya
  se hicieron** (`DueDays`); el día de hoy solo cuenta en contra cuando ya se hizo, y los días futuros no cuentan. Una
  semana cerrada coincide con la definición de §6.1. Los planeados de la semana completa (`PlannedDays`) se muestran
  aparte («2 de 4 días»). «Últimas 2 semanas» suma días de la semana pedida y la anterior, no promedia porcentajes.
- **Estados por día:** `done`, `missed`, `today`, `upcoming`, `rest`, `offplan`, `none`. «Fuera de plan» = registró un
  día que no tocaba o era descanso; no sube el porcentaje.
- **Cada fecha se juzga con la rutina y los días vigentes ese día** (`AddedOn`/`RetiredOn`, `starts_on`/`deactivated_on`),
  igual que la racha; las líneas retiradas dejan de contar como ejercicios planeados desde su fecha de retiro.
- **Código:** `Routines/Adherence/AdherenceCalculator` (función pura), `MemberAdherenceService` y los casos de uso
  `GetMemberAdherenceHandler`, `GetMyAdherenceHandler` y `ListRoutineAdherenceHandler`; lectura por `IAdherenceSource`
  (Infrastructure). El calendario se reimplementa en el calculador porque necesita el día y la rutina, no solo la fecha.
- **Permisos:** el listado exige `trainer` (Owner cumple); la ficha del miembro la ve todo el staff; `/api/me/adherence`
  responde 403 si `show_routines` está apagado.
- **Interfaz:** panel → Rutinas → «Cumplimiento» (`/routines/adherence`, `ServerTable` con tarjetas en celular y filtro
  «van atrasados»); ficha del miembro, pestaña Rutina → «Cumplimiento» (barras de 8 semanas y la semana por día);
  app del miembro → Mi rutina → «Esta semana: 2 de 4 días» (sin porcentaje).
- **Pendiente de medir:** el listado calcula en memoria sobre los miembros activos con rutina; si se vuelve lento se
  cachea por semana cerrada (aplazado, como dice §6.3).

### 6.3 Contratos

- `GET /api/routines/adherence?weekStart=&onlyBehind=&page=` → página de `MemberAdherenceRow` (miembro, rutina,
  cumplimiento de la semana y de las 2 últimas, último registro).
- `GET /api/members/{id}/adherence?weeks=8` → `MemberAdherence` (semanas con planeados, hechos, ejercicios).
- `GET /api/me/adherence` → la semana en curso del miembro.

Cálculo en `GSM.Application/Routines/Adherence/` con proyecciones `Select` sobre `routine_exercise_logs` agrupadas por
`BusinessDayRange` (nunca por rango UTC). Sin tabla nueva: se calcula al leer. Si el listado se vuelve lento con muchos
miembros, se cachea por semana cerrada (decisión aplazada hasta medirlo).

## 7. Alta del tenant

### 7.1 Datos

- `gyms.gyms.type` con enum `gyms.gym_type` (`gym`, `box`, `studio`, `personal_trainer`, `academy`, `other`),
  `not null default 'gym'`. Valores de varias palabras con `SnakeCaseNameTranslator`; en C# `GymType.PersonalTrainer`.
- `CreateGymRequest` suma `GymType Type = GymType.Gym`.
- `GymCreated` suma `GymType Type`.

### 7.2 Quién aplica cada parte

Cada módulo es dueño de su configuración, así que **cada uno aplica su parte de la plantilla reaccionando a
`GymCreated`**, igual que hoy la suscripción y los logros. No se agrega ninguna escritura cruzada nueva.

| Parte | Quién | Cómo |
|---|---|---|
| Tipo y módulos (§4.1) | Gyms, en `CreateGymHandler` | En la misma transacción del alta: `GymFeatureFlags.ForType(gymId, type)` en lugar de `Default`. |
| Entrada (§4.2) | Attendance | Handler nuevo `GymCreatedAppliesCheckinTemplateHandler`. |
| Membresías (§4.3) | Memberships | Handler nuevo `GymCreatedAppliesMembershipTemplateHandler`. |
| Rutinas (§4.4) | Routines | Handler nuevo `GymCreatedAppliesRoutineTemplateHandler`. |
| Logros (§4.5) | Achievements | `SeedDefaultAchievementsHandler` existente, que pasa a elegir el catálogo por `domainEvent.Type`. |

Las plantillas son clases estáticas en cada módulo (`CheckinTemplates.For(type)`, etc.), para que la tabla de §4 de
cada módulo viva junto al módulo y se pruebe sola.

`GymType` vive en `GSM.Domain/Gyms` y lo consumen los demás módulos solo como valor del evento, igual que hoy consumen
`PaymentMethod`. No es una referencia a la entidad `Gym`.

### 7.3 Idempotencia y fallos

- Cada handler **solo escribe si no existe fila** de configuración para ese gym (las configuraciones son perezosas, así
  que «sin fila» es exactamente «nadie la ha tocado»). Reintentarlo no pisa nada.
- Si un handler falla, el gym opera con los valores por defecto de ese módulo (los de hoy) y el fallo queda en
  `logs.event_handler_failures` para el reintento existente. El alta no se cae.
- El SuperAdmin ve en la ficha del gym si la plantilla quedó aplicada completa (cuatro marcas: entrada, membresías,
  rutinas, logros), leídas por los readers de cada módulo.

### 7.4 Interfaz del alta

- `/admin` → «Nuevo gimnasio»: un selector de tipo con la etiqueta y una línea de qué cambia («Sin recepción ni QR;
  el día cuenta al registrar el primer ejercicio»).
- El asistente de onboarding del dueño muestra el tipo arriba y enlaza a Configuración → Datos del gym para cambiarlo.

## 8. Onboarding según el tipo

El tipo sí se usa aquí, porque son **sugerencias** y no comportamiento:

- **Paso «Planes»** trae planes sugeridos ya rellenos, que el dueño puede editar o descartar antes de crearlos:
  - gym: Mensual (30 días), Trimestral (90 días).
  - box: Mensual ilimitado, Tiquetera 12 clases (45 días).
  - studio: Tiquetera 8 clases (30 días), Tiquetera 4 clases (30 días), Clase suelta (1 entrada, 7 días).
  - personal_trainer: Mensual (30 días), Paquete 12 sesiones (45 días).
  - academy: Mensual, Trimestral.
  - Los precios van **vacíos**: el dueño los escribe.
- **Paso «Equipos»** (si aplica): sugerencias de equipo según el tipo (el catálogo de Equipment ya tiene tipos).
- **Textos** del asistente: para `personal_trainer` se omiten las frases sobre «la entrada del gimnasio».

## 9. Cambiar el tipo

- Lo cambia el **Owner** en Configuración → Datos del gym, o el **SuperAdmin** en la ficha del gym.
- Cambia la etiqueta, las métricas del SuperAdmin y las sugerencias del onboarding. **No** cambia ninguna
  configuración.
- Al cambiarlo, el panel ofrece **«Ver la configuración recomendada»**: un diálogo con las diferencias entre lo que
  tiene el gym y la plantilla del tipo nuevo, opción por opción, para aplicar las que el dueño marque. Cada cambio
  marcado se guarda por el endpoint normal de su módulo (no hay endpoint «aplicar plantilla» que escriba en varios
  módulos).
- Los logros sembrados no se tocan: el dueño los administra en Logros.

## 10. Textos del panel

El panel dice «gimnasio» en muchos lugares (títulos, ayudas, correos). Para no reescribir todo:

- Los textos que nombran **el negocio** pasan por un helper `BusinessNoun` en `GSM.UI.Shared` con dos formas por tipo
  («tu gimnasio», «tu box», «tu estudio», «tu negocio», «tu academia»). Se cambia solo donde el texto queda raro, no
  en masa: correos de bienvenida, onboarding, avisos de suscripción y el login.
- La marca comercial (TrainingHub OS / GO) no cambia.
- **«Miembro» no cambia** en ningún tipo.

## 11. SuperAdmin

- Columna y filtro por tipo en la lista de gyms.
- Métricas del panel `/admin` partidas por tipo (gyms activos, ingresos, prueba → pago).
- El tipo se ve en la impersonación, para que soporte entienda por qué un gym no tiene recepción.

## 12. Fuera de alcance

- **«Self trainer»** (persona que entrena sola). Necesitaría que el dueño y el miembro sean la misma persona sin
  staff, sin cobros ni suscripción del gym; hoy `AppUser` tiene un acceso de equipo y otro de miembro por gym y el
  flujo asume un negocio que vende membresías. Si se quiere, es un producto aparte (posiblemente un «gym personal»
  creado solo por el usuario desde la app GO, con suscripción B2C). Se decide en su propia revisión.
- **Multi-sede** (Locations), que sigue sin planificar.
- **Precios distintos del SaaS por tipo**: lo decide el SuperAdmin con los planes del SaaS que ya existen.

## 13. Migración

- `V2_T1.sql`: `CREATE TYPE gyms.gym_type` y `ALTER TABLE gyms.gyms ADD COLUMN type gyms.gym_type NOT NULL DEFAULT
  'gym'`. Todos los gyms existentes quedan `gym`.
- `V2_T4.sql`: enum `attendance.training_entry_trigger`; columnas `remote_training` y `training_entry_trigger` en
  `attendance.checkin_settings` con los valores de hoy.
- `V2_T5.sql`: columna `show_front_desk` en `gyms.gym_feature_flags`, `true`.
- Ninguna migración cambia datos de gyms existentes.

## 14. Pruebas

- Por módulo: la plantilla de cada tipo produce exactamente la tabla de §4 (pruebas de las clases `*Templates`).
- Integración: un alta por tipo deja las cuatro configuraciones y los logros esperados; reintentar un handler no pisa
  una configuración que el dueño ya cambió.
- `RemoteTraining`: con horario cerrado, el QR da 409 y el entrenamiento cuenta; sin la opción, ambos dan 409.
- `first_log`: registrar un ejercicio crea la entrada del día una sola vez; con membresía vencida el registro se guarda
  y el día no cuenta.
- Onboarding: con `RemoteTraining`, ubicación, horario y equipos son opcionales y el gym se habilita con datos y planes.
- Cumplimiento: semanas con descanso planificado, día fuera de plan, rutina cambiada a mitad de semana (vale el
  calendario vigente cada día, igual que la racha), zona horaria (registro a las 11 p. m. hora Colombia).
- bUnit: selector de tipo en `/admin`, «Ver la configuración recomendada», menú sin Recepción con `ShowFrontDesk`
  apagado, tarjeta de `first_log` en la app.

## 15. Sub-etapas

Cada una se construye en el orden habitual (Domain → Infrastructure/migración → Application → API → interfaz) y con
confirmación entre etapas.

| Sub-etapa | Qué | Depende de |
|---|---|---|
| **T1** | `GymType` en Domain, columna y enum (`V2_T1.sql`), en `GymResponse`, `CreateGymRequest` y `GymCreated`. Selector en `/admin`, columna y filtro en la lista del SuperAdmin. Sin plantillas todavía. | — |
| **T2** | Plantilla de módulos (§4.1, sin `ShowFrontDesk`) en `CreateGymHandler` y handlers de Attendance, Memberships y Routines (§4.2–4.4, sin las opciones nuevas). Catálogo de logros por tipo (§4.5). Marcas de plantilla aplicada en la ficha del SuperAdmin. | T1 |
| **T3** | Onboarding: planes sugeridos por tipo (§8) y textos. Cambiar el tipo (§9) sin el diálogo de recomendada. | T1 |
| **T4** | `RemoteTraining` y `TrainingEntryTrigger` (§5.1, §5.2): migración, caso de uso, handler de `first_log`, Configuración → Check-in, app del miembro, aviso `RoutineEntryRisk`. Se suman a la plantilla `personal_trainer`. | T2, A8 |
| **T5** | `ShowFrontDesk` (§5.3) y pasos obligatorios del onboarding por opciones (§5.4). Se suma a la plantilla. | T2, T4 |
| **T6** | Cumplimiento de la rutina (§6): cálculo, endpoints, ficha, listado y app. **Construida.** | — (independiente) |
| **T7** | «Ver la configuración recomendada» (§9) y `BusinessNoun` (§10). Métricas por tipo en `/admin` (§11). | T2–T5 |

T6 no depende de las demás y es la que más valor da a un entrenador personal: puede ir primero si ese es el público
que se quiere atraer antes.

**Pendiente (no construido):** sugerencias de equipos en el paso «Equipos» del onboarding (§8), aplicar `BusinessNoun` en
correos y avisos de suscripción (§10), ocultar la sección de Configuración «Kioscos» con `ShowFrontDesk` apagado, mostrar
los pasos de ubicación, horario y equipos que dejan de ser obligatorios en «Para completar», y el refresco automático de
la tarjeta de Mi rutina tras el primer ejercicio (T4).

## 16. Preguntas abiertas

1. ¿El dueño puede **elegir** el tipo al registrarse solo (autoservicio), o el alta sigue siendo solo del SuperAdmin?
   Hoy no hay registro de gyms en autoservicio; este documento asume que no.
2. Los valores de §4 son un borrador razonable, no medidos. Conviene validarlos con uno o dos negocios reales de cada
   tipo antes de T2 (sobre todo gracia del estudio y logros del box).
3. Para `personal_trainer` online, ¿hace falta **mensajería** entrenador ↔ miembro? Hoy Comunicaciones es de uno a
   muchos y sin respuestas de miembros. Si se necesita, es una revisión aparte.
4. ¿Se cobra distinto el SaaS a un entrenador con 10 clientes que a un gimnasio con 800? Afecta a los planes del SaaS,
   no a este diseño.
