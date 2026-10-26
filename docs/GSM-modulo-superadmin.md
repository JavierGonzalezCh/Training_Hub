# Módulo SuperAdmin — Especificación

> Fase 3. Complementa `CLAUDE.md` y `docs/GSM-especificacion-tecnica.md`; **si algo aquí contradice a esos dos, ellos mandan.** Sigue las convenciones transversales de `docs/DESARROLLO.md`.

**Schema:** `superadmin` · **Toca también:** `identity` (impersonación) y la API (bloqueo por estado)

Este es el módulo que gestiona los gimnasios **como clientes del SaaS**. Es el único que opera por encima del multi-tenancy en lugar de dentro de él.

---

## Replanteamiento v2 — construido

> Nada de esta sección está construido. Lo que viene después de ella describe **la v1, que es lo
> construido hoy**. Donde las dos se contradicen, manda esta sección una vez aprobada.

### Decisiones tomadas

| Tema | Decisión |
|---|---|
| Planes del SaaS | **Definen un límite de miembros.** Todos los planes tienen todos los módulos (§V1). |
| Prueba gratis | **Opcional, por gym.** Al crear el gym, el SuperAdmin decide si tiene prueba y cuántos días. Al terminar la prueba, el gym tiene **3 días** para que se confirme el pago; si no, **se inhabilita** (§V2). |
| Avisos al gym | **Correo y push a los Owners** antes y después del vencimiento (§V3). |
| Métricas | **Foto mensual** de las métricas del SaaS (§V4). |
| Mora | Los días antes de inhabilitar son **configurables por cliente** (§V2). |
| SuperAdmin | **Uno solo**, con permiso para todo (§V5). |
| Impersonación | Lectura o escritura, **en silencio**: el gym nunca se entera (§V6). |

Heredadas: alta con invitación al Owner y "Reenviar acceso" / "Copiar enlace" / "Omitir onboarding"
(`GSM-modulo-gyms.md` §4–§5); agregar y quitar Owners (Gyms §6); catálogos del sistema (§V7); métodos de
pago de Colombia (`GSM-modulo-payments.md` §3).

### V1. Planes con límite de miembros

- Tabla nueva `superadmin.saas_plans`: `Name`, `MemberLimit` (nulo = sin límite), `SuggestedPrice`
  (orientativo; el precio real sigue siendo el de cada suscripción), `IsActive`. La edita el SuperAdmin.
  Reemplaza al enum fijo `gym_plan` (`basic`/`pro`/`enterprise`), que migra a tres filas con esos
  nombres.
- **Límites iniciales** (aprobados 2026-10-07): Basic 150, Pro 500, Enterprise sin límite.
- `GymSubscription` suma `MemberLimitOverride` (nulo = el del plan): el SuperAdmin puede darle a un
  cliente un límite distinto sin cambiarle el plan.
- **Qué cuenta:** miembros con una membresía que los cubre hoy (al día, por vencer, en gracia o
  congelados). Una ficha sin plan o vencida no cuenta.
- **Qué se bloquea al llegar al límite:** **dar una membresía a alguien que hoy no cuenta** (un miembro
  nuevo o uno que vuelve). **Renovar a quien ya cuenta nunca se bloquea**: no se le puede negar la
  renovación a un miembro actual.
  - Mensaje al staff: "Llegaste al límite de 150 miembros activos de tu plan. Habla con GSM para
    ampliarlo".
- **Avisos:** al 90 % del límite, a los Owners ("Te quedan 15 cupos") y al SuperAdmin; al llegar al
  límite, a ambos. Ver §V3.
- El panel del SuperAdmin muestra el uso del límite de cada gym ("142 / 150").

### V2. Prueba gratis opcional, mora configurable

**Al crear el gym**, el SuperAdmin elige:

- **Con prueba:** cuántos días (por ejemplo, 15 o 30). El gym nace en `trial` hasta `trial_ends_on`.
- **Sin prueba:** el formulario **exige el primer pago** (monto, método y periodo que cubre). El gym
  nace `active` hasta esa fecha. No existe un gym sin prueba y sin pago (confirmado 2026-10-07).

**Al terminar la prueba:**

| Estado | Cuándo | Qué puede hacer el gym |
|---|---|---|
| `trial` | Hasta `trial_ends_on` | Todo |
| **`trial_ended`** (nuevo, derivado) | Los **3 días** siguientes al fin de la prueba, sin pago confirmado | Todo, con un letrero: "Tu prueba terminó. Confirma tu pago para seguir usando GSM" |
| `disabled` | Pasados esos 3 días sin pago | Solo consultar |

