# Módulo Identity — replanteamiento (v2)

> **Estado:** construido en la v2 (rama `v2`, verificado con tests el 2026-10-20). Describe el diseño vigente; lo que quedó sin hacer o sin verificar está en `V2-PENDIENTES.md`. Las frases «hoy…» que contrastan con lo anterior describen la v1, previa a esta versión.

## 1. Propósito

**Quién es cada persona en el sistema, en qué gyms está, con qué rol y qué puede hacer**: cuentas,
inicio de sesión, permisos, invitaciones y dispositivos (kiosco).

Identity guarda **la cuenta** (email, contraseña, nombre). **Los datos personales del miembro son de
Members**, uno por gym (`GSM-modulo-members.md`).

## 2. Decisiones tomadas (2026-10-07)

| Tema | Decisión |
|---|---|
| Cuentas | **Una cuenta por persona, con acceso a varios gyms** y un rol distinto en cada uno (miembro en uno, entrenador en otro). Al entrar elige el gym si tiene más de uno (§3). |
| Permisos | **Perfiles predefinidos** (Recepción, Entrenador, Administrador) **más ajuste por persona** (§5). |
| Seguridad | **Bloqueo de la cuenta tras 5 contraseñas incorrectas** (§7). Doble factor, lista de sesiones y contraseñas filtradas quedan fuera por ahora. |
| Login del miembro | **Propio, en `/app`, con la marca del gym.** Entra con **contraseña o con un código de 6 dígitos** que llega al correo (§6). |

### Heredadas de otras revisiones

| Viene de | Qué |
|---|---|
| Members | Cambio de email con verificación; cambio de contraseña con la sesión iniciada; permiso `ViewMemberHealth`. |
| Routines | Permiso `Trainer`. |
| Attendance | Permiso `ManageAttendance`; cuenta de dispositivo para el kiosco (§8). |
| Payments | Permisos `VoidPayments` y `ViewFinances`. |
| Gyms | Owners sin contraseña con enlace de activación; varios Owners; traspaso; ascender staff a Owner; contacto de facturación. |
| Versión inicial §2 y §3 | Enlaces en vez de códigos para pegar; login propio del miembro. |

## 3. Una cuenta, varios gyms

### 3.1 Modelo

- **`AppUser`** (la persona): email (único en todo el sistema, como hoy), contraseña, nombre,
  `IsActive`, `IsSuperAdmin`. **Ya no lleva `GymId` ni `Role`.**
- **`GymAccess`** (nueva, una fila por persona y gym): `UserId`, `GymId`, `Role` (`owner`, `staff`,
  `member`), `ProfileId` (solo staff, §5), `IsActive`, `IsBillingContact` (solo owner),
  `CreatedAt`, `DeactivatedAt`.
  - Una persona tiene **como máximo dos accesos por gym: uno de equipo** (`owner` o `staff`) **y uno de
    `member`**. Quien es staff y también entrena en su gym tiene los dos y cambia de modo
    (`GSM-apps-y-vistas.md` §4; decidido 2026-10-07, reemplaza a "un acceso por gym"). Índice único
    `(UserId, GymId, es_equipo)`.
  - **Desactivar un acceso** (el staff que se va del gym A) **no toca los otros gyms** de la persona.
    Desactivar la cuenta completa solo lo hace el SuperAdmin.
- El SuperAdmin no tiene `GymAccess`: es global.

### 3.2 Inicio de sesión

1. La persona entra con su email y su contraseña (o código, si es miembro).
2. **Si tiene un solo gym**, entra directo.
3. **Si tiene varios**, ve "¿A qué gym quieres entrar?", con logo y rol de cada uno. El último usado
   queda primero.
4. El token se emite **para un gym**: lleva `gym_id`, el rol en ese gym y sus permisos. Así el filtro
   de multi-tenancy (Global Query Filter) sigue funcionando igual que hoy.
5. **"Cambiar de gym"** en el menú emite un token nuevo para otro gym sin volver a pedir contraseña.
   **"Pasar a modo miembro / modo staff"** hace lo mismo con el otro acceso del mismo gym. El selector
   muestra cada acceso por separado ("Gimnasio Demo · Staff", "Gimnasio Demo · Miembro").

### 3.3 Invitación de un miembro que ya tiene cuenta

- El miembro abre `/app/registro?code=…` y pone su documento, como hoy.
- **Si su email ya tiene cuenta** (por ejemplo, es miembro de otro gym): "Ya tienes una cuenta.
  Entra con tu contraseña o con un código para agregar este gym". Al entrar, se crea su `GymAccess` en
  este gym y se vincula su ficha de Members.
- **Si no tiene cuenta:** crea su contraseña. **El email ya no se pide**: se usa el de su ficha
  (decisión de Members). Si la ficha no tiene email, lo pide.
