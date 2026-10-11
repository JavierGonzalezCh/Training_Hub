# Módulo Memberships — replanteamiento (v2)

> **Estado:** propuesta en revisión (2026-10-07). Nada de esto está construido. Sustituye, cuando
> se apruebe, a la descripción de Memberships de `GSM-especificacion-tecnica.md`. Lo construido hoy
> incluye las sub-etapas 1.2–1.4 de `VERSION-INICIAL-PLAN.md` (cobrar al asignar, cancelar, congelar).

## 1. Propósito

Qué planes vende el gym, qué plan tiene cada miembro, **en qué fechas lo cubre, cuánto se acordó
cobrarle** y, a partir de eso, **en qué situación comercial está cada miembro**: al día, por vencer,
en gracia, vencido, congelado o sin plan.

El dinero recibido sigue en Payments. Memberships guarda **cuánto se acordó**; Payments guarda el
cobro, que por ahora es siempre completo (§6).

## 2. Decisiones tomadas (2026-10-07)

| Tema | Decisión |
|---|---|
| Tipos de plan | **Por tiempo** (en días o en **meses del calendario**) y **tiqueteras** (N entradas con vigencia). |
| Restricciones (horario, días, clases) | **No.** |
| Matrícula | **Sí**, configurable por gym, se cobra en la primera membresía y se puede exonerar. |
| Deuda | **Por ahora no hay saldos pendientes** (decidido 2026-10-07): toda membresía se cobra completa al asignarla. La membresía sí guarda el **precio acordado** (con descuento o matrícula). Abonos y cartera quedan para después (§6). |
| Gracia | **Configurable por gym.** Durante la gracia el miembro entra; pasada la gracia, **se bloquea la entrada** en el kiosco y en la app. **Recepción sí puede dejarlo pasar** (§5.1). |
| Aviso al entrar | Cuando a la membresía le quedan **7 días o menos** (configurable, el mismo número de "Por vencer"), el miembro ve un **letrero** al registrar su entrada. El kiosco solo dice "Bienvenido" y "¿Problemas para ingresar? Comunícate con un asesor" (§5.3). |
| Gracia en tiqueteras | **No hay.** (§5.1) |
| Renovar | **Si paga dentro de la gracia, conserva su ciclo** (vencía el 8 → la nueva va hasta el 8 del mes siguiente). **Si paga después de la gracia, empieza el día en que paga.** |
| Congelar | **Reglas por plan** (máximo de días, mínimo por vez), motivo obligatorio, se puede programar a futuro. El Owner puede saltarse el límite. |
| Estado comercial | Se calcula y se muestra en la lista de miembros, en recepción y en el dashboard. |
| Cambio de plan | Se puede cambiar a mitad de camino, con el valor no usado como crédito. |
| Planes de grupo | Familiares, de pareja y corporativos (§9). |

## 3. Planes — `MembershipPlan`

| Campo | Tipo | Notas |
|---|---|---|
| `Name`, `Description` | texto | Igual que hoy. |
| `Kind` | enum `time`, `pass` | **Nuevo.** `time` = cubre un periodo; `pass` = tiquetera. |
| `Price` | `decimal(12,2)` | Igual que hoy. |
| `DurationUnit` | enum `days`, `months` | **Nuevo**, solo en `time`. Los planes actuales migran como `days`. |
| `DurationValue` | int ≥ 1 | Reemplaza a `DurationDays`. Mensual = 1 mes; trimestral = 3 meses; "15 días" = 15 días. |
| `Entries` | int ≥ 1 | **Nuevo**, solo en `pass`: cuántas entradas trae. |
| `ValidityDays` | int ≥ 1 | **Nuevo**, solo en `pass`: en cuántos días se deben usar. |
| `MaxFreezeDays` | int ≥ 0 | **Nuevo.** Total de días que se puede congelar una membresía de este plan. 0 = no se congela. |
| `MinFreezeDays` | int ≥ 1 | **Nuevo.** Mínimo de días por congelamiento (evita congelar de a un día). |
| `Seats` | int ≥ 1 | **Nuevo.** 1 = individual; más de 1 = plan de grupo (§9). |
| `IsActive` | bool | Igual. **Se añade reactivar** (`POST /api/membership-plans/{id}/reactivate`). |