- Registrar el primer pago (confirmación manual del SuperAdmin) lo pasa a `active` en ese momento,
  como hoy.
- Los 3 días son el valor por defecto; se pueden cambiar por cliente (`TrialGraceDays`).

**Mora de un gym que ya pagaba:** igual que hoy (`overdue` → `disabled`), pero los 15 días pasan a ser
un campo por cliente (`OverdueGraceDays`, 15 por defecto).

Regla de estados actualizada:

```
si Gym.Status == suspended                         → suspended
si está en prueba y hoy <= trial_ends_on            → trial
si nunca pagó y hoy <= trial_ends_on + TrialGrace   → trial_ended
si hoy <= paid_through                              → active
si ya pagó alguna vez y hoy <= paid_through + OverdueGrace → overdue
en otro caso                                        → disabled
```

### V3. Avisos de la suscripción a los Owners

Por correo y push a **todos los Owners** del gym, además del letrero del panel (Notifications):

| Aviso | Cuándo |
|---|---|
| Tu prueba termina pronto | 3 días antes de `trial_ends_on` |
| Tu prueba terminó | El día siguiente: "Tienes 3 días para confirmar tu pago" |
| Tu suscripción vence pronto | 5 días antes de `paid_through` |
| Tu suscripción vence hoy | El día |
| Tu gym entró en mora | El día siguiente al vencimiento |
| Tu gym se inhabilitará pronto | 3 días antes de inhabilitarse |
| Tu gym fue inhabilitado / suspendido / reactivado | Al pasar |
| Pago registrado | Al registrar un pago: "Recibimos tu pago; tu suscripción va hasta el 30 nov" |
| Límite de miembros | Al 90 % y al llegar al límite (§V1) |

El SuperAdmin ve en su panel los gyms con prueba terminada, en mora y por inhabilitarse.

### V4. Métricas mensuales

- Al cerrar cada mes (lo hace la tarea periódica en su primera ejecución del mes siguiente; es
  idempotente por mes), se guarda una foto en `superadmin.monthly_metrics`:
  - **MRR** (ingreso recurrente mensual: suma de los precios de las suscripciones activas);
  - **cobrado** en el mes;
  - **gyms**: nuevos, en prueba, convertidos de prueba a pago, perdidos (inhabilitados o suspendidos
    en el mes), activos al cierre;
  - **churn**: gyms que eran clientes al inicio del mes (al día o en mora) y se perdieron ÷ activos al inicio del mes. Las pruebas que se caen sin pagar **no** cuentan: van aparte como **pruebas no convertidas** (decidido 2026-10-11);
  - **miembros activos** y **entradas** de toda la plataforma.
- El panel muestra la evolución de los últimos 12 meses.
- **Lo que no se guardó no se recupera**: la serie empieza el primer mes después de construirlo.

### V5. Un solo SuperAdmin

Decidido 2026-10-07: **hay una sola cuenta de SuperAdmin y puede hacer todo.**

- `AppUser.IsSuperAdmin` sigue siendo un booleano y **solo una cuenta puede tenerlo**. La base lo
  garantiza con un índice único parcial (`WHERE is_super_admin`).
- No hay roles ni equipo de plataforma.
- **Riesgo a tener presente:** es la cuenta más poderosa del sistema y un único punto de falla. Si se
  pierde el acceso, se recupera por "olvidé mi contraseña" con su correo; si se pierde el correo, solo
  queda una intervención directa en la base. El doble factor se descartó para esta versión
  (`GSM-modulo-identity.md` §2); conviene reconsiderarlo solo para esta cuenta.

### V6. Impersonación en silencio

- Al entrar a un gym se elige **lectura** o **escritura**. Se pide un motivo, que queda en la auditoría
  de la plataforma.
- **El gym nunca se entera** (decidido 2026-10-07): no hay aviso a los Owners, ni letrero, ni rastro
  visible en las pantallas del gym por **leer**.
  - Las consultas del SuperAdmin a datos de salud **no** se anotan en el registro de consultas que ve
    el Owner (`GSM-modulo-members.md` §10). Van a la auditoría de la plataforma, que solo ve el
    SuperAdmin.
- **Permiso para todo**, en los dos modos: datos de salud, notas internas, finanzas, Owners, staff,
  permisos, anonimizar.
