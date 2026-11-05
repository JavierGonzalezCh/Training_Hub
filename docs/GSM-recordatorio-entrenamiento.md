# Recordatorio de la sesión de entrenamiento (TR)

**Estado:** diseño, sin construir. Pedido el 2026-11-02 para el tenant de entrenador personal; sirve a cualquier tipo de negocio (no hay `if (tipo == …)`).

## 1. Qué resuelve

El miembro recibe un push (y el aviso en su bandeja) los días que **su rutina dice que toca entrenar**, a una hora que él elige: «Hoy toca *Pierna y glúteo* · 6 ejercicios». Los días de descanso planificado no hay aviso. Si ese día ya entrenó antes de la hora, el aviso se cancela.

No es el recordatorio de clase (`ClassReminder`, N5): ese depende de una reserva y aquí las clases pueden estar apagadas.

## 2. Reglas

| Regla | Decisión |
|---|---|
| Qué día toca | **`RoutineCalendar.Resolve(...).IsTraining`**, la misma regla de la racha y el cumplimiento. No se reimplementa. |
| A qué hora | La elige el miembro (`TimeOnly`, hora local del gym). Sin elegir: la que fije el gym (por defecto 07:00). |
| Quién decide si existe | El gym lo enciende o apaga (`RoutineSettings.TrainingReminderEnabled`, por defecto **encendido** solo en la plantilla de `personal_trainer` y `studio`; en las demás, apagado). El miembro puede apagarlo para sí. |
| Categoría de preferencias | `Progress` (ya existe): el miembro que apaga push de Progreso no lo recibe. |
| Horario sin avisos | Lo respeta (prioridad `Normal`). Si la hora elegida cae dentro, sale al terminar el horario sin avisos — se avisa al miembro al elegirla. |
| Ya entrenó | Si antes de la hora ya hay registro de un ejercicio de ese día (`RoutineExerciseLogged`) o check-in, se cancela el aviso del día. |
| Sin rutina activa o sin días vigentes | No hay aviso. |
| Membresía vencida fuera de gracia | No hay aviso (no tiene sentido empujar a quien no puede entrar). Se consulta `IMembershipReader.GetAccessStatusAsync`. |
| Idempotencia | `(TrainingReminder, miembro, referencia)` con referencia = Guid determinista de `(memberId, fecha)`. Repetir la tarea no duplica. |

## 3. Cómo encaja (sin escritura cruzada nueva)

Mismo patrón que el cumpleaños (CO5): **la tarea periódica publica un evento y Notifications programa el aviso.**

```
POST /internal/jobs/communications/run  (cada 5–10 min, ya existe)
  └─ Routines: PublishTrainingRemindersHandler
       · gyms con TrainingReminderEnabled
       · miembros con rutina activa y hoy IsTraining (RoutineCalendar)
       · publica TrainingReminderDue(gymId, memberId, date, routineDayName, exerciseCount, sendAt)
           └─ Notifications: TrainingReminderDueHandler
                · ScheduledNoticeService.ScheduleForMemberAsync(type: TrainingReminder, ScheduledFor: sendAt)
                · el dispatcher (30 s) lo envía por FCM a su hora
RoutineExerciseLogged / AttendanceRegistered
  └─ Notifications: cancela TrainingReminder de (miembro, hoy) si aún no salió
```

- Se programa **solo el día de hoy** (la primera corrida del día de negocio, desde las 00:00 locales). No se generan avisos por adelantado: cambiar la rutina o la hora afecta desde la siguiente corrida.
- Cambiar la hora o apagarlo hoy reprograma o cancela el aviso pendiente de hoy (`Reschedule` / `CancelForMemberAsync`, ya existen).
- `IAdherenceSource` ya da las rutinas con sus días en la forma que `RoutineCalendar` necesita.

## 4. Datos

| Dónde | Qué |
|---|---|
| `notifications.notification_type` | valor nuevo `training_reminder` (`ALTER TYPE … ADD VALUE`, irreversible) |
| `routines.routine_settings` | `training_reminder_enabled bool` y `training_reminder_default_time time` (perezosa, como hoy) |
| `routines.member_training_reminders` (nueva) | `member_id`, `gym_id`, `enabled bool`, `time time null` — una fila por miembro, solo si cambió algo |

## 5. API y pantallas

- `GET/PUT /api/me/training-reminder` — el miembro: encendido y hora.
- Ajustes del gym: dos campos más en `PUT /api/routines/settings` (pestaña **Rutinas** de `/settings`).
- **App del miembro (GO):** en Perfil → Notificaciones, «Recordarme entrenar» con selector de hora; aviso si cae en el horario sin avisos.
- **Plantillas de tipo:** `personal_trainer` y `studio` lo encienden en `GymCreatedAppliesRoutineTemplateHandler`, y entra en «Ver la configuración recomendada».

## 6. Etapas

| Etapa | Qué | Tests |
|---|---|---|
| **TR1** | Dominio + migración: tipo de aviso, ajustes del gym, preferencia del miembro | integración: ajustes perezosos |
| **TR2** | Tarea + evento + handler de Notifications + cancelación al entrenar | integración: toca / descanso / ya entrenó / sin rutina / vencido / idempotente / zona horaria (Colombia UTC-5) |
| **TR3** | API (`/api/me/training-reminder`, ajustes) + cliente NSwag | integración HTTP |
| **TR4** | Pantallas GO y `/settings`, plantilla y recomendada | bUnit |

## 7. Lo que necesita el dueño (fuera del código)

1. **Firebase** (`Notifications:Fcm:*` en el servidor) y el `google-services.json` en `GSM.Mobile`: sin eso el aviso llega a la bandeja pero no hay push.
2. **La tarea periódica programada en Dokploy** (`POST /internal/jobs/communications/run` con `X-Job-Secret`): sin ella no se programa ningún recordatorio (tampoco hoy los de vencimiento ni cumpleaños). Hay que confirmar que ya corre en producción.

## 8. Fuera de alcance

Recordatorios a varias horas por día, avisos «llevas 2 días sin entrenar» (retención), y que el entrenador fije la hora por miembro. Se pueden agregar después sobre la misma tabla.
