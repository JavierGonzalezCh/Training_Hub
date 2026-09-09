# Plan de construcción — Módulo SuperAdmin (Etapa 11)

> Documento de trabajo para Claude Code. La **especificación funcional** está en `docs/GSM-modulo-superadmin.md`; este archivo dice en qué orden se construye. Complementa `CLAUDE.md`: **si algo aquí lo contradice, él manda.**
>
> **Ritmo:** una sub-etapa por vez, sin empezar la siguiente sin confirmación explícita. Cada una termina compilando, con su migración aplicada, sus endpoints respondiendo y **la suite completa en verde**.

---

## Punto de partida

Etapas 0–9 completas salvo el despliegue (etapa 8, punto 4), y **Classes (etapa 10) en construcción**. La cifra de tests a re-correr es la que haya cuando se arranque: eran 48 al cerrar Routines y Classes está sumando las suyas. Convenciones que este módulo hereda sin excepción:

- `IClock` para toda lectura de tiempo. `Today` para días de negocio, `BusinessDayRange` para agrupar por mes.
- `LowerCaseNameTranslator.Instance` para enums de una palabra, `SnakeCaseNameTranslator.Instance` para los multi-palabra. Nunca `new`.
- Un caso de uso por archivo con `HandleAsync`, inyectado directo en el controller.
- Errores por la jerarquía de `DomainException`.
- Migración por sub-etapa, nombrada `Etapa11_{n}_{Nombre}`.

## Lo que hace distinto a este módulo

**Es el primero que opera por encima del multi-tenancy.** Los demás viven dentro de un gym; este los gestiona a todos. Dos consecuencias:

1. Sus consultas recorren la base entera. Proyección explícita siempre, nunca cargar entidades.
2. **La sub-etapa 11.2 toca todos los endpoints del sistema.** Es, con diferencia, lo más arriesgado que se ha hecho en el proyecto.

---

# 11.1 — Suscripciones y estado efectivo

**Objetivo:** cada gym tiene su suscripción y el sistema sabe en qué estado comercial está. Todavía **sin bloquear nada**.

### Trabajo

1. **Domain** — `GymSubscription` y el enum `EffectiveSubscriptionStatus` (`trial`, `active`, `overdue`, `disabled`, `suspended`). La derivación del estado es un **método puro** sobre la entidad, que recibe el `Gym.Status` manual y el día de hoy: así se prueba sin base de datos.
2. **Infrastructure** — configuración con único `(gym_id)` e índice `(paid_through)`, repositorio.
3. **Application** — `Public/ISubscriptionReader` (`GetEffectiveStatusAsync`, `IsWriteAllowedAsync`), y casos de uso: listar cartera con filtros, ficha del gym, actualizar plan y precio, renovar sin pago, suspender y reactivar. `suspend`/`reactivate` escriben `Gym.Status`, que es de Gyms: va por `IGymReader`… **y aquí no hay lectura que valga**, así que se resuelve como se indica abajo.
4. **API** — `AdminGymsController` bajo `/api/admin/gyms`, todo con `RequireSuperAdmin`.
5. **Migración de datos** — crear una suscripción para **cada gym existente**, en `active` y con `paid_through` a 30 días vista. Sin esto, los gyms que ya están en la base quedan sin suscripción.

### El punto delicado: suspender toca Gyms

`suspend` y `reactivate` cambian `Gym.Status`, que pertenece a otro módulo. Opciones, en orden de preferencia:

- **Añadir `SetCommercialStatus` a `Gyms.Public.IGymAdministration`** — una interfaz de escritura en el `Public/` de Gyms, siguiendo el patrón ya documentado en `CLAUDE.md`. Es una **tercera excepción**, y `CLAUDE.md` obliga a justificarla: aquí se justifica porque el resultado (el gym queda suspendido) es la respuesta misma de la operación, no una reacción.
- Alternativa si se prefiere no abrir otra excepción: mover el estado comercial a `GymSubscription` y dejar `Gym.Status` obsoleto. Es más limpio conceptualmente pero **sí exige migración** de Gyms, que la especificación buscaba evitar.

**Decidir antes de escribir código.** No empezar con las dos opciones a medias.