**Meses del calendario:** un plan de 1 mes que empieza el 8 de septiembre cubre **hasta el 7 de
octubre**; el siguiente ciclo va del 8 de octubre al 7 de noviembre. Si el día no existe en el mes
de destino, se usa el último día del mes: empieza el 31 de enero → cubre hasta el 27 de febrero
(o el 28 en año bisiesto), y el ciclo siguiente arranca el 28 (o el 29). El "día del ciclo" del miembro
se conserva en la membresía (`CycleDay`) para no ir perdiendo días mes a mes.

## 4. Membresía asignada — `Membership`

| Campo | Notas |
|---|---|
| `MemberId`, `PlanId`, `StartDate`, `EndDate` | Igual. `EndDate` se calcula según el tipo de plan. |
| `CycleDay` | **Nuevo**, solo planes en meses: día del mes en que arranca el ciclo (1–31). |
| `EntriesTotal`, `EntriesUsed` | **Nuevo**, solo tiqueteras. `EntriesUsed` se calcula (§7). |
| `AgreedPrice` | **Nuevo.** Precio acordado para **esta** membresía: el del plan, o menos si hubo descuento. Una vez asignada no cambia aunque el plan cambie de precio. |
| `DiscountReason` | **Nuevo.** Obligatorio si `AgreedPrice` < precio del plan. |
| `EnrollmentFee` | **Nuevo.** Matrícula cobrada con esta membresía (0 si no aplica o se exoneró). |
| `EnrollmentWaivedReason` | **Nuevo.** Si se exoneró la matrícula, por qué. |
| `CreditApplied` | **Nuevo.** Crédito que viene de un cambio de plan (§8). |
| `RenewalKind` | **Nuevo**, enum `new`, `early` (antes de vencer), `in_grace`, `after_grace`. Para reportes y para saber cómo se calculó el inicio. |
| `Status` | `active`, `cancelled`. **`paused` deja de guardarse**: se calcula con los congelamientos (§5). `expired` se sigue calculando. |
| `CancelledAt`, `CancellationReason` | Igual. |
| `GroupId` | **Nuevo**, solo planes de grupo (§9). |

**Total a cobrar** = `AgreedPrice` + `EnrollmentFee` − `CreditApplied`.

### 4.1 Asignar y renovar

El inicio lo **calcula el sistema** según la situación del miembro, y el diálogo lo **muestra antes de
confirmar** ("Empieza el 8 oct · vence el 7 nov"). Ya no se ignora en silencio una fecha pedida.

| Situación del miembro | Inicio de la nueva membresía | `RenewalKind` |
|---|---|---|
| Nunca tuvo membresía, o la última fue cancelada | Hoy, o la fecha que elija recepción | `new` |
| Tiene una vigente (renueva antes de vencer) | Día siguiente al fin de la última | `early` |
| Vencida, **dentro de la gracia** | **Día siguiente al fin de la vencida.** Conserva su ciclo: vencía el 7 oct → la nueva va del 8 oct al 7 nov, aunque pague el 10 | `in_grace` |
| Vencida, **pasada la gracia** | **El día en que paga** (hoy) | `after_grace` |

- **Tiqueteras:** siempre empiezan el día en que se compran. Las entradas que sobren de una tiquetera
  vencida **se pierden**.
- El staff puede **cambiar la fecha de inicio caso a caso**, con motivo, y queda registrado.
- **Al asignar, el diálogo propone el plan que el miembro ya tenía**, con su precio.
- **Matrícula:** se suma sola cuando el miembro nunca tuvo membresía en el gym. El staff puede
  exonerarla con motivo. Un miembro que vuelve después de que su ficha se anonimizó es nuevo: paga
  matrícula.

## 5. Gracia y congelamientos

### 5.1 Gracia

- **Pasa de Attendance a Memberships**: `memberships.membership_settings.grace_days` (3 por defecto),
  con el mismo patrón perezoso de hoy (sin fila valen los valores por defecto). La migración copia los
  valores de `attendance.checkin_settings`.
- Durante la gracia, el miembro **entra** (kiosco, app y recepción) con el letrero de §5.3.
- **Pasada la gracia se bloquea la entrada** en el kiosco y en la app. Queda el `CheckinAttempt`
  rechazado, como hoy.
