# Módulo Payments — replanteamiento (v2)

> **Estado:** propuesta en revisión (2026-10-07). Nada de esto está construido. Lo construido hoy está
> en `GSM-especificacion-tecnica.md` (Payments) y en la sub-etapa 1.1 de `VERSION-INICIAL-PLAN.md`
> (anular un pago).

## 1. Propósito

**Registrar** lo que el gym cobra, **cuadrar la caja** de cada turno y dar al Owner la información
para conciliar. **El sistema no recibe pagos**: no hay pasarela, ni pagos en línea, ni facturación
electrónica. Solo registra cobros que pasaron por fuera (efectivo, datáfono, Nequi…).

## 2. Decisiones tomadas (2026-10-07)

| Tema | Decisión |
|---|---|
| Anular pagos | Solo el **Owner** o un staff con el permiso **`VoidPayments`**. El resto puede **solicitar** la anulación (§6). |
| Ver ingresos | Solo el **Owner** o un staff con el permiso **`ViewFinances`**. El resto del staff ve **lo que cobró en su turno** (§7). |
| Caja | **Por turno, con arqueo**: base inicial, conteo del efectivo al cerrar y diferencia (§4). |
| Métodos | **Todos los métodos de Colombia** (§3). Se registra solo el método: **sin referencia ni comprobante**. |
| Efectivo vs. digital | **El efectivo va a la caja del turno.** Los demás métodos los **concilia el Owner a mano** contra sus extractos (§5). |
| Lo que no entra | Recibos al miembro, productos con inventario, pagos en línea y facturación electrónica. **Solo se registra; no se reciben pagos.** |

Heredadas de Memberships (`GSM-modulo-memberships.md` §6, §8): asignar una membresía la cobra completa
en el mismo paso; no hay saldos pendientes; un cambio a un plan más barato genera una **devolución**
(pago negativo).

## 3. Métodos de pago

El enum `payment_method` pasa de 4 a 8 valores:

| Valor | Etiqueta | ¿Va a la caja? |
|---|---|---|
| `cash` | Efectivo | **Sí** |
| `card` | Tarjeta (débito o crédito, datáfono) | No |
| `nequi` | Nequi | No |
| `daviplata` | Daviplata | No |
| `breb` | Bre-B (llaves) | No |
| `bank_transfer` | Transferencia bancaria | No |
| `bank_deposit` | Consignación | No |
| `other` | Otro | No |

- Migración: `transfer` → `bank_transfer`; `cash`, `card` y `other` no cambian.
- **El enum lo comparten los pagos del SaaS** (SuperAdmin). Allá también aparecen los métodos nuevos;
  no hace daño.
- Un enum de varias palabras va con `SnakeCaseNameTranslator` (nota de Classes en `CLAUDE.md`).

## 4. Caja por turno — `CashShift` (nueva)

| Campo | Notas |
|---|---|
| `GymId` | |
| `OpenedBy`, `OpenedAt`, `OpeningCash` | Quién abre, cuándo y con cuánta base en efectivo. |
| `ClosedBy`, `ClosedAt` | |
| `CountedCash` | Efectivo contado al cerrar. |
| `ExpectedCash` | Se calcula al cerrar y **se guarda**, para que el cierre no cambie después. |
| `Difference` | Contado − esperado. |
| `CloseNotes` | **Obligatorias si la diferencia no es 0.** |

**Efectivo esperado** = base + cobros en efectivo del turno − devoluciones y anulaciones en efectivo
que salieron de la caja + ingresos manuales − retiros (§4.2).

### 4.1 Reglas

- **Una caja abierta por gym a la vez.** El cambio de turno es: el que sale cierra (cuenta y cuadra),
  el que entra abre. La base del que entra se precarga con lo que quedó contado.
- **Cobrar en efectivo exige una caja abierta.** Sin caja: "Abre la caja para cobrar en efectivo".
  Los cobros con otros métodos se pueden registrar sin caja abierta.
- **Todo pago queda ligado al turno abierto** en ese momento (`Payment.ShiftId`), sea en efectivo o
  no. Así se sabe qué cobró cada turno.
- **Caja olvidada:** si quien abrió no cerró, el siguiente que intente cobrar en efectivo ve "La caja
  de Ana sigue abierta desde ayer 6:00" y debe cerrarla con su conteo antes de abrir la suya; o la
  cierra el Owner. Una caja abierta hace más de 16 horas sale resaltada para el Owner.
- **Un turno cerrado no se reabre ni se edita.** Si después aparece un error, se corrige con una
  anulación o un movimiento en el turno actual.

### 4.2 Retiros e ingresos de efectivo — `CashMovement` (nueva)

- Sacar plata de la caja a mitad de turno (llevarla al banco, pagar un domicilio) o meter cambio.
- Tipo (`withdrawal` / `deposit`), monto, motivo obligatorio, quién y cuándo. Ligado al turno.
- **No son ingresos ni gastos del gym**, solo movimientos de la caja: no aparecen en los ingresos.
- Los hace quien tiene la caja abierta o el Owner.

