# Módulo Members — replanteamiento (v2)

> **Estado:** propuesta en revisión (2026-10-07). Nada de esto está construido. Sustituye, cuando
> se apruebe, a la descripción de Members de `GSM-especificacion-tecnica.md` (§5 y §6).

## 1. Propósito

Members es **el único dueño de los datos personales del cliente del gym**: identidad, contacto,
emergencia, salud, medidas, relación comercial y autorizaciones legales. Ningún otro módulo guarda
copia de esos datos; los leen por `IMemberReader`.

El miembro puede editar sus propios datos desde la app, y **todo cambio queda registrado** con quién,
cuándo, desde dónde y qué valor tenía antes.

## 2. Decisiones tomadas

| Tema | Decisión |
|---|---|
| Peso y altura | **Historial de mediciones con fecha**, no un valor suelto en la ficha. |
| Edición por el miembro | **Directa, salvo identidad.** Contacto, emergencia, salud, medidas y foto se guardan al instante. Nombres, tipo y número de documento y fecha de nacimiento solo los cambia el staff. |
| Datos de salud | Los ve el **Owner** y el **staff con el permiso `ViewMemberHealth`**. El resto del staff ve solo un aviso ("tiene restricciones"), sin detalle. Requiere permisos por cuenta de staff (§7). |
| Email | **Uno solo, manda Members.** El email de la ficha es el de inicio de sesión. Cambiarlo exige verificar el correo nuevo antes de aplicarlo (§6). |

## 3. Modelo de datos

### 3.1 `Member` (ampliado)

| Grupo | Campo | Tipo | Oblig. | Notas |
|---|---|---|---|---|
| Identificación | `DocumentType` | enum `cc`, `ti`, `ce`, `pa`, `ppt`, `nit`, `other` | Sí | Nuevo. Único por gym: `(gym_id, document_type, document_number)`. |
| | `DocumentNumber` | texto ≤ 30 | Sí | |
| | `FirstName`, `LastName` | texto ≤ 100 | Sí | |
| | `BirthDate` | `DateOnly` | **Sí** (nuevo) | Necesaria para saber si es menor de edad (§3.5). |
| | `Gender` | enum `female`, `male`, `other`, `undisclosed` | No | |
| | `PhotoUrl` | texto | No | |
| Contacto | `Phone` | texto E.164 (`+573001234567`) | No | Se normaliza al guardar; `+57` por defecto. Permite enlace `wa.me`. |
| | `Email` | texto ≤ 256 | No | Ver §6 si tiene cuenta. |
| | `Address`, `City` | texto | No | |
| Emergencia | `EmergencyContactName`, `EmergencyContactRelationship`, `EmergencyContactPhone` | texto, texto, E.164 | No | Sustituye al `EmergencyContact` de texto libre. |
| Salud | `MedicalConditions`, `Medications`, `TrainingRestrictions` | texto ≤ 1000 | No | **Datos sensibles** (Ley 1581). Acceso restringido (§7). |
| | `MedicalClearanceDate` | `DateOnly` | No | Fecha del certificado o apto médico. |
| | `HasHealthAlerts` | derivado | — | `true` si hay condiciones o restricciones. Es lo único de salud que ve el staff sin permiso. |
| Relación | `Source` | enum `walk_in`, `referral`, `social_media`, `google`, `flyer`, `corporate`, `other` | No | Cómo se enteró. |
| | `ReferredByMemberId` | uuid | No | Otro miembro del mismo gym. |
| | `Goal` | enum `weight_loss`, `muscle_gain`, `fitness`, `health`, `sport`, `other` | No | |
| | `Tags` | `text[]` | No | Libres por gym. |
| | `TrainerUserId` | uuid | No | **Entrenador asignado** (nulo = sin entrenador). Cuenta de staff con el permiso `Trainer`, o el Owner; se valida contra Identity. Lo usa Routines (`GSM-modulo-routines.md`, "Entrenador asignado"). |
| Estado | `Status`, `JoinedAt`, `UserId` | | | Sin cambios. `JoinedAt` pasa a ser **editable por staff** (gyms que migran su base). |
| Menor de edad | `Guardian*` | ver §3.5 | Si es menor | |

Solo staff: `Source`, `ReferredByMemberId`, `Tags`, `TrainerUserId`, `JoinedAt`, y las notas internas (§3.3). El miembro **ve** quién es su entrenador, pero no lo cambia.

### 3.2 `MemberMeasurement` (nueva, solo se añade)

| Campo | Tipo | Notas |
|---|---|---|
| `MemberId`, `GymId` | uuid | |
| `MeasuredOn` | `DateOnly` | No puede ser futura. |
| `WeightKg` | `decimal(5,2)` | 20–400. |
| `HeightCm` | `decimal(5,1)` | 50–250. Opcional: si falta, vale la última registrada. |
| `RecordedByUserId`, `RecordedByRole` | | Miembro o staff. |
| `CreatedAt` | instante | |

