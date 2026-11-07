# App del miembro v3 (TrainingHub GO)

> **Construida en noviembre de 2026** sobre la rama `main`. Reemplaza la organización de la app del miembro descrita
> en `GSM-dashboards.md` §2 y `GSM-apps-y-vistas.md` §6: lo que aquí cambia manda sobre esos textos.

## 1. Por qué

La app se sentía «regada»: lo mismo en varios sitios (el progreso en tres pantallas, la entrada y las novedades en
Inicio y en otras pestañas), sin una acción principal y con un aspecto plano. Se reordenó alrededor de lo que hace el
miembro: **entrenar** primero; entrar al gym, ver si avanza, sus clases y su gym después.

## 2. Estructura

**Barra inferior flotante** (vidrio, celular): **Hoy · Plan · botón central · Progreso · Gym**. En escritorio, los
mismos destinos en el menú lateral.

| Destino | Ruta | Qué es |
|---|---|---|
| Hoy | `/app` | Tablero de contexto rápido (§3). |
| Plan | `/app/plan` | Planificador por semanas y los días de la rutina (§4). |
| Botón central | `/app/rutina` | La barra de press en un círculo rojo: el **entrenamiento de hoy** (§5). |
| Progreso | `/app/progreso` | Constancia (racha, calendario, volumen semanal), Fuerza (récords), Logros y Ranking. `/app/logros` y `?tab=` se conservan (los avisos los usan). |
| Gym | `/app/gimnasio` | Ahora (abierto y ocupación), clases, novedades, horario y contacto. Activa también en `/app/clases` y `/app/novedades`; lleva el contador de novedades sin leer. |

**Encabezado de toda la app:** el nombre del gym, el botón **«Entrar»** (QR de la entrada, solo si el gym usa
`CheckinQr`; con lector abre la cámara, en el navegador explica cómo escanear) y el menú de la cuenta (perfil,
membresía, autorizaciones, avisos, contraseña, salir).

**Estilo:** redondeado y vidrio, solo en la app del miembro (`GSM.UI.Shared/wwwroot/gsm-member-v3.css`, todo bajo
`.gsm-m-shell`); el panel conserva Forja. Texto secundario `#B8B2AD`.

## 3. Hoy

En orden: saludo y estado; **aviso de membresía** solo si hay algo que atender (por vencer o en gracia, con
«Renovar», que abre Mi membresía: la app no cobra); **novedades** en una sola tarjeta si hay algo fijado o sin leer
(«N sin leer», abre Novedades; no crece con la cantidad); pendientes (autorizaciones, cupos liberados, premios);
la entrada de hoy; **lo que toca** con foto y «Empezar a entrenar»; el anillo de la semana y la racha; **Tu cuerpo
hoy** (recuperación estimada, §7); tu clase; el gym ahora; tu progreso.

## 4. Plan

`RoutineWeekPlanner`: la semana de lunes a domingo (hasta 26 semanas atrás o adelante) con lo que toca cada día, lo
hecho y hoy («Hoy» en lugar de la letra). Lee `GET /api/me/routines/week?date=`, que resuelve cada fecha con
`RoutineCalendar` (respeta rotativas y la sesión perdida). Debajo, los días de la rutina con su foto, cifras y mapa
muscular; el de hoy va primero con «Entrenar». Tocar otro día abre Entrenar con `?dia=`.

## 5. Entrenamiento de hoy y modo sesión

**Antes de empezar** (`/app/rutina`): portada con la foto del día de borde a borde (rutina, día, ejercicios, series,
tiempo estimado, revisión); la sesión perdida si aplica; si el día elegido en Plan no es el de hoy, el aviso «Hoy es tu
día de Pierna» con «Cambiar a Pierna»; en descanso, «Elegir un día» (va a Plan); «Hoy trabajas» con el mapa muscular;
«Empezar»; la lista completa de ejercicios (foto, meta, cambio por una alternativa; se abren para ver su mapa, el equipo
y registrar). El selector de días que había aquí se quitó: el día se elige en Plan.

**Modo sesión** (al tocar «Empezar»): un ejercicio por pantalla.

- Foto o video, mapa muscular, meta y «la vez pasada».
- Series en filas grandes, **prellenadas** con la meta o la última carga (historial de 120 días).
- **Guardado por serie:** cada ✓ reenvía las series hechas del ejercicio al endpoint de registro, que reemplaza el del
  día; desmarcar todas borra el registro. No hizo falta API nueva.
- **Descanso que inicia el miembro** («Iniciar descanso · 90 s», con +15 s y «Saltar»; al terminar vibra).
- **Aviso de récord** en el momento (contra el mejor peso de `GET /api/me/progress`).
- Anterior / Siguiente / Terminar y la lista para saltar a cualquier ejercicio.
- **Resumen:** duración (inicio guardado en `localStorage`), ejercicios, series, volumen, músculos, récords y lo que
  faltó.

**Sin RIR en la app del miembro:** las metas, las series y el editor de rutina propia no lo muestran
(`SetPlan.Summary(..., withRir: false)`, `RoutineForm ShowRir="false"`). El panel lo conserva.

## 6. Material visual

- **Fotos:** la de cada ejercicio (`ImageUrl` del catálogo). Mientras no haya fotos reales, `PlaceholderMedia` usa
  imágenes de muestra de Unsplash por músculo principal. **Temporal.**
- **Mapa muscular** (`MuscleMap`): silueta propia de frente y espalda, una zona por músculo del catálogo. Principal o
  cansado en rojo (`#FF3347`), secundario o recuperando en naranja (`#FF9F43`), el resto en gris.
- **Medallas de logros:** ver `GSM-modulo-achievements.md` §12.

## 7. Datos nuevos del backend

| Endpoint | Qué devuelve |
|---|---|
| `GET /api/me/routines/week?date=` | La semana con lo que toca, lo hecho y hoy (`GetMyRoutineWeekHandler`). |
| `GET /api/me/body` | Recuperación estimada por músculo (0–100), días desde el último entreno y músculos listos (`MemberBodyCalculator`, función pura). Regla: el principal queda en 0 % y se recupera en 2 días (3 con más de 6 series); el secundario queda en 50 % y se recupera en 1 (2 con más de 6); sin calentamientos. Es una estimación. |
| `GET /api/me/progress/weekly?weeks=` | Volumen (Σ peso × reps), series y sesiones por semana (8 por defecto, máximo 26). |

`RoutineExerciseResponse` suma `PrimaryMuscle`, `SecondaryMuscles`, `ImageUrl` y `VideoUrl`.

## 8. Pendiente

- Fotos reales de ejercicios (falta R2) para retirar `PlaceholderMedia`.
- Autorizaciones bloqueantes (propuesto, sin construir): el tratamiento de datos bloquea la app al entrar; la
  exoneración se pide al tocar «Empezar»; nunca a menores (las acepta el acudiente).
- Recorrer en Android.