### 4.3 Anulaciones y devoluciones en efectivo

- Anular un pago en efectivo **del turno actual**: el asiento inverso entra en la caja del turno (se
  asume que el dinero se devuelve o nunca entró).
- Anular un pago en efectivo **de un turno ya cerrado**: el sistema pregunta **"¿Se devuelve el dinero
  ahora?"**.
  - Sí → el asiento inverso sale de la caja abierta.
  - No (fue un error de registro y la plata nunca estuvo) → el asiento inverso queda marcado
    `AffectsCashDrawer = false` y no toca la caja actual. Se ve en el reporte del Owner.
- Las devoluciones por cambio de plan siguen la misma regla.

### 4.4 Pantallas

- **Recepción / Pagos:** barra con el estado de la caja ("Caja abierta por Ana desde las 6:00 ·
  efectivo esperado $340.000") y los botones "Abrir caja", "Retiro/ingreso" y "Cerrar caja".
- **Cerrar caja:** se escribe el efectivo contado, se ven el esperado y la diferencia, y si no cuadra
  se exige una nota. Al confirmar sale el **reporte del turno**, que se puede imprimir: base, cobros
  por método, movimientos, anulaciones, esperado, contado y diferencia.
- **Owner:** lista de turnos con sus diferencias, filtrable por persona y fecha. Una persona con
  diferencias repetidas salta a la vista.

## 5. Conciliación de pagos digitales

- **El sistema no verifica pagos digitales.** El staff registra "Nequi · $120.000" y el Owner compara
  por fuera con su extracto o su app del banco.
- Para facilitarlo, el **cierre diario** (§7) separa los totales por método: "Nequi $480.000 (4
  pagos)", con la lista de cada uno (miembro, hora, quién cobró).
- No hay marca de "conciliado" en esta versión.

## 6. Anular un pago

- **Quién:** el Owner o un staff con el permiso **`VoidPayments`**. Se suma a la lista de permisos por
  cuenta de Identity (`GSM-modulo-members.md` §7).
- Igual que hoy: asiento inverso fechado hoy, motivo obligatorio, una sola vez, una anulación no se
  anula.
- **El staff sin permiso puede solicitar la anulación**, con motivo. La solicitud llega como aviso a
  los Owners (campana del panel; push cuando exista Notifications) y la aprueba o rechaza un Owner o
  alguien con `VoidPayments`. Mientras está pendiente, el pago se ve "anulación solicitada".
- Tabla nueva `payments.void_requests`: pago, motivo, quién y cuándo pidió, estado (`pending`,
  `approved`, `rejected`), quién y cuándo resolvió.

## 7. Quién ve qué

| Qué | Owner / `ViewFinances` | Resto del staff |
|---|---|---|
| Registrar cobros | Sí | Sí |
| Cobros de su propio turno | Sí | **Sí** |
| Pagos de un miembro (pestaña Pagos de la ficha) | Sí | **Sí**: lo necesita para atender ("¿ya pagó?") |
| Lista completa de pagos del gym | Sí | No |
| Cierre diario y totales por método | Sí | No |
| Turnos de otras personas y sus diferencias | Sí | No |
| Tarjetas de ingresos del dashboard | Sí | No (no se muestran) |

## 8. Cierre diario

La pantalla `/cash-register` actual pasa a ser el **cierre diario del Owner**: los turnos del día con
sus diferencias, los totales por método (para conciliar), las anulaciones con su motivo y los
movimientos de caja. Agrupa por día de negocio con `IClock.BusinessDayRange`.

Imprimir sí; exportar a Excel va en Reports, que es el módulo de descargas.

## 9. Lo que no cambia

- Pagos sin miembro (ventas de mostrador) con concepto en texto libre.
- Los pagos son inmutables: toda corrección es otro asiento.
- `PaidAt` siempre es "ahora": no se registran pagos con otra fecha.

## 10. Bugs relacionados

- **B2** (`docs/BUGS.md`): el comparativo de ingresos del dashboard compara contra el mes anterior
  completo. Se corrige en la sub-etapa P3.
- **B3**: formato del precio distinto según la pantalla.

## 11. Propuesta de sub-etapas

| Sub-etapa | Qué |
|---|---|
| **P1** | Métodos de pago de Colombia (migración del enum). |
| **P2** | Permisos `VoidPayments` y `ViewFinances`; vistas según permiso; solicitudes de anulación. |
| **P3** | Caja por turno: abrir, cerrar con arqueo, movimientos, anulaciones en efectivo, reporte imprimible. Cierre diario del Owner. Corrige B2. |

## 12. Para después

- Recibo al miembro (correo, WhatsApp, impreso).
- Productos de mostrador con precio fijo e inventario.
- Pagos en línea desde la app.
- Facturación electrónica DIAN (depende de la obligación tributaria de cada gym).
- Marca de "conciliado" en los pagos digitales; número de referencia y comprobante.
- Varias cajas abiertas a la vez (gyms con más de un punto de cobro).
- Abonos y saldo pendiente (`GSM-modulo-memberships.md` §6).