- **Recepción sí puede dejarlo pasar** (decidido 2026-10-07). Al registrarlo a mano, el sistema
  muestra "Membresía vencida el 7 oct, fuera de la gracia" con dos botones: **"Renovar y cobrar"** y
  **"Dejar pasar"**. "Dejar pasar" pide un motivo obligatorio ("paga mañana", "cortesía") y queda
  registrado en la asistencia: quién lo dejó pasar, cuándo y por qué. El Owner ve esas entradas en
  Asistencia con un filtro "entradas autorizadas".
- **Las tiqueteras no tienen gracia** (decidido 2026-10-07): ni por entradas ni por fecha. Sin
  entradas, o pasada su vigencia, no se entra. Al renovarlas empiezan siempre el día en que se
  compran (§4.1). Recepción puede dejar pasar igual, con motivo.

### 5.2 Congelamientos — `MembershipFreeze` (tabla nueva)

| Campo | Notas |
|---|---|
| `MembershipId` | |
| `StartsOn`, `EndsOn` | `EndsOn` nulo = congelamiento abierto, que solo puede empezar hoy y termina al reanudar. |
| `Reason` | Obligatorio (viaje, lesión, incapacidad…). |
| `OverrideLimit` | `true` si el Owner se saltó el límite del plan. |
| `CreatedBy`, `CreatedAt` | |

Reglas:

- **No hay tarea programada**: un día dentro de un congelamiento no está cubierto, y eso se calcula.
- **Programado** (con fechas): al crearlo se corre de inmediato el fin de la membresía (y el de las
  encadenadas) los días que dura. Si se borra antes de empezar, se deshace.
- **Abierto** (desde hoy, sin fin): el fin se corre al reanudar, como hoy.
- Se valida contra el plan: total congelado ≤ `MaxFreezeDays`, y cada congelamiento ≥ `MinFreezeDays`.
  **Solo el Owner** puede superar el límite, con motivo.
- Una membresía congelada no da gracia mientras está congelada.
- Las tiqueteras también se congelan: se corre su vigencia, no sus entradas.

### 5.3 Letrero al entrar

Decidido 2026-10-07: **cuando a la membresía le quedan 7 días o menos, al registrar su entrada el
miembro ve un letrero avisándole.** Es el mismo número de la lista "Por vencer" del dashboard, que hoy
ya usa 7 días: un solo número para todo (`membership_settings.expiring_days`, 7 por defecto,
configurable por gym).

- **Cuándo sale:**
  - Membresía por tiempo: le quedan **7 días o menos**, hasta "vence hoy".
  - Tiquetera: le quedan 7 días o menos de vigencia **o** 2 entradas o menos.
  - **En gracia, siempre** (solo membresías por tiempo; las tiqueteras no tienen gracia).
- **Qué dice:**
  - Por vencer: "**Tu plan vence en 3 días** (el 7 oct). Renueva en recepción para no perder tu fecha."
  - Vence hoy: "**Tu plan vence hoy.**"
  - En gracia: "**Tu plan venció el 7 oct.** Tienes hasta el 10 oct para renovar sin perder tu fecha;
    después no podrás entrar."
  - Tiquetera: "**Te quedan 2 entradas** de 12 (vence el 30 nov)."
- **Dónde sale:**
  - **App del miembro**, en el resultado del escaneo del QR del kiosco: un letrero grande, en color
    de alerta, encima del "Entrada registrada", que el miembro cierra con un toque. No es una
    notificación pequeña que desaparece sola.
  - **Recepción**, cuando el staff lo registra a mano: el mismo texto en la tarjeta del miembro, para
    que se lo diga en persona.
  - **El kiosco no muestra nada del miembro.** Es una pantalla pública: muestra el QR, **"Bienvenido"**
    y **"¿Problemas para ingresar? Comunícate con un asesor"** (decidido 2026-10-07). El nombre del
    gym y su logo siguen saliendo como hoy.
- Sale **en cada entrada** mientras se cumpla la condición, no una sola vez: el objetivo es que
  renueve.
- Lo calcula Memberships: el `AccessStatus` que pide Attendance (§14) trae el texto del aviso y los
  días restantes.

## 6. Cobro