- **Las escrituras sí quedan firmadas.** Un pago, una membresía o un cambio de ficha hechos por el
  SuperAdmin quedan registrados como hechos por **"GSM"** en el historial del gym. Ocultar quién hizo un
  cambio de datos rompería la trazabilidad de la caja y de los historiales; leer es silencioso, cambiar
  no puede serlo del todo.
- **Auditoría de la plataforma** (`superadmin.impersonation_sessions` + `superadmin.impersonation_actions`):
  sesión, motivo, modo, y cada consulta y cada escritura con endpoint, resumen y hora. Solo la ve el
  SuperAdmin.
- El token sigue durando 15 minutos y conserva la identidad real en `sub`.

### V7. Catálogos del sistema

Hoy no hay pantalla para ninguno (en F8 no se alcanzó a abrir el catálogo de ejercicios al SuperAdmin).
Sección **"Catálogos"** en `/admin`, solo para el SuperAdmin:

- **Ejercicios del sistema**: los 55 actuales y nuevos, con tipo, `tracking_type`, músculos, nivel,
  unilateral, imagen y video (`GSM-modulo-routines.md`, catálogo v2).
- **Alternativas recomendadas** entre ejercicios del sistema.
- **Máquinas del catálogo** (`EquipmentModel`, ~40) con su imagen de referencia
  (`GSM-modulo-equipment.md` §3.2).
- **Ejercicio ↔ máquina** del sistema (`routines.exercise_equipment_models`).
- Para resolver el problema de F8 (un SuperAdmin no tiene gym), estas pantallas usan endpoints propios
  `/api/admin/catalog/*` que trabajan solo con filas `gym_id IS NULL`, sin pasar por el contexto de gym.

### Sub-etapas (construidas)

| Sub-etapa | Qué |
|---|---|
| **S1** | Prueba opcional en el alta, estado `trial_ended`, días de gracia por cliente, primer pago en el alta sin prueba. (Con Gyms G1.) |
| **S2** | Planes con límite de miembros: tabla de planes, override por cliente, bloqueo al dar membresías nuevas, uso en el panel. |
| **S3** | Avisos de la suscripción a los Owners (depende de Notifications). |
| **S4** | Catálogos del sistema en `/admin`. Necesario antes de E1 (máquinas del catálogo) y de R3/R5 (catálogo de ejercicios v2). |
| **S5** | Cuenta única de SuperAdmin (índice único) y permiso total. |
| **S6** | Impersonación con escritura y en silencio; auditoría de la plataforma por consulta y por cambio. |
| **S7** | Métricas mensuales y su evolución en el panel. |

### Decisiones abiertas de la v1, resueltas

1. Los 15 días de gracia → configurables por cliente (§V2).
2. Aviso de vencimiento por correo → avisos a los Owners (§V3).
3. Facturación electrónica → sigue fuera.
4. Métricas SaaS → foto mensual (§V4).
5. Notificar al gym su propio estado → §V3.

## Decisiones de producto

1. **Cinco estados**, tres de ellos automáticos: `trial`, `active`, `overdue`, `disabled`, `suspended`.
2. **Suscripción con registro de pagos:** plan, precio mensual y vencimiento, más el histórico de lo que cada gym ha pagado. Sin facturación electrónica.
3. **Registrar un pago renueva el gym automáticamente:** decir "este gimnasio pagó" es la única acción necesaria.
4. **Impersonación de solo lectura y auditada.**
5. **Panel con cartera, ingresos (contratados y cobrados), uso por gym y totales de plataforma.**

---

## Los cinco estados

| Estado | Cómo se llega | Qué puede hacer el gym |
|---|---|---|
| `trial` | Al crearse, hasta `trial_ends_on` | Todo |
| `active` | Con la suscripción al día | Todo |
| `overdue` | **Automático:** vencido hace ≤ 15 días | Todo, **con aviso visible en el panel** |
| `disabled` | **Automático:** vencido hace > 15 días | **Solo lectura** |
| `suspended` | **Solo manual**, por un superadmin | **Nada** |

### El estado no se persiste: se deriva

`overdue` y `disabled` salen de comparar `paid_through` con el día de hoy. **No hay scheduler en el sistema** y no se introduce uno por esto — es el mismo criterio que la etapa 4 tomó con las membresías `expired`, y por la misma razón: un estado calculado nunca se queda desincronizado.