- El IMC **se calcula** (peso / altura²), no se guarda.
- Una medición mal digitada se **borra**, y el borrado queda en el historial de cambios (§4). No se edita.
- Más adelante se pueden añadir % de grasa, cintura, etc. sin cambiar el modelo.

### 3.3 `MemberNote` (nueva) — notas internas

Notas que el **miembro no ve nunca**: texto, autor, fecha. Solo se añaden. Reemplazan a un campo
"observaciones" único, para que se sepa quién escribió qué.

**Quién las ve:** todo el equipo del gym (Owner y todo el staff, sin permiso especial) y el
SuperAdmin. **El propio miembro, nunca**, ni en la app ni por `/api/me`. Decidido 2026-10-07.

### 3.4 `MemberConsent` (nueva) — autorizaciones

| Campo | Notas |
|---|---|
| `Type` | `data_processing` (autorización de tratamiento de datos, Ley 1581) o `liability_waiver` (exoneración de responsabilidad). |
| `DocumentVersion` | Versión del texto que aceptó (ver `ConsentDocument`). |
| `AcceptedAt`, `Channel` | `app`, `front_desk` (firmado en papel y registrado por staff) o `import`. |
| `AcceptedByUserId` | Quien lo registró. |
| `AcceptedByGuardian` | `true` si lo aceptó el acudiente de un menor. |
| `RevokedAt` | La autorización de datos se puede revocar (derecho del titular). |

`ConsentDocument` (por gym): tipo, versión, texto y fecha de vigencia. El gym los redacta en
Configuración; el sistema trae un texto base para cada uno. **Publicar una versión nueva no anula las
aceptaciones anteriores**, pero la ficha marca "pendiente de aceptar la versión vigente".

**La autorización de datos es obligatoria para guardar datos de salud y medidas.** Sin ella, esas
secciones están bloqueadas.

### 3.5 Menores de edad

Si `BirthDate` da menos de 18 años en `IClock.Today`:

- Son obligatorios `GuardianName`, `GuardianDocumentType`, `GuardianDocumentNumber`,
  `GuardianPhone` y `GuardianRelationship`.
- Las autorizaciones las acepta el acudiente (`AcceptedByGuardian = true`).
- El menor puede tener cuenta en la app, pero **no edita salud ni medidas**. Lo hace el staff o
  el acudiente en recepción.

## 4. Historial de cambios — `MemberChange`

Una fila por **cada campo** que cambia:

| Campo | Notas |
|---|---|
| `MemberId`, `GymId` | |
| `Field` | Nombre estable del campo (`phone`, `medical_conditions`, `measurement`…). |
| `OldValue`, `NewValue` | Texto. En mediciones borradas, el resumen de la fila. |
| `ChangedByUserId`, `ChangedByRole` | `member`, `staff`, `owner`, `superadmin`, `system`. |
| `Source` | `panel`, `app`, `import`, `system`. |
| `ChangedAt` | instante |
| `IsSensitive` | `true` en campos de salud. Esas filas solo las ve quien tenga permiso de salud. |

Reglas:

- **Se escribe en la misma transacción que el cambio.** No hay cambio sin su fila.
- No se registra la creación campo por campo, sino una sola fila `created`.
- Las mediciones añadidas no generan fila (ellas mismas son el historial). Las borradas sí.
- Las aceptaciones y revocaciones de autorizaciones sí generan fila.
- El historial **no se puede editar ni borrar**.

## 5. Operaciones

### Staff (panel)

| Endpoint | Qué hace |
|---|---|
| `GET /api/members` | Lista. Se añaden filtros por etiqueta y "con alertas de salud". Filtro por **estado comercial** (al día, por vencer, en gracia, vencido, congelado, sin plan), que calcula Memberships (`GSM-modulo-memberships.md` §10). |
| `GET /api/members/{id}` | Ficha. Sin permiso de salud, los campos de salud llegan vacíos y solo viene `hasHealthAlerts`. |
| `POST` / `PUT /api/members/{id}` | Crear y editar. Cada `PUT` genera filas de historial. |
| `POST /api/members/{id}/reactivate` | **Nuevo.** Hoy un miembro dado de baja no puede volver. |
| `GET /api/members/{id}/changes` | Historial, paginado. |
| `GET` / `POST /api/members/{id}/measurements`, `DELETE …/{measurementId}` | Mediciones. |
| `GET` / `POST /api/members/{id}/notes` | Notas internas. |
| `POST /api/members/{id}/consents` | Registrar una autorización firmada en papel. |
| `GET` / `PUT /api/gym/consent-documents` | Textos de autorización del gym (solo Owner). |

### Miembro (app)