**Por ahora no hay saldos pendientes** (decidido 2026-10-07).

- **Asignar una membresía siempre la cobra completa en el mismo paso**: total = precio acordado +
  matrícula − crédito (§8). El diálogo "Asignar membresía" deja de tener "cobrar ahora" como opción:
  cobrar es parte de asignar. Es la evolución de la sub-etapa 1.2 de la versión inicial.
- Asignar y cobrar son dos llamadas desde la UI (Memberships y Payments), como hoy, porque no hay
  escritura cruzada. Si el cobro falla después de crear la membresía, la UI ofrece reintentar el cobro
  o cancelar la membresía.
- **Excepción:** un total de 0 (cortesía, 100 % de descuento) no genera cobro. Exige motivo de
  descuento.
- La membresía **sí guarda el precio acordado** y el descuento con su motivo. Sirve para saber cuánto
  se cobró y por qué, aunque el plan cambie de precio.

**Para después:** abonos, saldo pendiente y cartera. El modelo ya guarda el precio acordado, así que
el saldo se podría calcular sin migrar nada: precio acordado − pagos ligados.

## 7. Tiqueteras

- **Cada entrada consume una.** Normalmente es una por día; un miembro con entradas múltiples
  habilitadas por el staff gasta una por cada entrada (`GSM-modulo-attendance.md` §3.2).
- **Cómo se descuenta sin escritura cruzada:** Attendance publica `CheckInRegistered`, y Memberships
  guarda la entrada consumida en `memberships.pass_usages` (`membership_id`, `attendance_id`, índice
  único por entrada; una entrada anulada se devuelve con `CheckInVoided`). Si el manejador falla, la entrada no se descuenta: el error favorece al miembro, nunca lo
  perjudica. La asistencia a una clase también registra check-in y también consume.
- Antes de dejar entrar, Attendance pregunta a Memberships si quedan entradas.
- La app y la ficha muestran "Te quedan 4 de 12 entradas · vence el 30 nov".

## 8. Cambio de plan

- **Desde la ficha:** "Cambiar plan" sobre la membresía vigente.
- La vigente se cancela hoy, con el motivo "Cambio de plan".
- **Crédito** = lo pagado de la vigente × días que le quedan ÷ días totales. En tiqueteras: entradas
  que quedan ÷ entradas totales. El staff puede ajustarlo con motivo.
- La nueva empieza **hoy**, con `CreditApplied` = crédito, y se cobra la diferencia.
- **Si el crédito supera el precio del plan nuevo** (pasar a uno más barato), la diferencia se
  **devuelve** al miembro y se registra como una devolución (un pago negativo en Payments, el mismo
  mecanismo de las anulaciones). No hay saldo a favor guardado.
- Los pagos de la vigente **no se anulan**: siguen siendo lo que se cobró. El crédito es un descuento
  sobre la nueva.

## 9. Planes de grupo (familiar, pareja, corporativo)

- Un plan con `Seats` > 1. Al asignarlo se eligen **el titular** (el que paga) y **los beneficiarios**,
  hasta llenar los cupos.
- Se crea una `Membership` por persona, todas con el mismo `GroupId`, las mismas fechas y el mismo
  ciclo. **El precio acordado es del grupo**: lo paga el titular y va en su membresía; las de los
  beneficiarios tienen precio 0.
- Se renuevan, congelan y cancelan **juntas**. Se puede sacar a un beneficiario del grupo (su
  membresía se cancela) y meter otro mientras haya cupo.
- Los planes corporativos de varias decenas de personas se tratan igual. Si más adelante se necesita
  facturar a una empresa, se revisa en Payments.

## 10. Estado comercial del miembro

Lo calcula Memberships para cada miembro, a una fecha:

| Estado | Cuándo |
|---|---|
| `no_plan` | Nunca tuvo membresía, o solo tiene canceladas. Las canceladas no cuentan para el estado (siguen en el historial): cancelada + una vencida anterior = `expired`. |
| `current` (al día) | Tiene una membresía que cubre hoy y le quedan más días que la ventana de aviso. |
| `expiring` (por vencer) | Le quedan 7 días o menos (`expiring_days`, §5.3). En tiqueteras, también si le quedan 2 entradas o menos. El mismo número sirve para el estado, el letrero y la lista "Por vencer" del dashboard. |
| `grace` (en gracia) | Vencida, dentro de la gracia. |
| `expired` (vencido) | Vencida, pasada la gracia. |
| `frozen` (congelado) | Hoy cae dentro de un congelamiento. |
| `scheduled` (programada) | No tiene membresía que cubra hoy, pero sí una que empieza más adelante, y no está en gracia (si está en gracia, manda `grace`). No entra por el escáner; recepción ve "Programada: empieza el {fecha}" y puede usar "Dejar pasar". No cuenta como "sin plan" en listas ni dashboard. Decidido 2026-10-11. |

- `Member.Status` (activo / dado de baja) **no cambia de significado**, pero en pantalla se deja de
  mostrar como "Activo": la lista de miembros muestra el estado comercial, y "Dado de baja" solo cuando
  aplica. Se acaba la confusión de la revisión inicial.
- **Lista de miembros:** filtro por estado comercial. Como el estado depende del día y no hay tarea
  programada, no se guarda en la tabla de miembros: Memberships devuelve los ids de cada estado y
  Members filtra por esos ids. Suficiente para el tamaño de un gym (miles de miembros, no millones).
- **Recepción:** la tarjeta del miembro muestra el estado comercial antes de registrar la entrada.
- **Dashboard:** las tarjetas pasan a contar por estado comercial ("32 al día · 5 por vencer · 2 en
  gracia · 7 vencidos").

## 11. Otras mejoras

- **App del miembro:** historial de sus membresías, entradas de tiquetera y congelamientos.
- **Reactivar planes** desactivados.

## 12. Pendiente de decidir

- Nada por ahora.

## 13. Migración de los datos actuales

- Planes: `Kind = time`, `DurationUnit = days`, `DurationValue = DurationDays`, `MaxFreezeDays = 0`,
  `MinFreezeDays = 1`, `Seats = 1`.
- Membresías: `AgreedPrice` = suma de sus pagos ligados no anulados si los tiene; si no, el precio actual del plan. `EnrollmentFee = 0`,
  `RenewalKind = new`.
- Las congeladas hoy (`paused` + `paused_on`) pasan a un `MembershipFreeze` abierto desde `paused_on`,
  con el motivo "Migrado".
- `grace_days` se copia de `attendance.checkin_settings` a `memberships.membership_settings`.

## 14. Lo que cambia en otros módulos

- **Attendance:** deja de tener la gracia. Para decidir si alguien entra pide a Memberships un
  `AccessStatus` (cubierto / por vencer / en gracia / bloqueado / sin entradas, con los días o
  entradas que quedan y el texto del letrero) en vez de calcularlo él. Guarda en la asistencia si fue
  una **entrada autorizada** por recepción fuera de la gracia, con quién y el motivo. Publica
  `CheckInRegistered` para las tiqueteras.
- **Payments:** registra el cobro completo al asignar y las devoluciones de un cambio de plan.
- **Members:** la lista filtra y muestra el estado comercial.
- **Dashboard:** tarjetas por estado comercial (y corrige el bug B2, `docs/BUGS.md`).
- **Comunicaciones:** los recordatorios de vencimiento usan la gracia de Memberships ("Tu plan venció;
  tienes hasta el 10 para renovar sin perder tu fecha").

## 15. Propuesta de sub-etapas

| Sub-etapa | Qué |
|---|---|
| **MS1** | Estado comercial (§10) en la lista de miembros, la ficha, recepción y el dashboard. Gracia movida a Memberships, regla de renovación (§4.1) con el inicio visible antes de confirmar, **bloqueo pasada la gracia con "Dejar pasar" en recepción** y **letrero al entrar** (§5.3). Plan actual propuesto al asignar. Reactivar planes. |
| **MS2** | Precio acordado, descuento con motivo, matrícula y cobro completo obligatorio al asignar. |
| **MS3** | Planes en meses del calendario. |
| **MS4** | Congelamientos con reglas por plan y programados. |
| **MS5** | Tiqueteras. |
| **MS6** | Cambio de plan con crédito. |
| **MS7** | Planes de grupo. |
| **MS8** | App del miembro: historial, entradas y congelamientos. |
| Después | Abonos, saldo pendiente y cartera (§6). |
