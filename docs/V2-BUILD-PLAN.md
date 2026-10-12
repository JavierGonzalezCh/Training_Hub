# Plan de construcción de la v2 — desarrollo con varios agentes en paralelo

> **Estado:** plan aprobado para empezar (2026-10-07). Ordena todas las sub-etapas que salieron del
> replanteamiento (ver la tabla de especificaciones en §11) para que las construyan **varios agentes
> Sonnet a la vez**, coordinados por un integrador.

## 1. Cómo se trabaja

### 1.1 Roles

| Rol | Quién | Qué hace |
|---|---|---|
| **Integrador** | Una sesión con Opus, o la persona responsable | Reparte las tareas de cada ola, revisa y **une** cada rama, regenera migraciones y cliente, corre todos los tests, actualiza `CLAUDE.md` y `docs/BUGS.md`. Es el único que toca `main`. |
| **Implementador** | Un agente Sonnet por tarea | Construye **una sola sub-etapa** en su propio worktree, con sus tests, y se detiene. No une ramas ni toma decisiones de diseño. |
| **Dueño del producto** | La persona | Resuelve preguntas de diseño que levanten los agentes y aprueba el cierre de cada ola. |

### 1.2 Reglas para los implementadores

1. **Una tarea = una sub-etapa.** Es la regla de `CLAUDE.md` ("no implementes más de una sub-etapa sin
   confirmación"), aplicada a cada agente.
2. **Antes de escribir código**, leer `CLAUDE.md`, la especificación de su tarea (columna "Spec") y las
   que esa cita.
3. **Orden de capas** del proyecto: Domain → Infrastructure (configuración + migración) → Application →
   API → cliente → UI.
4. **Si la especificación no alcanza o se contradice, el agente se detiene** y deja la pregunta en la
   descripción de su rama. **No inventa** reglas de negocio.
5. **No edita** `CLAUDE.md`, `docs/*.md` ni archivos de otro módulo fuera de los que su tarjeta
   autoriza.
6. **Tests obligatorios:** los criterios de aceptación de la tarjeta, como tests de integración contra
   **su propia base de carril** (§2.1), y bUnit para la UI que agregue.
7. **Bugs:** si la tarea corrige uno de `docs/BUGS.md`, primero va el test que lo reproduce, que debe
   fallar, y después el arreglo.
8. **Commits** con Conventional Commits y la línea de coautoría de las instrucciones del proyecto.
9. **Al terminar:** la rama compila, sus tests pasan y deja un resumen (§6.2). Ahí se detiene.

## 2. Cimientos del paralelismo (tarea X0, antes de todo)

### 2.1 Una base de datos por carril

Hoy los tests usan `gsm_test`, fijo en `tests/GSM.IntegrationTests/ApiFactory.cs`, y hacen `TRUNCATE`
al prepararse. **Dos agentes a la vez se borran los datos.**

- `ApiFactory` lee la base de la variable de entorno **`GSM_TEST_DATABASE`** (por defecto `gsm_test`).
- Script `scripts/new-lane.ps1 <carril>`:
  - crea el worktree en `../gsm-lanes/<carril>` desde la rama de integración;
  - crea las bases `gsm_dev_<carril>` y `gsm_test_<carril>` y les aplica las migraciones;
  - escribe un `.env.lane` con la base, y puertos propios para la API y la web
    (`7300 + 10·n` y `7301 + 10·n`).
- Script `scripts/drop-lane.ps1 <carril>` para limpiar.

### 2.2 Registro por módulo

Para que dos agentes no editen siempre los mismos archivos centrales:

- **Inyección de dependencias:** `GSM.Application/DependencyInjection.cs` y
  `GSM.Infrastructure/DependencyInjection.cs` pasan a llamar un método por módulo
  (`AddMembersModule()`, `AddMembersInfrastructure()`…), cada uno en la carpeta de su módulo. Un agente
  solo toca el de su módulo.
- **EF Core:** los `DbSet` y el mapeo de enums de Postgres de `GsmDbContext` se reparten en archivos
  parciales por módulo (`GsmDbContext.Members.cs`…). Las configuraciones ya están por módulo.
- **Menú y configuración del panel:** cada módulo declara sus entradas de menú y sus secciones de
  configuración en un registro propio (se crea en AP2), en lugar de editar `NavMenu.razor` y
  `Settings/Index.razor`.

### 2.3 Archivos que siempre chocan, y cómo se resuelven

| Archivo | Regla |
|---|---|
| `Migrations/GsmDbContextModelSnapshot.cs` y las migraciones | El agente genera su migración (`V2_<id>_<Descripción>`) para probar. **Al unir, si el snapshot choca, el integrador borra la migración del agente, une el código y la vuelve a generar** sobre el modelo unido. Por eso **todo SQL escrito a mano** (copiar datos, convertir enums, rellenar columnas) va en `GSM.Infrastructure/Migrations/Scripts/V2_<id>.sql`, que la migración ejecuta. Ese archivo nunca se pierde al regenerar. |
| `GSM.Client/Generated/GsmApiClient.g.cs` | Nunca se une a mano: el integrador lo **regenera** con `scripts/generate-client.ps1` después de cada unión. Los agentes lo regeneran en su rama para compilar. |
| `ALTER TYPE ... ADD VALUE` (enums de Postgres) | No se revierte. Va en el script SQL de la tarea y se revisa con cuidado al unir. |
| `CLAUDE.md`, `docs/` | Solo el integrador, al cerrar cada ola. |

### 2.4 Ramas

- Rama de integración **`v2`**, que sale de `main`.
- Cada tarea, en **`v2/<id>-<nombre-corto>`** (por ejemplo `v2/MS1-estado-comercial`), creada desde
  `v2` al empezar la tarea.
- El integrador une las ramas a `v2` **en el orden de la columna "Depende de"**. Al cerrar cada ola,
  `v2` debe compilar y pasar **todos** los tests.
- `v2` pasa a `main` cuando se cierra una ola que deja el sistema desplegable (§7).

## 3. Olas y carriles

Una **ola** es un grupo de tareas que pueden correr a la vez. Un **carril** es una línea de trabajo
(normalmente un módulo) con **un agente a la vez**; dentro de un carril, las tareas van en el orden
indicado. **Entre olas, el integrador une todo y deja `v2` verde** antes de lanzar la siguiente.

Agentes simultáneos: **3 o 4 como máximo** (corregido el 2026-10-11; el plan original decía 6 a 9).
Tamaño: **S** (horas), **M** (un día de agente), **L** (varios).

> **Por qué no más (lo aprendido en las olas 0 a 2):**
> - **El cuello de botella es el límite de uso de la API, no la cantidad de agentes.** Hubo tres cortes,
>   todos con 4 o más agentes corriendo; uno perdió un worktree entero (S7). Más agentes no hacen mas
>   trabajo total: gastan la cuota antes y dejan más tareas a medias.
> - **Cada agente arranca en frío** y relee `CLAUDE.md` y las specs: con N agentes, N veces lo mismo.
> - **Unir cuesta y crece con el número de ramas:** choques en el snapshot, el cliente, lectores
>   compartidos y Designers, y choques semánticos que git no marca (I2 rompió los tests de P1, R2, R3 y M1).
> - **Incidentes por paralelismo:** la base `gsm` migrada por error, un agente contra `gsm_test`, un
>   test intermitente y la suite de 2,5 a 8 minutos.
> - **El dueño del producto también es un cuello de botella:** las preguntas se acumulan (22 al cerrar
>   la Ola 2) y varias obligan a rehacer o agregar trabajo.
>
> **Cómo se lanza:** carriles que no se pisen; **en serie** las tareas que comparten terreno (p. ej.
> A1, MS1b, G3b y C1 tocan estado comercial y horario); **unir cada tarea apenas termina**, sin esperar
> al cierre de la ola. La columna "Carril" de cada ola no significa "todos a la vez".
>
> **Agentes que solo escriben código (desde el 2026-10-12).** Los implementadores **no compilan, no
> corren tests, no levantan la API ni ejecutan `dotnet ef`**: compilar varias soluciones a la vez agotaba
> la memoria de la máquina. Escriben el código de todas las capas y sus tests, y entregan. El integrador,
> cuando terminan todos los de la tanda, une cada rama, **genera las migraciones sobre el modelo ya unido**
> (un solo snapshot, sin choques), regenera el cliente, corrige lo que no compile o no pase y corre la
> suite completa **en primer plano**. Por eso: el SQL a mano va en `Migrations/Scripts/V2_<id>.sql` y la
> tarea deja escrita la migración que espera; la UI usa los nombres de método que NSwag debería generar y
> el agente anota cuáles supuso.

### Ola 0 — Cimientos

| Orden | Id | Tarea | Spec | Tamaño |
|---|---|---|---|---|
| 1 (solo) | **X0** | Base por carril, scripts de carril, DI y `DbContext` por módulo, carpeta de scripts SQL | §2 de este plan | M |
| 2 (en paralelo) | **AP1** | Extraer `GSM.Panel.UI` de `GSM.Web`, sin cambios visibles; portapapeles por interfaz | `GSM-apps-y-vistas.md` §3 | L |
| 2 (en paralelo) | **LG1** | Schema `logs`, `ILogStore`, código de rastreo en `ProblemDetails`, `errors`, `event_handler_failures`, `job_runs`, latidos, purga | `GSM-logs.md` §3–§6, §9 | M |
| 3 (solo) | **T1** | "socio" → "miembro" en textos de UI y comentarios | `CLAUDE.md` (Terminología) | S |

### Ola 1

| Carril | Tareas en orden | Spec |
|---|---|---|
| Identidad | **I2** Una cuenta, varios gyms (`GymAccess`), selector y cambio de gym, migración | `GSM-modulo-identity.md` §3 |
| Miembros | **M1** Ficha ampliada, historial de cambios, reactivar | `GSM-modulo-members.md` §3.1, §3.5, §4, §9 |
| Rutinas | **R1** Edición en el lugar (**corrige B1**) → **R2** Modo semanal | `GSM-modulo-routines.md` v2 |
| Pagos | **P1** Métodos de pago de Colombia | `GSM-modulo-payments.md` §3 |
| Avisos | **N1** Schema `notifications`, migración de la bandeja y la cola de correos, dispatcher con candado, campana general del staff | `GSM-modulo-notifications.md` §3–§5, §7 |
| Apps | **AP2** Panel adaptable: navegación del celular, `ServerTable` con modo tarjeta, diálogos a pantalla completa, registro de menú y configuración por módulo → **B3** (reproducir y corregir) | `GSM-apps-y-vistas.md` §5 |
| Plataforma | **S5** Cuenta única de SuperAdmin → **S6** Impersonación con escritura y en silencio | `GSM-modulo-superadmin.md` §V5, §V6 |

### Ola 2

| Carril | Tareas en orden | Spec |
|---|---|---|
| Identidad | **I3** Perfiles y permisos → **I1** Bloqueo por intentos, cambio de contraseña, enlaces | Identity §5, §7, §4, §11 |
| Membresías | **MS1** Estado comercial, gracia movida a Memberships, `AccessStatus`, regla de renovación, letrero, plan actual propuesto, reactivar planes | `GSM-modulo-memberships.md` §4.1, §5.1, §5.3, §10 |
| Gyms | **G2** Dirección, coordenadas, razón social, redes, página "Mi gimnasio" → **G3** Horario, festivos, excepciones, `IGymReader.IsOpenAt` | `GSM-modulo-gyms.md` §3, §8 |
| Catálogo | **R3** Catálogo de ejercicios v2 (tipo, `tracking_type`, unilateral, nivel, músculos, grupo muscular, ocultar, duplicar) → **E1** Catálogo de máquinas del sistema, inventario, mover `EquipmentType` | Routines v2; `GSM-modulo-equipment.md` §3, §8 |
| Miembros | **M2** Autorizaciones (textos por gym, aceptación, bloqueo de salud y medidas) | Members §3.4 |
| Avisos | **N2** Push web (Firebase, dispositivos, PWA del miembro, panel instalable) → **N4 + CO1** Preferencias por categoría y canal, horario sin avisos | Notifications §6, §8, §10; `GSM-modulo-comunicaciones.md` v2 §C, §G |
| Comunicaciones | **CO3** WhatsApp por enlace (plantillas, registro de contacto) | Comunicaciones v2 §B |
| Plataforma | **S7** Métricas mensuales | SuperAdmin §V4 |

### Ola 3

| Carril | Tareas en orden | Spec |
|---|---|---|
| Identidad | **I7** Cuenta de dispositivo del kiosco → **I5** Login del miembro con código → **I6** Cambio de email con verificación | Identity §8, §6, §3.4 |
| Puerta (Attendance) | **A1** Recepción en dos pasos, una entrada por día, motivos nuevos (incluido `gym_closed`), "Dejar pasar" → **A1b** Entradas múltiples → **A2** Anular entradas | `GSM-modulo-attendance.md` §3, §4, §5 |
| Plataforma | **G1 + S1** Alta con invitación y prueba opcional (primer pago sin prueba) → **S2** Planes con límite de miembros | Gyms §4; SuperAdmin §V1, §V2 |
| Miembros | **M1b** Entrenador asignado → **M3** Salud y registro de consultas → **M4** Mediciones | Members §3.2, §3.1, §7, §10 |
| Catálogo | **E2** Estado de máquinas, historial, nivel 1 → **R4** Series planeadas y superseries → **E3** Ligar ejercicios a máquinas | Equipment §4–§6; Routines v2 |
| Pagos | **P2** Permisos y vistas, solicitudes de anulación → **P3** Caja por turno (**corrige B2**) | Payments §4–§8 |
| Clases | **C1** Horario del gym y estar al día → **C5** Fecha de fin → **C3** Reglas de reserva → **C2** Instructor de la lista de entrenadores | `GSM-modulo-classes.md` v2 |
| Comunicaciones | **CO7** Recordatorios alineados con la gracia → **CO2** Audiencias por segmento → **CO5** Cumpleaños | Comunicaciones v2 §H, §A, §F |
| Avisos | **N5** Avisos programados y cancelables (recordatorio de clase, cumpleaños, vencimientos) | Notifications §4.4 |

### Ola 4

| Carril | Tareas en orden | Spec |
|---|---|---|
| Identidad + Gyms | **I4 + G4** Varios Owners, traspaso, contacto de facturación | Identity §12; Gyms §6 |
| Puerta | **A3** Kiosco con cuenta de dispositivo → **A4** Entrada con documento → **A5** Ocupación (**corrige B4**) → **A6** Geolocalización con QR fijo | Attendance §6–§9 |
| Membresías | **MS2** Precio acordado, matrícula, cobro obligatorio → **MS3** Meses del calendario → **MS4** Congelamientos → **MS5** Tiqueteras → **MS6** Cambio de plan → **MS7** Planes de grupo | Memberships §3–§9 |
| Catálogo | **R5** Alternativas (recomendadas y sugeridas, "hacer otro ejercicio hoy") → **E4** Reportes de fallas | Routines v2; Equipment §6–§7 |
| Clases | **C4** Lista de espera con confirmación → **C6** Cupo ligado a máquinas → **C7** Asistencia por el miembro → **C8** Avisos de clase | Classes v2 |
| Logros | **L1** Reglas heredadas y logros retroactivos → **L2** Antigüedad y referidos → **L4** Logros manuales → **L5** Premios → **L6** Ranking | `GSM-modulo-achievements.md` |
| Comunicaciones + Avisos | **CO4** Imagen y programación de anuncios → **A7** Push de rechazos al staff → **S3** Avisos de la suscripción | Comunicaciones v2 §D, §E; Attendance §10; SuperAdmin §V3 |
| Logs | **LG2** Rechazos, seguridad, lentitud → **LG3** Servicios externos y errores del cliente → **LG4** Panel de errores | `GSM-logs.md` |
| Apps | **AP3 + N3** Panel en la app nativa y push en Android (**requiere un Android real o emulador**) → **AP4** Modo staff / modo miembro → **AP5** Bloqueo con huella o PIN | Apps §4, §5, §7; Notifications §6 |

### Ola 5

| Carril | Tareas en orden | Spec |
|---|---|---|
| Miembros | **M5** "Mi perfil" del miembro → **M6** Anonimización (con sus consumidores: **R10** en Routines, Equipment, Comunicaciones) | Members §5, §6, §11 |
| Rutinas | **R7** Progreso y récords → **L3** Logros de récords y volumen → **R8** Fecha de revisión → **R9** Salud en el editor → **R6** Subida de imagen y video | Routines v2; Achievements §3.2 |
| Gyms | **G5** Onboarding obligatorio → **G6** Configuración ordenada | Gyms §5, §7 |
| Apps | **AP6** Miembro en escritorio → **AP7** Pulir para celular lo no diario → **MS8 + L7** Historial de membresías y "Mi progreso" en la app | Apps §6; Memberships §11; Achievements §8 |
| Dashboards | **D1** Panel del gym → **D2** Inicio del miembro → **D3** Panel del SuperAdmin | `GSM-dashboards.md` §4–§7. **D2 espera a que el carril Apps una AP6 y MS8 + L7** |
| Plataforma | **S4** Catálogos del sistema en `/admin` (ejercicios v2, alternativas, máquinas, ligados) | SuperAdmin §V7 |
| Logs | **LG5** Alertas al SuperAdmin | `GSM-logs.md` §8 |
| Comunicaciones | **CO6** Horario sin avisos aplicado a anuncios y correos (si no quedó completo en N4) | Comunicaciones v2 §G |

### Ola 6 — Cierre

| Id | Tarea |
|---|---|
| **R11** | Plantillas de rutina (decidido: al final) |
| **V1** | Recorrido completo de punta a punta en el navegador y en Android, con el guion de `VERSION-INICIAL-PLAN.md` ampliado a la v2 |
| **DP** | Despliegue (`DEPLOY-DOKPLOY-PLAN.md`), con R2, Resend y Firebase configurados |

## 4. Tareas que no hace un agente

Necesitan a la persona: crear cuentas, credenciales o hardware. Conviene empezarlas **desde la Ola 1**,
porque varias tareas no se pueden probar de verdad sin ellas.

| Qué | Lo necesitan | Cuándo |
|---|---|---|
| Servidor y ambiente de pruebas con Dokploy | Todo lo que depende de servicios externos | Antes de la Ola 2 |
| Cuenta de Cloudflare R2 | M1 (fotos), E1, R6, CO4 | Antes de la Ola 2 |
| Cuenta de Resend y dominio verificado | I1, I5, CO*, N1 (correos) | Antes de la Ola 2 |
| Proyecto de Firebase por ambiente | N2, N3, AP3 | Antes de la Ola 2 |
| Teléfono Android o emulador | AP3, N3, A6, escáner QR | Antes de la Ola 4 |
| Cuenta de Apple Developer y una Mac | iOS (fuera de este plan) | — |
| Imágenes de ejercicios y máquinas con licencia | S4, E1, R6 | Antes de la Ola 5 |
| Revisión legal (Ley 1581, Ley 2300) | M2, M3, CO* | Antes del despliegue |

## 5. Tarjeta de tarea (prompt para cada agente Sonnet)

El integrador arma el prompt de cada agente con esta plantilla y la fila de la tabla:

```
Eres un agente implementador del proyecto GSM. Trabajas SOLO en la tarea {Id} — {Nombre}.

Antes de empezar:
1. Lee CLAUDE.md completo y docs/V2-BUILD-PLAN.md §1–§2 y §6.
2. Lee la especificación: {Spec}, y las secciones que esa cite.
3. Tu worktree es ../gsm-lanes/{carril}. Carga .env.lane (GSM_TEST_DATABASE, puertos).
4. Crea tu rama v2/{Id}-{nombre-corto} desde v2.

Alcance:
- Debes construir: {lista de lo que entra, copiada de la spec}.
- NO construyas: {lo que es de otras tareas}.
- Archivos de otros módulos que puedes tocar: {lista; vacía si ninguno}.
- Depende de (ya unido en v2): {Ids}.

Criterios de aceptación (cada uno con su test):
{lista numerada}

Reglas:
- Domain → Infrastructure (configuración + migración V2_{Id}_*; SQL a mano en
  Migrations/Scripts/V2_{Id}.sql) → Application → API → cliente (scripts/generate-client.ps1) → UI.
- Si la especificación no alcanza o se contradice: DETENTE y deja la pregunta. No inventes reglas.
- No edites CLAUDE.md ni docs/.
- Tests de integración contra tu base de carril; bUnit para la UI nueva.
- Si corriges un bug de docs/BUGS.md: primero el test que lo reproduce (debe fallar), luego el arreglo.

Al terminar, deja el resumen de entrega de §6.2 y detente.
```

## 6. Unir una tarea

### 6.1 Lo que revisa el integrador

1. La tarea cumple su alcance y **nada más**.
2. Todos los criterios de aceptación tienen su test, y pasan.
3. Las reglas de `CLAUDE.md`: sin escrituras cruzadas nuevas, eventos después del commit, `IClock`,
   días de negocio, el patrón de 400 frente a 422 y 409.
4. Une a `v2`. Si el snapshot choca, regenera la migración (§2.3). Regenera el cliente.
5. Corre **toda** la suite (integración y bUnit) contra `gsm_test`.
6. Si la tarea es visible, una verificación corta en el navegador.

### 6.2 Resumen de entrega del agente

```
Tarea: {Id} — {Nombre}
Rama: v2/{Id}-...
Hecho: (lista)
Migración: V2_{Id}_... ; SQL a mano: sí/no (Migrations/Scripts/V2_{Id}.sql)
Cliente regenerado: sí/no
Contratos públicos nuevos o cambiados: (I{Módulo}Reader, eventos, endpoints)
Tests: N nuevos, todos pasan en gsm_test_{carril}
Preguntas abiertas / desvíos de la spec: (o "ninguno")
Bugs: corregidos (Bx) / encontrados (descripción para docs/BUGS.md)
```

### 6.3 Al cerrar cada ola

- `v2` compila y pasa todos los tests.
- El integrador actualiza `CLAUDE.md` (estado y notas que afecten a otros módulos) y `docs/BUGS.md`.
- El dueño del producto aprueba antes de lanzar la siguiente ola.

## 7. Cuándo se puede desplegar

- **Tras la Ola 3:** el núcleo diario de la v2 queda completo (cuentas, estado comercial, recepción,
  caja, horario, clases con reglas, permisos). Es el primer punto razonable para llevar `v2` a `main`
  y desplegar en el ambiente de pruebas con un gym piloto.
- **Tras la Ola 6:** la v2 completa.

## 8. Riesgos del plan

| Riesgo | Mitigación |
|---|---|
| **I2 (cuentas con varios gyms) toca la autenticación de todo el sistema** | Va sola en su carril en la Ola 1, con tests HTTP de identidad real (patrón `SubscriptionGateHttpTests`), y antes que todo lo que dependa de permisos. |
| **AP1 y AP2 tocan casi todo el frontend** | Van en las Olas 0 y 1, antes de que otros carriles agreguen pantallas. |
| Choques de migraciones | §2.3: migraciones regenerables y SQL a mano en archivos aparte. |
| **La app Android nunca se probó en un dispositivo** | AP3/N3 no se dan por terminadas sin una prueba en Android real o emulador (§4). |
| Un agente toma decisiones de diseño por su cuenta | Regla 4 de §1.2 y revisión del integrador. |
| Muchos manejadores de eventos nuevos | Deben ser idempotentes (lo exige el reintento de `GSM-logs.md` §4.3); es un criterio de revisión. |
| Servicios externos sin configurar | §4: empezar las cuentas en la Ola 1. |

## 9. Bugs

| Bug | Se corrige en |
|---|---|
| B1 Editar una rutina borra el historial | R1 (Ola 1) |
| B2 Comparativo de ingresos del dashboard | P3 (Ola 3); D1 lo reutiliza |
| B3 Formato del precio | Después de AP2 (Ola 1), carril Apps |
| B4 El aforo no se usa | A5 (Ola 4) |

## 10. Índice de tareas

| Grupo | Tareas | Ola |
|---|---|---|
| Cimientos | X0, AP1, LG1, T1 | 0 |
| Identity | I2 · I3, I1 · I7, I5, I6 · I4 | 1 · 2 · 3 · 4 |
| Members | M1 · M2 · M1b, M3, M4 · M5, M6 | 1 · 2 · 3 · 5 |
| Memberships | MS1 · MS2–MS7 · MS8 | 2 · 4 · 5 |
| Attendance | A1, A1b, A2 · A3–A6, A7 | 3 · 4 |
| Gyms | G2, G3 · G1 · G4 · G5, G6 | 2 · 3 · 4 · 5 |
| Payments | P1 · P2, P3 | 1 · 3 |
| Routines | R1, R2 · R3 · R4 · R5 · R6–R10 · R11 | 1 · 2 · 3 · 4 · 5 · 6 |
| Equipment | E1 · E2, E3 · E4 | 2 · 3 · 4 |
| Classes | C1, C2, C3, C5 · C4, C6, C7, C8 | 3 · 4 |
| Achievements | L1, L2, L4, L5, L6 · L3, L7 | 4 · 5 |
| Communications | CO1, CO3 · CO2, CO5, CO7 · CO4 · CO6 | 2 · 3 · 4 · 5 |
| Notifications | N1 · N2, N4 · N5 · N3 | 1 · 2 · 3 · 4 |
| SuperAdmin | S5, S6 · S7 · S1 · S2 · S3 · S4 | 1 · 2 · 3 · 3 · 4 · 5 |
| Apps | AP1 · AP2 · AP3, AP4, AP5 · AP6, AP7 | 0 · 1 · 4 · 5 |
| Logs | LG1 · LG2, LG3, LG4 · LG5 | 0 · 4 · 5 |
| Dashboards | D1, D2, D3 | 5 |
| Cierre | R11, V1, DP | 6 |

## 11. Especificaciones de la v2

| Tema | Documento |
|---|---|
| Miembros | `docs/GSM-modulo-members.md` |
| Rutinas y ejercicios | `docs/GSM-modulo-routines.md` (sección "Replanteamiento v2") |
| Equipos (nuevo) | `docs/GSM-modulo-equipment.md` |
| Membresías | `docs/GSM-modulo-memberships.md` |
| Asistencia | `docs/GSM-modulo-attendance.md` |
| Gyms | `docs/GSM-modulo-gyms.md` |
| Pagos | `docs/GSM-modulo-payments.md` |
| Identity | `docs/GSM-modulo-identity.md` |
| Clases | `docs/GSM-modulo-classes.md` (sección "Replanteamiento v2") |
| Logros | `docs/GSM-modulo-achievements.md` |
| Comunicaciones | `docs/GSM-modulo-comunicaciones.md` (sección "Replanteamiento v2") |
| Notifications (nuevo) | `docs/GSM-modulo-notifications.md` |
| SuperAdmin | `docs/GSM-modulo-superadmin.md` (sección "Replanteamiento v2") |
| Apps y vistas | `docs/GSM-apps-y-vistas.md` |
| Dashboards | `docs/GSM-dashboards.md` |
| Logs | `docs/GSM-logs.md` |
| Bugs | `docs/BUGS.md` |
| Fuera de la v2 | Reports y Locations (para más adelante) |