### Migración
`Etapa11_1_Subscriptions` — tabla `superadmin.gym_subscriptions` + alta de suscripciones para los gyms existentes.

### Aceptación
- Un gym con `paid_through` de ayer se lee como `overdue` sin ejecutar nada.
- A los 16 días de vencer, `disabled`. Por el paso del tiempo, no por un job.
- `suspended` solo por acción manual; jamás derivado.
- Renovar devuelve a `active` de inmediato.
- Todos los gyms preexistentes tienen suscripción tras migrar.
- La suite completa sigue en verde.

---

# 11.2 — Bloqueo por estado

**Objetivo:** el estado comercial tiene consecuencias reales.

> ⚠️ **La sub-etapa de mayor riesgo del proyecto.** Un fallo aquí no rompe una función: deja fuera a gimnasios enteros. Va sola, sin mezclar con otro trabajo.

### Trabajo

1. **Filtro global en la API**, después de la autenticación:
   - `disabled` → todo lo que no sea `GET` responde **`403`** con un `ProblemDetails` que explique el motivo.
   - `suspended` → **todo** rechazado, incluido el login.
   - `overdue` → no bloquea.
   - **El superadmin queda siempre exento.**
2. **Un gym sin suscripción no se bloquea.** Decisión explícita: ante un dato faltante, se deja pasar. Bloquear a un cliente por un registro que falta es peor error que no bloquear a uno moroso, y además protege a los tests y a cualquier gym creado por otra vía.
3. **Caché por petición** del estado: una sola consulta por request, no una por caso de uso.
4. **El bloque `subscription`** en `GET /api/gym` y `GET /api/auth/me`, con `status`, `paidThrough` y `daysUntilDisabled` cuando aplique.
5. **Endpoints exentos** además de los de admin: `/health` y el refresh de token (para no dejar sesiones colgadas a mitad de una operación legítima).

### Migración
Ninguna.

### Aceptación
- Un gym `disabled` hace `GET` con normalidad y recibe `403` en cualquier escritura.
- Un gym `suspended` no puede ni iniciar sesión.
- Un gym `overdue` opera con total normalidad y ve `daysUntilDisabled` correcto.
- Un gym **sin suscripción** opera con normalidad.
- El superadmin opera sobre cualquier gym en cualquier estado.
- **Los tests existentes pasan sin modificarse.** Si hay que tocarlos, es que el filtro bloquea de más.

---

# 11.3 — Pagos de suscripción

**Objetivo:** registrar que un gym pagó, y que eso lo renueve solo.

### Trabajo

1. **Domain** — `SubscriptionPayment`, **inmutable**: sin métodos de edición. Invariantes: monto distinto de cero, `covers_through >= covers_from`, `registered_by` obligatorio.
2. **Infrastructure** — índices `(gym_id, paid_at)` y `(paid_at)`. Reutiliza el enum `payment_method` de la etapa 5 (`LowerCaseNameTranslator`, ya registrado).
3. **Application**
   - `RegisterSubscriptionPaymentHandler` — guarda el pago **y** empuja `paid_through` hasta `covers_through`, **en una transacción**. `registered_by` de `ICurrentUserService`, nunca del body.
   - Periodo por defecto: desde el día siguiente al `paid_through` vigente, un mes.
   - Histórico por gym y por periodo, con proyección explícita.
4. **API** — `POST`/`GET /api/admin/gyms/{id}/payments` y `GET /api/admin/payments`. **Sin `PUT` ni `DELETE`**: la inmutabilidad se garantiza porque la ruta no existe.

### Migración
`Etapa11_3_SubscriptionPayments` — tabla `superadmin.subscription_payments`.

### Aceptación
- Registrar un pago empuja `paid_through` y devuelve el gym a `active` en la misma operación.
- Si falla el guardado del pago, `paid_through` **no** se mueve (test de rollback).
- El periodo por defecto arranca el día siguiente al `paid_through` vigente.
- `PUT` y `DELETE` sobre un pago responden `405`.
- Un pago negativo (ajuste) se acepta; uno de cero, `422`.

---

# 11.4 — Panel

**Objetivo:** una pantalla que responda "cómo va el negocio".

### Trabajo