| Endpoint | Qué hace |
|---|---|
| `GET /api/me/profile` | Su ficha completa, incluida su propia salud. Nunca las notas internas, `Source` ni `Tags`. |
| `PUT /api/me/profile` | Edita contacto, emergencia, salud y foto. Si viene un campo de identidad → 403. |
| `GET` / `POST /api/me/measurements` | Sus mediciones y su evolución. |
| `POST /api/me/consents` | Acepta la versión vigente de una autorización. |
| `POST /api/me/email-change` + `POST /api/me/email-change/confirm` | Cambio de email con verificación (§6). |
| `POST /api/me/password` | Cambiar contraseña con la sesión iniciada. (Identity, no Members.) |

## 6. Email único

- El email de la ficha **es** el usuario de la app. El registro por invitación deja de pedir email
  si la ficha ya lo tiene; si no lo tiene, lo pide y lo escribe en la ficha.
- **Cambiarlo** (por el miembro o por el staff) cuando el miembro ya tiene cuenta: se envía un código al
  correo nuevo y, al confirmarlo, se actualizan **en la misma transacción** la ficha y la cuenta de
  Identity. Hasta entonces sigue valiendo el anterior.
- La escritura en Identity va por `Identity.Public.IUserProvisioning`, que ya es una de las tres
  excepciones de escritura cruzada (se le añade `ChangeEmailAsync`). **No hace falta una cuarta.**
- Mientras Resend no esté configurado, el código va al log, igual que hoy.

## 7. Permisos de staff

Identity no tiene hoy permisos más finos que el rol. Se añade una lista de permisos por cuenta de
staff, empezando con dos:

- `ViewMemberHealth` — ver y editar salud, y ver las filas sensibles del historial.
- `Trainer` — la cuenta puede ser **entrenador asignado** de miembros. El Owner también puede serlo.
- `ManageAttendance` — anular entradas registradas por error (`GSM-modulo-attendance.md` §5).
- `VoidPayments` — anular pagos y aprobar solicitudes de anulación (`GSM-modulo-payments.md` §6).
- `ViewFinances` — ver ingresos, el cierre diario y los turnos de caja de otros (`GSM-modulo-payments.md` §7).

Si una cuenta con miembros asignados se desactiva o pierde `Trainer`, esos miembros quedan sin
entrenador y el Owner ve la lista para reasignarlos.

El Owner tiene todos los permisos siempre. El permiso viaja en el JWT (claim `perm`), así que un
cambio aplica en el siguiente refresh del token. Se gestiona desde la pestaña Staff de Configuración.

> Es una pieza de Identity, no de Members. Se construye antes que la sección de salud.

## 8. Lo que cambia en otros módulos

- **Identity:** permisos de staff (§7), `ChangeEmailAsync`, cambio de contraseña con sesión.
- **Recepción y kiosco:** el resultado del check-in muestra "tiene alertas de salud" y "falta la
  exoneración" (lectura por `IMemberReader`, sin detalle).
- **App del miembro:** pantalla "Mi perfil" (`/app/perfil`) con sus secciones, mediciones con gráfica
  y aceptación de autorizaciones pendientes.
- **Comunicaciones:** el teléfono en E.164 habilita enlaces de WhatsApp (se decide en su revisión).

## 9. Migración de los datos actuales

- `document_type` se rellena con `cc` (todos los miembros actuales son colombianos de prueba).
- `birth_date` pasa a ser obligatoria **solo para fichas nuevas**. Las existentes sin fecha quedan
  marcadas "ficha incompleta".
- `emergency_contact` (texto libre) **no se puede separar** en nombre y teléfono de forma fiable: se
  copia como nota interna "Contacto de emergencia (dato anterior)" y los campos nuevos quedan vacíos.
- Los teléfonos se normalizan a E.164 con `+57`. Los que no se puedan interpretar se dejan tal cual
  y la ficha se marca "ficha incompleta".
- Ningún miembro actual tiene autorización registrada: todos quedan "pendiente de autorizar".

## 10. Consultas de datos de salud

Se registra **quién vio** los datos de salud, no solo quién los cambió. Las notas internas no se
registran.

- Tabla `members.health_access_log`: `MemberId`, `GymId`, `UserId`, `Role`, `AccessedAt`.
- Se escribe **solo cuando la respuesta trae el detalle de salud**, es decir, cuando quien consulta
  tiene permiso. Al staff sin permiso no se le registra nada, porque no vio nada.
- No se registra cuando el propio miembro consulta su ficha.
- Como mucho una fila por usuario, miembro y día, para no llenar la tabla cada vez que se abre la ficha.
- El Owner lo ve en la pestaña "Historial" de la ficha, junto a los cambios.
- **Las consultas del SuperAdmin no aparecen aquí**: van a la auditoría de la plataforma
  (`GSM-modulo-superadmin.md` §V6).