```
si Gym.Status == suspended        → suspended      (manual, manda siempre)
si Gym.Status == trial            → trial          (solo hasta trial_ends_on)
si hoy <= paid_through            → active
si hoy <= paid_through + 15 días  → overdue
en otro caso                      → disabled
```

**`trial_ends_on` nulo no es una prueba indefinida:** sin fecha de fin el gym cae directo en la
lógica de `paid_through`. Todo gym nace con `gym_status = trial`, así que tratar el nulo como
"trial para siempre" dejaría a la plataforma entera fuera del bloqueo.

**La suscripción se da de alta al crear el gym**, por un handler de `GymCreated`: plan el del gym,
precio 0 (aún no negociado) y 30 días de prueba (`trial_ends_on` = `paid_through` = hoy + 30). Si
ese handler fallara, el gym queda sin suscripción —y por tanto sin bloqueo, según la regla de
abajo—; `PUT /api/admin/gyms/{id}/subscription` la repone, creándola si no existe.

**`Gym.Status` no cambia.** El enum `gym_status` de la etapa 2 (`trial`/`active`/`suspended`) sigue tal cual y pasa a significar *estado comercial manual*; `overdue` y `disabled` son derivados y viven solo en el cálculo. Así este módulo **no migra nada** de Gyms.

### Cómo se aplica el bloqueo

Un filtro global en la API, después de la autenticación:

- `disabled` → rechaza todo lo que no sea `GET` con **`403`** y un `ProblemDetails` que explique el motivo. El gym puede consultar sus datos pero no cobrar, inscribir ni fichar.
- `suspended` → rechaza **todo**, incluido el login de sus usuarios.
- `overdue` → no bloquea nada.

El superadmin queda siempre exento.

> **El bloqueo va en un filtro, no repartido por los casos de uso.** Es una condición transversal, como el multi-tenancy; si se comprueba caso por caso, el día que se olvide en uno el agujero pasa inadvertido.

### El aviso de mora

`GET /api/gym` y `GET /api/auth/me` incluyen el estado efectivo y, cuando aplica, los días que faltan para la inhabilitación:

```json
"subscription": { "status": "overdue", "paidThrough": "2026-09-01", "daysUntilDisabled": 9 }
```

El panel del gimnasio muestra el letrero con eso. El backend no decide cómo se ve, solo entrega el dato.

---

## Entidades

### `GymSubscription` — una por gym

| Campo | Tipo | Notas |
|---|---|---|
| `id` | `Guid` | |
| `gym_id` | `Guid` | Único. Sin FK: otro schema; se valida con `IGymReader`. |
| `plan` | enum `gym_plan` | Reutiliza el de la etapa 2 (`basic`/`pro`/`enterprise`). |
| `monthly_price` | `decimal(12,2)` | COP. **Precio de este cliente**, no del plan. |
| `started_on` | `DateOnly` | |
| `trial_ends_on` | `DateOnly?` | |
| `paid_through` | `DateOnly` | Último día cubierto. Los pagos la empujan. |
| `notes` | `string(1000)?` | Acuerdos, contactos, lo que sea. |

> El precio vive en la suscripción, no en un catálogo de planes. En B2B el precio se negocia por cliente, y un catálogo obligaría a excepciones desde el primer descuento.

**Índices:** único `(gym_id)`, más `(paid_through)` para la cartera.

### `SubscriptionPayment` — lo que cada gym ha pagado

| Campo | Tipo | Notas |
|---|---|---|
| `id` | `Guid` | |
| `gym_id` | `Guid` | |
| `amount` | `decimal(12,2)` | COP. Puede ser negativo (ajuste). Nunca cero. |
| `method` | enum `payment_method` | Reutiliza el de la etapa 5. |
| `covers_from` · `covers_through` | `DateOnly` | Periodo que cubre este pago. |
| `paid_at` | `DateTimeOffset` | Instante del registro. |
| `registered_by` | `Guid` | El superadmin, de `ICurrentUserService`. Nunca del body. |
| `reference` | `string(200)?` | Número de transferencia, comprobante. |
| `notes` | `string(1000)?` | |

**Índices:** `(gym_id, paid_at)`, `(paid_at)` para los ingresos del mes.

> **Inmutable, igual que la caja del gym.** No se editan ni se borran: una corrección es otro asiento con monto negativo y concepto explícito. Es la misma regla de la etapa 5 y por el mismo motivo — es dinero, y el histórico tiene que poder auditarse. Cuesta lo mismo hacerlo bien desde el principio.