1. **`GetAdminDashboardHandler`** — compone `ISubscriptionReader`, `IMemberReader`, `IAttendanceReader` y `IPaymentReader`, siguiendo el patrón del dashboard de la etapa 8.
2. **Cartera** — recuento por estado efectivo y los que vencen en 15 días.
3. **Ingresos, dos cifras:** contratado (suma de `monthly_price` de los no suspendidos ni inhabilitados) y cobrado (suma de pagos del mes, con el mes anterior al lado).
4. **Uso por gym** — socios activos, check-ins del mes y último movimiento de cada cliente.
5. **Totales de plataforma** — agregados de todos los gyms.

> **Agrupar por día de negocio, no por rango UTC.** Es exactamente el defecto que se encontró y corrigió en la etapa 5 (`GetMonthlyRevenueAsync`); repetirlo aquí sería fácil. Usar `IClock.BusinessDayRange`.

> **Proyección explícita siempre.** Estas consultas recorren la base entera, no un tenant: nada de materializar entidades para contarlas.

### Migración
Ninguna.

### Aceptación
- Los ingresos cobrados del mes cuadran con la suma de los pagos, **agrupados por día de negocio** (test con un pago a las 22:00 hora Colombia el último día del mes).
- Los contratados suman solo gyms no suspendidos ni inhabilitados.
- La cartera cuadra con el estado efectivo de cada gym.

---

# 11.5 — Impersonación

**Objetivo:** poder mirar el sistema de un cliente para dar soporte, sin poder tocarlo y dejando rastro.

### Trabajo

1. **Domain** — `ImpersonationSession` con `reason` obligatorio.
2. **Identity** — `JwtTokenService` emite un token de **15 minutos** que mantiene `sub` (la identidad real del superadmin), lleva el `gym_id` visitado e incluye `impersonating: true` más el id de la sesión.
3. **El filtro de 11.2 se amplía**: con `impersonating`, cualquier método distinto de `GET` responde `403`. La restricción vive en infraestructura, no en cada endpoint.
4. **Auditoría** — cada petición con ese token se loguea con el id de sesión, no solo la apertura.
5. **API** — abrir, cerrar y consultar el historial.

### Migración
`Etapa11_5_Impersonation` — tabla `superadmin.impersonation_sessions`.

### Aceptación
- Con token de impersonación, un `GET` del gym visitado funciona; cualquier escritura, `403`.
- El token caduca a los 15 minutos.
- Sin motivo, la apertura responde `422`.
- La sesión queda registrada con quién, dónde, cuándo y por qué.
- El token de impersonación **no** sirve para entrar a otro gym distinto al de su claim.

---

## Resumen de migraciones

| Sub-etapa | Migración | Contenido |
|---|---|---|
| 11.1 | `Etapa11_1_Subscriptions` | `gym_subscriptions` + alta para gyms existentes |
| 11.2 | — | sin cambios de esquema |
| 11.3 | `Etapa11_3_SubscriptionPayments` | `subscription_payments` |
| 11.4 | — | sin cambios de esquema |
| 11.5 | `Etapa11_5_Impersonation` | `impersonation_sessions` |

## Tests que deja el módulo

Además de re-correr la suite completa en cada sub-etapa:

- **Derivación de estado** — unitario, sin base: vencido ayer, vencido hace 16 días, suspendido manual, trial.
- **Bloqueo** — `disabled` lee pero no escribe; `suspended` no entra; `overdue` opera; sin suscripción, opera.
- **Renovación por pago** — atómica, con test de rollback.
- **Ingresos por día de negocio** — el caso límite de las 22:00 del último día del mes.
- **Impersonación** — lectura sí, escritura no, caducidad, motivo obligatorio, aislamiento entre gyms.

## Orden y dependencias

```
11.1 suscripciones ──> 11.2 bloqueo ──> 11.3 pagos ──> 11.4 panel ──> 11.5 impersonación
```

El corte natural para pausar es **al final de 11.3**: ahí ya puedes cobrar, controlar quién debe y cortar el servicio al que no paga, que es lo que hace falta para vender. El panel y la impersonación son comodidad.

**11.2 va sola.** Es la única sub-etapa del proyecto que puede dejar clientes sin servicio si se equivoca; no se mezcla con otro trabajo ni se apura.