- **Al terminar queda con la sesión iniciada.**
- Lo mismo aplica al staff y a los Owners invitados: si el email ya tiene cuenta, se agrega el acceso.

### 3.4 Email y nombre de la cuenta

- El email es uno por persona. **Cambiarlo** exige confirmar el correo nuevo con un código.
- Al confirmarse, Identity publica `UserEmailChanged` y Members actualiza **todas** las fichas
  vinculadas a esa persona, en todos sus gyms. Así se cumple "un solo email, manda Members" sin que
  dos gyms tengan emails distintos para la misma persona.
- El nombre de la cuenta es el que se ve en el panel (quién cobró, quién anuló). El nombre de la ficha
  del miembro lo maneja Members.

### 3.5 Migración

Cada `AppUser` actual genera un `GymAccess` con su `GymId` y su `Role` de hoy. Como el email ya era
único, no hay cuentas que fusionar. El staff actual queda con el perfil **Administrador**, que es lo
más parecido a lo que puede hacer hoy (todo), y el Owner lo ajusta después.

## 4. Invitaciones y enlaces

**Todos los accesos nuevos llegan por enlace, nunca con contraseña escrita por otro ni con un código
para pegar a mano.**

| Para quién | Enlace | Vence |
|---|---|---|
| Owner (alta del gym, otro Owner) | `/activar?email=…&code=…` | 72 h |
| Staff | `/activar?email=…&code=…` | 72 h |
| Miembro | `/app/registro?code=…` | 7 días (como hoy) |
| Restablecer contraseña | `/restablecer?email=…&code=…` | 24 h |

- El enlace abre el formulario con todo precargado: solo falta la contraseña.
- Si la persona ya tiene cuenta, el enlace pide entrar en vez de crear contraseña (§3.3).
- **Copiar enlace** está disponible para el Owner (staff y miembros) y para el SuperAdmin (Owners),
  para mandarlo por WhatsApp. Queda registrado quién lo copió.
- El enlace del miembro se puede compartir por WhatsApp desde la ficha, con un mensaje listo:
  "Hola Ana, activa tu cuenta del Gimnasio Demo: …".
- **Sin Resend configurado, los correos no llegan**: depende del despliegue.

## 5. Perfiles y permisos de staff

### 5.1 Permisos

| Permiso | Qué permite | Origen |
|---|---|---|
| `ViewMemberHealth` | Ver y editar datos de salud | Members |
| `Trainer` | Ser entrenador asignado de miembros | Routines |
| `ManageAttendance` | Anular entradas | Attendance |
| `VoidPayments` | Anular pagos y aprobar solicitudes de anulación | Payments |
| `ViewFinances` | Ver ingresos, cierre diario y cajas de otros | Payments |

El **Owner tiene todos** siempre. Todo lo demás que hoy hace el staff (registrar miembros, cobrar,
registrar entradas, armar rutinas, manejar clases) **sigue abierto a cualquier staff**.

### 5.2 Perfiles

| Perfil | Permisos que trae |
|---|---|
| **Recepción** | Ninguno de los especiales. |
| **Entrenador** | `Trainer`, `ViewMemberHealth`. |
| **Administrador** | `ViewMemberHealth`, `ManageAttendance`, `VoidPayments`, `ViewFinances`. |

- Al invitar a alguien del staff se elige el perfil.
- El Owner puede **agregar o quitar permisos sueltos** a una persona sin cambiar su perfil
  ("Entrenador + `ManageAttendance`"). En la pantalla se ve "Entrenador (ajustado)".
- Los perfiles son fijos del sistema en esta versión; cada gym no crea los suyos.
- Tabla `identity.gym_access_permissions` (`GymAccessId`, `Permission`, `Granted` sí/no) guarda solo
  los ajustes; los permisos efectivos = los del perfil + agregados − quitados.

### 5.3 Cuándo aplica un cambio de permisos

- Los permisos viajan en el token (claim `perm`).
- **Agregar** un permiso aplica en la próxima renovación del token (como mucho 15 minutos: el access token dura 15 min, decidido 2026-10-11).
- **Quitar** un permiso **cierra las sesiones de esa persona en ese gym**: tiene que volver a entrar.
  Un permiso sensible (finanzas, salud) no puede seguir activo una hora después de quitarlo.
- Todo cambio de perfil o de permisos queda registrado: quién, a quién, qué y cuándo.

## 6. Login del miembro

- **Pantalla propia** en `/app/entrar`, con el logo y los colores del gym. El gym se sabe por el enlace
  de invitación, por `/app/entrar?gym=slug`, o porque el teléfono recuerda el último. Sin gym conocido,
  sale la marca de GSM.