### `ImpersonationSession` — la auditoría

| Campo | Tipo | Notas |
|---|---|---|
| `id` | `Guid` | |
| `super_admin_user_id` | `Guid` | Quién entró. |
| `gym_id` | `Guid` | Dónde. |
| `reason` | `string(200)` | **Obligatorio.** |
| `started_at` · `ended_at` | `DateTimeOffset` / `?` | |

**Índices:** `(gym_id, started_at)`, `(super_admin_user_id, started_at)`.

---

## Registrar un pago renueva el gym

Es la operación central del módulo y hace **dos cosas en una transacción**: guarda el pago y empuja `paid_through` hasta `covers_through`.

```
Gym vencido el 2026-09-01, hoy es 2026-09-10 → overdue
   registrar pago de 150.000 que cubre 2026-09-01 → 2026-09-30
   paid_through pasa a 2026-09-30
   estado efectivo → active, al instante
```

**El periodo por defecto se calcula solo:** arranca el día siguiente a `paid_through` y dura un mes, para que el caso normal sea un clic. Se puede sobrescribir cuando el gym paga un semestre o se pacta un periodo distinto.

**Renovar sin pago** (`POST /renew`) queda como operación aparte, para cortesías o correcciones de fecha. Deja rastro en las notas de la suscripción, pero no inventa un pago que no existió — mezclar las dos cosas ensucia los ingresos.

---

## Impersonación de solo lectura

El superadmin pide entrar a un gym indicando un motivo. Recibe un **token distinto**, de vida corta (15 minutos), que:

- mantiene su identidad real en `sub` — nunca suplanta a un usuario del gym,
- lleva el `gym_id` del gym visitado,
- incluye `impersonating: true` y una referencia a la sesión de auditoría.

**El mismo filtro que aplica el bloqueo rechaza con `403` cualquier método que no sea `GET`** cuando el token trae `impersonating`. La restricción es de infraestructura, no de buena voluntad de cada endpoint.

Para que la lectura funcione, la política de los endpoints del gym (`RequireStaff`: owner o staff) admite también a un superadmin cuyo token trae `impersonating: true`. El token lleva el rol real, `superadmin`, y sin esa excepción recibiría `403` en todo el gym. La escritura sigue cortada por el filtro, y `RequireOwner` no se abre porque no protege ningún `GET`.

Toda petición con ese token se registra en el log con el id de la sesión, así que queda rastro de qué se miró, no solo de que se entró.

> **Por qué la identidad real no se suplanta:** si el token dijera que es el owner del gym, cualquier dato creado o modificado durante la sesión aparecería firmado por él. Con solo lectura no debería pasar nada, pero un fallo en el filtro dejaría rastros falsos en datos de un cliente. Manteniendo `sub`, un error se ve enseguida.

---

## Panel

Un endpoint, un response, compuesto de los `Public/I{Módulo}Reader` existentes — el mismo patrón que el dashboard de la etapa 8:

**Cartera:** gyms por estado efectivo (trial, activos, en mora, inhabilitados, suspendidos) y los que vencen en los próximos 15 días.

**Ingresos**, dos cifras que responden preguntas distintas:

- **Contratado** — suma de `monthly_price` de los gyms no suspendidos ni inhabilitados. Lo que *deberías* facturar este mes.
- **Cobrado** — suma de `SubscriptionPayment` del mes en curso, con el mes anterior al lado. Lo que *entró*.

La diferencia entre ambas es la cartera pendiente, y es la cifra que de verdad se mira.

**Uso por gym:** miembros activos, check-ins del mes y último movimiento de cada cliente, vía `IMemberReader`, `IAttendanceReader` y `IPaymentReader`. Es lo que delata al gym que dejó de usar el sistema **antes** de que pida la baja.

**Totales de plataforma:** miembros y check-ins agregados de todos los gyms.

> El superadmin no tiene `gym_id`, así que el Global Query Filter ya está desactivado para él y estas agregaciones ven todo sin trucos. Van con proyección explícita (`Select` con `SUM`/`COUNT`), nunca cargando entidades: aquí se recorre la base entera, no un tenant.

**Ojo con el mes:** los ingresos cobrados se agrupan por **día de negocio** (`IClock.BusinessDayRange`), no por rango UTC. Es exactamente el defecto que se corrigió en la etapa 5 y sería fácil repetirlo aquí.

