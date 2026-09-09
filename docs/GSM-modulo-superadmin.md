# Módulo SuperAdmin — Especificación

> Fase 3. Complementa `CLAUDE.md` y `docs/GSM-especificacion-tecnica.md`; **si algo aquí contradice a esos dos, ellos mandan.** Sigue las convenciones transversales de `docs/BACKEND-MVP-PLAN.md`.

**Schema:** `superadmin` · **Toca también:** `identity` (impersonación) y la API (bloqueo por estado)

Este es el módulo que gestiona los gimnasios **como clientes del SaaS**. Es el único que opera por encima del multi-tenancy en lugar de dentro de él.

---

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
si Gym.Status == trial            → trial          (hasta trial_ends_on)
si hoy <= paid_through            → active
si hoy <= paid_through + 15 días  → overdue
en otro caso                      → disabled
```

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

**Uso por gym:** socios activos, check-ins del mes y último movimiento de cada cliente, vía `IMemberReader`, `IAttendanceReader` y `IPaymentReader`. Es lo que delata al gym que dejó de usar el sistema **antes** de que pida la baja.

**Totales de plataforma:** socios y check-ins agregados de todos los gyms.

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

**Payments (5)** — se reutiliza el enum `payment_method`. **Nada más se comparte:** los pagos del gym a sus socios y los del gym al SaaS son dinero de bolsillos distintos y no deben mezclarse en la misma tabla ni en el mismo cierre de caja.

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

## Decisiones abiertas

1. **Los 15 días de gracia** son constante. Si algún cliente necesita otro plazo, pasa a ser un campo de la suscripción.
2. **Aviso automático de vencimiento por correo.** Resend está integrado y el dato está; falta decidir cuándo se dispara — y hoy no hay proceso programado que lo haga.
3. **Facturación electrónica** (numeración, DIAN) no entra. El registro de pagos es control interno, no un documento fiscal.
4. **Métricas SaaS clásicas** (MRR histórico, churn) necesitan guardar la foto de cada mes; con solo el estado actual no se reconstruyen hacia atrás. Si las quieres, hay que empezar a registrarlas pronto: el histórico que no se guarda no se recupera.
5. **Notificar al gym su propio estado** por un canal que no sea el panel (correo al owner al pasar a mora) no entra.