Por qué: la Ley 1581 no exige este registro de forma expresa. Pero los datos de salud son sensibles
y el gym tiene que poder demostrar que los protegió (principio de responsabilidad demostrada). Ante
un reclamo, "estas tres personas lo vieron" es esa prueba. Cuesta poco, porque solo aplica a una
sección y a quien tenga permiso.

## 11. Baja y anonimización

**Dar de baja** sigue siendo cambiar el estado a `Inactive`; no se borra nada. Se mantiene
`POST /api/members/{id}/reactivate`.

**Pasado un plazo desde la baja, los datos personales se anonimizan:**

| Qué | Cómo queda |
|---|---|
| Nombres y apellidos | `Miembro anonimizado` + 4 caracteres del id |
| Documento | Un valor único sin significado (`ANON-{id}`), para no chocar con el índice único |
| Contacto, emergencia, dirección, salud, acudiente, foto | Vacíos. La foto se borra de R2. |
| Fecha de nacimiento | Se queda solo el año (estadística por edades) |
| Género, `Source`, `Goal`, `JoinedAt` | Se conservan: no identifican a nadie y sirven para reportes |
| Mediciones | Se conservan sin vínculo a la persona (no se pueden atribuir a nadie) |
| Notas internas | Se borran |
| Historial de cambios | Se vacían `OldValue` y `NewValue`; se conserva qué campo cambió y cuándo |
| Registro de consultas de salud | Se borra |
| Cuenta de la app | Se elimina en Identity |
| Rutinas y registros de entrenamiento | **Se conservan**; solo se vacían sus notas, que son texto libre y pueden traer datos de salud. Lo hace Routines al recibir `MemberAnonymized`. |
| Entrenador asignado | Se vacía |
| Pagos, asistencias, membresías | **No se tocan**: apuntan a un `member_id` que ya no identifica a nadie. Los pagos son la contabilidad del gym. |
| Autorizaciones | Se conservan la fecha, el tipo y la versión, como prueba de que existió la autorización |

Regla general (decidido 2026-10-07): **los datos de actividad se conservan y solo se vacían los
datos personales y sensibles.** Cada módulo que guarde texto libre del miembro (notas) lo limpia en su
propio manejador del evento `MemberAnonymized`, que publica Members al terminar. Sin escrituras
cruzadas.

Reglas:

- **Es irreversible.** Un miembro anonimizado no se puede reactivar. Si vuelve, es una ficha nueva.
- Lo ejecuta la tarea periódica que ya existe (`/internal/jobs/…`), sin scheduler propio. Si una
  ejecución se pierde, la siguiente se pone al día.
- **Si el miembro vuelve antes del plazo**, se reactiva y el plazo deja de correr.
- El miembro también puede **pedir la supresión** (derecho de la Ley 1581). En ese caso, el Owner
  anonimiza la ficha de inmediato desde el panel, con confirmación.
- Antes de anonimizar, el sistema avisa al Owner en el panel con unos días de anticipación.

**Plazo:** configurable por gym, **24 meses por defecto** desde la baja (decidido 2026-10-07).

Sigue sin decidir qué más pasa al dar de baja (cancelar la membresía vigente, las reservas,
bloquear la cuenta de la app). Hoy el evento `MemberDeactivated` no lo escucha nadie. Se decide al
revisar Memberships y Classes.

## 12. Propuesta de sub-etapas

Igual que el resto del proyecto: una por vez, con confirmación entre una y otra.

| Sub-etapa | Qué |
|---|---|
| **M1** | Ficha ampliada (identificación, contacto, emergencia, relación, menores) + historial de cambios + reactivar. Solo staff. |
| **M1b** | Entrenador asignado: permiso `Trainer` en Identity (con la lista de permisos por cuenta de §7) y `TrainerUserId` en la ficha. |
| **M2** | Autorizaciones: textos por gym, aceptación en papel y en la app, bloqueo de salud y medidas sin autorización de datos. |
| **M3** | Sección de salud + permiso `ViewMemberHealth` (la lista de permisos por cuenta ya existe desde M1b). |
| **M4** | Mediciones de peso y altura con historial. |
| **M5** | "Mi perfil" del miembro: `GET`/`PUT /api/me/profile`, mediciones propias, email único con verificación, cambio de contraseña. |
| **M6** | Anonimización: plazo por gym, aviso previo al Owner, ejecución en la tarea periódica, supresión a pedido. |

El registro de consultas de salud (§10) entra en **M3**, junto con la sección de salud.

## 13. Pendiente de decidir

- **Qué más pasa al dar de baja**: se decide al revisar Memberships y Classes.

## 14. En evaluación (fuera de este módulo por ahora)

- **Importación desde CSV/Excel.** Aplazada. **Reports** queda definido como el módulo de
  descargas (Excel y similares); la importación se evaluará junto con él.