---

## Endpoints

```
GET    /api/admin/gyms                     cartera, con estado efectivo y filtros
GET    /api/admin/gyms/{id}                ficha del cliente: suscripción, pagos y uso
PUT    /api/admin/gyms/{id}/subscription   plan, precio
POST   /api/admin/gyms/{id}/payments       registrar pago → renueva
GET    /api/admin/gyms/{id}/payments       histórico de pagos del gym
POST   /api/admin/gyms/{id}/renew          ajuste de fecha sin pago (cortesías)
POST   /api/admin/gyms/{id}/suspend        manual, con motivo
POST   /api/admin/gyms/{id}/reactivate     saca de suspended
GET    /api/admin/payments?from=&to=       todos los cobros del periodo
GET    /api/admin/dashboard                el panel
POST   /api/admin/impersonate/{gymId}      abre sesión (motivo obligatorio) → token corto
POST   /api/admin/impersonate/end
GET    /api/admin/impersonations           historial de auditoría
```

Todos bajo `[Authorize(Policy = RequireSuperAdmin)]`. `POST /api/gyms` (alta, etapa 2) se mantiene donde está.

---

## Integración con módulos existentes

**Gyms (2)** — **sin migración**. Se reutilizan `gym_status` y `gym_plan` tal como están; el estado efectivo se calcula encima. `GET /api/gym` gana el bloque `subscription`.

**Payments (5)** — se reutiliza el enum `payment_method`. **Nada más se comparte:** los pagos del gym a sus miembros y los del gym al SaaS son dinero de bolsillos distintos y no deben mezclarse en la misma tabla ni en el mismo cierre de caja.

**Identity (1)** — `JwtTokenService` emite el token de impersonación (vida corta, claims extra). El resto no cambia.

**API** — un filtro nuevo que aplica bloqueo por estado y modo de solo lectura. Es la única pieza que afecta a **todos** los endpoints del sistema.

**`Public/ISubscriptionReader`** — para que otros módulos consulten el estado efectivo sin conocer este:

- `GetEffectiveStatusAsync(gymId)`
- `IsWriteAllowedAsync(gymId)`

---

## Criterios de aceptación

1. Un gym con `paid_through` de ayer aparece como `overdue` **sin que nadie haya ejecutado nada**.
2. A los 16 días de vencer pasa a `disabled` solo, por el paso del tiempo.
3. Un gym `disabled` puede hacer `GET` pero recibe `403` en cualquier `POST`, `PUT` o `DELETE`.
4. Un gym `suspended` no puede ni iniciar sesión.
5. `overdue` no bloquea nada, y `GET /api/gym` devuelve `daysUntilDisabled` correcto.
6. **Registrar un pago empuja `paid_through` y devuelve el gym a `active` en la misma operación**, de forma atómica: si falla el pago, la fecha no se mueve.
7. El periodo por defecto de un pago arranca el día siguiente al `paid_through` vigente.
8. Un pago no se puede editar ni borrar; la corrección es un asiento negativo.
9. Los **ingresos cobrados** del mes cuadran con la suma de los pagos, agrupados por día de negocio, y los **contratados** suman solo gyms no suspendidos ni inhabilitados.
10. Un gym solo llega a `suspended` por acción manual; nunca por el paso del tiempo.
11. Con token de impersonación, un `GET` funciona y cualquier escritura devuelve `403`.
12. Toda impersonación queda registrada con quién, dónde, cuándo y por qué; sin motivo, `422`.
13. El token de impersonación caduca a los 15 minutos.
14. Un usuario no superadmin recibe `403` en todo `/api/admin/*`.

---

## Tipo de negocio (T1–T7)

- La cartera y la ficha llevan el tipo; `GET /api/admin/gyms?type=` filtra y `PUT /api/admin/gyms/{id}/type` lo cambia (sin reaplicar la plantilla).
- La ficha muestra si la plantilla quedó aplicada (`GymDetailResponse.Template`: entrada, membresías, rutinas, logros), leído por los readers de cada módulo. «Sin plantilla» es normal en gyms anteriores a las plantillas; en uno nuevo apunta a un handler pendiente de reintento.
- El panel de plataforma suma `AdminDashboardResponse.ByType`: gyms, pagando, en prueba, MRR, miembros activos y avance prueba → pago (`pagando / (pagando + prueba)`) por tipo. La barra de impersonación muestra el tipo del gym.