- **Dos formas de entrar:**
  1. **Email y contraseña.**
  2. **"Enviarme un código":** llega un código de 6 dígitos al correo. Vence en 10 minutos, sirve una
     sola vez y admite 5 intentos. Se puede pedir uno por minuto y 5 por hora.
- **"Olvidé mi contraseña"** manda el enlace de §4. Con el código, en la práctica casi no hace falta.
- **La app nativa y la PWA mantienen la sesión** mientras se usen: cada renovación extiende el token
  de renovación otros 30 días. Un miembro que abre la app cada semana no vuelve a escribir nada.
- El panel (`/login`) queda para Owner, staff y SuperAdmin. Si un miembro entra por ahí, se le lleva a
  `/app`.
- **El código por WhatsApp** queda para después (exige un proveedor de WhatsApp Business).

## 7. Bloqueo por intentos

- **5 contraseñas incorrectas seguidas → la cuenta se bloquea 15 minutos.** Usa el lockout que ya trae
  ASP.NET Identity (`MaxFailedAccessAttempts = 5`, `DefaultLockoutTimeSpan = 15 min`).
- Se avisa por correo al dueño de la cuenta: "Hubo 5 intentos fallidos de entrar a tu cuenta. Si no
  fuiste tú, cambia tu contraseña".
- **Durante el bloqueo, el código por correo sí funciona** para los miembros: quien tiene acceso a su
  correo no queda afuera.
- El mensaje de error no dice si el email existe ("Email o contraseña incorrectos"), como hoy.
- Se mantiene el límite actual por IP (10 intentos por minuto).

## 8. Cuenta de dispositivo para el kiosco

Diseñada en `GSM-modulo-attendance.md` §6. En Identity:

- **No es una persona:** tabla `identity.kiosk_devices` (`GymId`, `Name`, `PairedAt`, `PairedBy`,
  `RevokedAt`, hash del token de renovación). No tiene email ni contraseña.
- **Emparejamiento:** el Owner genera un código de 8 caracteres (vence en 10 minutos); el equipo lo
  digita en `/kiosk` y recibe una sesión con rol `kiosk` para ese gym.
- El token del kiosco solo sirve para los endpoints del kiosco (mostrar el QR y registrar entradas por
  documento).
- El Owner ve y revoca sus kioscos en Configuración → Entrada al gym → Kioscos (`/settings/kioscos`).

## 9. Perfil propio

Para todos los roles, en `/perfil` (panel) y `/app/perfil` (miembro):

- **Cambiar la contraseña** con la sesión iniciada (pide la actual).
- **Cambiar el email** con verificación (§3.4).
- Cambiar el nombre de la cuenta (staff y Owner; el miembro cambia sus datos en su ficha, Members).
- Ver sus gyms y su rol en cada uno.

## 10. Lo que cambia en otros módulos

- **Todos:** el rol y el `gym_id` salen del token igual que hoy; lo que cambia es de dónde los saca
  Identity (de `GymAccess`, no del usuario).
- **Members:** `Member.UserId` sigue igual, pero una misma persona puede estar vinculada a fichas en
  varios gyms. Consume `UserEmailChanged`.
- **Gyms:** los Owners se leen de `GymAccess` (`Gym.OwnerId` desaparece, `GSM-modulo-gyms.md` §6).
- **SuperAdmin:** la ficha del gym lista sus Owners con su estado de activación.
- **Communications, Equipment, Payments:** los avisos "al Owner" van a todos los accesos `owner` del
  gym.

## 11. Decisiones finales (2026-10-07)

- **Las tareas básicas siguen abiertas a todo el staff** (cobrar, asignar membresías, editar miembros,
  registrar entradas). Si un gym lo pide, se agregan permisos base después.
- **Cambiar la contraseña cierra las otras sesiones** de la persona, en todos sus gyms. La sesión
  desde la que se cambió sigue abierta.

## 12. Propuesta de sub-etapas

| Sub-etapa | Qué |
|---|---|
| **I1** | Bloqueo por intentos y aviso por correo. Cambio de contraseña con sesión (cierra las otras sesiones). Enlaces en vez de códigos (restablecer, activar). |
| **I2** | Una cuenta, varios gyms: `GymAccess`, selector de gym, cambiar de gym, migración. Invitaciones que reconocen cuentas existentes. |
| **I3** | Perfiles y permisos: los cinco permisos, tres perfiles, ajustes por persona, cierre de sesiones al quitar. |
| **I4** | Varios Owners, traspaso, ascender staff, contacto de facturación (con Gyms G4). |
| **I5** | Login del miembro en `/app`: pantalla con la marca del gym, código por correo, sesión persistente, registro que deja la sesión iniciada. |
| **I6** | Cambio de email con verificación y `UserEmailChanged` (con Members M5). |
| **I7** | Cuenta de dispositivo para el kiosco (con Attendance A3). |
