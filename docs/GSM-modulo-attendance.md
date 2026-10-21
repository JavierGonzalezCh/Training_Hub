# Módulo Attendance — replanteamiento (v2)

> **Estado:** construido en la v2 (rama `v2`, verificado con tests el 2026-10-20). Describe el diseño vigente; lo que quedó sin hacer o sin verificar está en `V2-PENDIENTES.md`. Las frases «hoy…» que contrastan con lo anterior describen la v1, previa a esta versión.

## 1. Propósito

Registrar **quién entró al gym y cuándo**, decidir en la puerta si puede entrar (preguntándole a
Memberships), y dar al gym la información que sale de eso: rechazos, ocupación y horas pico.

## 2. Decisiones tomadas

### 2.1 Heredadas de otras revisiones

| Tema | Decisión | Dónde |
|---|---|---|
| Gracia | Se va a Memberships. Attendance pide un `AccessStatus` y recibe si puede entrar, cuántos días le quedan y el texto del letrero. | `GSM-modulo-memberships.md` §5, §14 |
| Pasada la gracia | Bloqueado en el kiosco y en la app. Recepción puede **dejar pasar** con motivo obligatorio, y queda registrado. | Memberships §5.1 |
| Letrero al entrar | Con 7 días o menos, o en gracia, el miembro ve un letrero en la app; recepción ve el mismo texto. | Memberships §5.3 |
| Kiosco | Muestra el QR, "Bienvenido" y "¿Problemas para ingresar? Comunícate con un asesor". | Memberships §5.3 |
| Tiqueteras | Cada día con entrada consume una entrada, por el evento `CheckInRegistered` (ya existe). Sin gracia. | Memberships §7 |
| Clases | Reservar y asistir a una clase exige poder entrar al gym (estar al día, por vencer o en gracia). La asistencia registra la entrada del día como hoy. | `GSM-modulo-classes.md`, v2 |
| Avisos de salud | Recepción ve "tiene alertas de salud" y "falta la exoneración". | `GSM-modulo-members.md` §8 |

### 2.2 De esta revisión (2026-10-07)

| Tema | Decisión |
|---|---|
| Recepción | Elegir al miembro **no** registra la entrada. Hay que presionar un botón (§4). |
| Borrar una entrada | Sí, solo el **Owner** o un **staff con el permiso `ManageAttendance`** (§5). |
| Kiosco | Se crea una **cuenta de dispositivo** para el kiosco (§6). |
| Ocupación | Se calcula con una **permanencia de 2 horas** por entrada (§8). |
| Entrada con documento | En el kiosco, la persona puede **digitar su cédula**. Se acepta el riesgo de que alguien use el número de otro (§7). |
| Una entrada por día | **Es la regla general**: un segundo intento el mismo día se rechaza. **El staff puede habilitar entradas múltiples** a un miembro, con un máximo por día (deportistas que entrenan dos veces) (§3.2). |
| Geolocalización | **Solo con el QR fijo.** Si el miembro no comparte su ubicación, o está fuera del radio, la entrada se registra pero **no cuenta para logros ni racha** (§9). |
| Entrada con documento | Aprobada tal como está en §7, incluida la foto en el kiosco. |
| Push | Será un **módulo nuevo, Notifications** (§10). |
| Avisos de rechazos | **Por push.** Requiere construir el sistema de notificaciones push (§10). |
| Hardware (torniquetes, huella) | Para más adelante. |

## 3. Reglas de entrada

Orden de las verificaciones al registrar una entrada:

1. **Gym cerrado** según su horario → rechazo `gym_closed`, sin "Dejar pasar" en recepción
   (`GSM-modulo-gyms.md` §3.2). Vale también para la asistencia a clases.
2. **Miembro dado de baja** → rechazo `member_inactive`.
3. **Ya entró hoy** → rechazo `already_checked_in` (§3.1).
4. **`AccessStatus` de Memberships:**
   - cubierto, por vencer o en gracia → entra (con el letrero si corresponde);
   - congelado → rechazo `membership_frozen`;
   - tiquetera sin entradas → rechazo `pass_exhausted`;
   - vencida fuera de la gracia → rechazo `membership_expired`;
   - sin membresía → rechazo `no_membership`.
5. Si entra: se guarda la `Attendance`, se publica `CheckInRegistered` y se evalúan los logros (como
   hoy).

Los motivos de rechazo pasan de dos a siete: `membership_expired`, `no_membership`,
`membership_frozen`, `pass_exhausted`, `member_inactive`, `already_checked_in` y `gym_closed`.

**Recepción** pasa por las mismas verificaciones, pero ante un rechazo puede **dejar pasar** con motivo
(§4). Eso vale para todos los motivos menos dos: `member_inactive` (a un miembro dado de baja primero
se le reactiva la ficha) y `gym_closed` (si el gym abre fuera de horario, se carga como excepción del
día en el horario).

### 3.1 Una entrada por día

- **Regla general:** un miembro **solo puede registrar una entrada por día**, por cualquier vía (QR,
  documento o recepción). La excepción está en §3.2.
- **QR y documento:** un segundo intento el mismo día se rechaza con "Ya registraste tu entrada hoy a
  las 6:12". Queda un `CheckinAttempt` con motivo `already_checked_in`.
  - **Este motivo no suena en la campana** ni genera push, para no llenar de avisos al staff por
    alguien que escaneó dos veces. Se ve en Asistencia, filtrando por motivo.
  - Varios intentos repetidos el mismo día pueden indicar que el QR o el documento se están
    prestando. El panel lo resalta cuando hay 3 o más el mismo día.
- **Recepción:** si el miembro ya entró hoy, la tarjeta dice "Ya entró hoy a las 6:12" y no ofrece
  "Registrar entrada". Si alguien sale y vuelve (almuerzo), no hace falta otra entrada.
- La ocupación y las tiqueteras cuentan una vez por día, igual que la asistencia.

### 3.2 Entradas múltiples (excepción)

Decidido 2026-10-07: **el staff puede habilitar a un miembro para entrar más de una vez al día**, por
ejemplo un deportista de alto rendimiento que entrena mañana y tarde.

- Tabla nueva `attendance.member_entry_rules`: `MemberId`, `MaxDailyEntries` (2 a 5), `Reason`
  (obligatorio), `ValidUntil` (opcional; vacío = sin vencimiento), `GrantedBy`, `GrantedAt`.
- **Quién la habilita:** el Owner o cualquier staff, desde la ficha del miembro ("Entradas por día").
  Queda registrado quién y por qué, y se puede quitar.
- **Tiempo mínimo entre entradas:** 2 horas, el mismo número que la permanencia asumida (§8). Evita
  que alguien escanee y le pase el QR a otra persona en la puerta.
- Pasado el máximo del día, el siguiente intento se rechaza con `already_checked_in`, como en §3.1.
- **Efectos de cada entrada adicional:**
  - **Asistencia y ocupación:** cuenta cada entrada.
  - **Tiqueteras:** **cada entrada consume una**. Un deportista con tiquetera que entra dos veces gasta
    dos. La tabla `memberships.pass_usages` pasa de ser única por día a ser única por entrada.
  - **Racha y logros:** cuentan **días**, no entradas. Entrenar dos veces el mismo día no suma dos días
    de racha.
- La tarjeta de recepción muestra "Entradas múltiples: 2 por día · lleva 1 hoy".

## 4. Recepción

Pantalla `/attendance/checkin`, rediseñada:

1. **Buscar** por nombre o documento.
2. **Tarjeta del miembro** (todavía sin registrar nada): foto, nombre, estado comercial, días o
   entradas que quedan, letrero si corresponde, avisos de salud y exoneración, y "ya entró hoy" si
   aplica.
3. **Botones según el caso:**
   - Puede entrar → **"Registrar entrada"**.
   - Rechazado → **"Renovar y cobrar"** (si aplica) y **"Dejar pasar"**, que pide motivo.
   - Ya entró hoy → ninguno; solo el aviso.
4. **Resultado:** "Entrada registrada · 6:12", con los logros desbloqueados si los hay.

La entrada autorizada ("Dejar pasar") se guarda como `Attendance` con `AuthorizedBy` y
`AuthorizationReason`. El Owner la ve en Asistencia con el filtro "entradas autorizadas".

## 5. Borrar una entrada

- Para corregir errores, como registrar a otro miembro por equivocación.
- **Quién:** el **Owner**, o un staff con el permiso **`ManageAttendance`**. Se suma a la lista de
  permisos por cuenta de Identity, junto a `ViewMemberHealth` y `Trainer` (`GSM-modulo-members.md`
  §7).
- **Motivo obligatorio.** Es un borrado lógico: la entrada queda marcada como anulada (`VoidedAt`,
  `VoidedBy`, `VoidReason`) y deja de contar para todo. No se borra la fila, así queda la traza.
- **Efectos:**
  - Publica `CheckInVoided`. Memberships lo escucha y **devuelve la entrada de la tiquetera**.
  - La racha se recalcula sola, porque se calcula siempre desde las asistencias.
  - **Los logros ya desbloqueados no se quitan.** Quitar un logro que el miembro ya vio es peor que
    dejar uno de más por un error de recepción.
  - Deja libre el día: el miembro puede volver a registrar su entrada hoy.
- Solo se pueden anular entradas de los **últimos 7 días**. Más atrás ya no es una corrección de
  recepción.

## 6. Cuenta de dispositivo para el kiosco

Antes el kiosco usaba una sesión de staff normal, con el panel completo abierto en el equipo de la
entrada. Se creó un rol nuevo en Identity.

- **Rol `kiosk`:** solo puede mostrar el QR del gym (`GET /api/attendance/kiosk-code`) y registrar
  entradas por documento (§7). No ve miembros, pagos ni configuración.
- **Emparejamiento:**
  1. El Owner, en Configuración → Kiosco, presiona "Agregar kiosco", le pone nombre ("Entrada
     principal") y recibe un **código de 8 caracteres** que vence en 10 minutos.
  2. En el equipo del kiosco se abre `/kiosk` y se digita el código.
  3. El equipo queda con una sesión de dispositivo **de larga duración** (se renueva sola) y abre
     directamente la pantalla del kiosco.
- El Owner ve sus kioscos y puede **revocarlos** uno por uno; el equipo vuelve a pedir código.
- Salir de la pantalla del kiosco no lleva al panel: en el equipo no hay panel.
- **El staff ya no abre el kiosco con su sesión.** El enlace "Kiosco" del panel pasa a ser
  "Configurar kiosco".

## 7. Entrada con documento en el kiosco

- La pantalla del kiosco suma un **teclado numérico**: "¿No tienes la app? Digita tu documento".
- Vale lo mismo que el QR: mismas reglas (§3), mismo letrero, mismas tiqueteras. El método queda
  guardado como `document`.
- **Riesgo aceptado:** no hay forma de evitar que alguien digite el número de otro. Se reduce así:
  - **El kiosco muestra la foto del miembro** al registrar ("Bienvenida, Ana" + foto). Quien mira
    la pantalla, o el staff, ve si coincide.
  - **El miembro recibe un push** "Se registró tu entrada con tu documento a las 6:12" (cuando exista
    push, §10). Si no fue él, lo sabe de inmediato.
  - Las entradas por documento se pueden filtrar en Asistencia.
  - La restricción de una entrada por día (§3.1) limita el daño: el número de otro solo sirve una vez
    al día.
- **Qué muestra el kiosco**, sin dar detalles de la cuenta en una pantalla pública:
  - Entra: "**Bienvenida, Ana**" + foto, por 4 segundos. Si está por vencer o en gracia: "Tu plan
    vence pronto, pasa por recepción", sin fechas ni montos.
  - Rechazado (cualquier motivo): "**No pudimos registrar tu entrada. Comunícate con un asesor.**" No
    se dice el motivo en pantalla; el staff lo ve en la campana.
  - Documento que no existe: el mismo mensaje.
- Después de 5 intentos fallidos seguidos, el teclado se bloquea 1 minuto, para que nadie pruebe
  números al azar.
- Solo se activa si el gym lo enciende (flag `CheckinByDocument`, apagado por defecto).

## 8. Ocupación del gym

Arregla el bug **B4** (`docs/BUGS.md`): el aforo que el gym ya configura pasa a usarse.

- **Personas adentro ahora** = entradas no anuladas de las **últimas 2 horas**. Se asume que cada
  persona se queda 2 horas, que es más de lo normal, para tener margen. No se registra la salida.
- **Ocupación** = personas adentro ÷ aforo. Si el gym no configuró aforo, solo se muestra el número de
  personas.
- **Dónde se ve:**
  - **Dashboard del panel:** "18 personas · 45 % del aforo".
  - **App del miembro:** "¿Qué tan lleno está?" en tres niveles: **tranquilo** (< 40 %), **moderado**
    (40–75 %) y **lleno** (> 75 %), junto a las horas pico que ya existen. Sin número exacto.
- La permanencia de 2 horas es configurable por gym (`checkin_settings.assumed_stay_minutes`, 120 por
  defecto).
- Es una estimación; el texto de Configuración lo dice así.

## 9. Geolocalización

Decidido 2026-10-07. Comprueba que el miembro está cerca del gym cuando registra la entrada con el
**QR fijo** (el cartel impreso), que es el que se puede fotografiar y usar desde la casa.

- **Cuándo se pide:** **solo con el QR fijo.** El rotativo ya limita el abuso a 30 segundos, y la
  entrada por documento y la de recepción ocurren con la persona presente.
- **Cómo funciona:** el gym guarda sus coordenadas y un radio (Gyms, 150 m por defecto). Al escanear,
  la app pide la ubicación al teléfono y la envía con el código; el servidor calcula la distancia.
  **Solo se guarda el resultado** (`location_check`: `inside`, `outside`, `denied`), **nunca las
  coordenadas.**
- **Si no comparte la ubicación** (`denied`) **o está fuera del radio** (`outside`):
  - **La entrada se registra igual**: cuenta para el acceso, la tiquetera y la ocupación.
  - **No cuenta para logros ni para la racha.** La racha también queda fuera porque los logros de
    racha dependen de ella; si contara, el logro se colaría por ahí.
  - La app le explica: "Registramos tu entrada, pero sin tu ubicación no suma a tu racha ni a tus
    logros."
  - El staff puede filtrar estas entradas en Asistencia.
- Se trata igual "no compartió" que "fuera del radio" porque, dentro de un edificio, el GPS puede
  fallar aunque la persona esté adentro. Rechazar la entrada castigaría a quien sí está en el gym.
- **Limitaciones conocidas:** se puede engañar con apps de ubicación falsa. Frena el abuso casual, no
  el decidido.
- **Gyms** suma `Latitude`, `Longitude` y `GeofenceRadiusMeters`. Sin coordenadas configuradas no se
  pide ubicación y todas las entradas cuentan, como hoy.
- **Racha y logros:** Attendance expone el `location_check` de cada entrada, y el cálculo de racha
  (`MemberStreak`) y la evaluación de logros (Achievements) ignoran las que no son `inside` cuando el
  gym usa QR fijo. Se revisa en detalle en la revisión de Achievements.

## 10. Avisos por push

Decidido: los rechazos se avisan **por push**.

- **No existe** sistema de notificaciones push. **Será un módulo nuevo, Notifications** (decidido
  2026-10-07), que se diseña en su propia revisión: FCM para Android y la app web, APNs para iOS
  (exige cuenta de Apple Developer) y Web Push para el panel abierto en el navegador. Attendance solo
  publica eventos; Notifications decide a quién y cómo avisar.
- **Lo que Attendance va a enviar cuando exista:**
  - Al **staff y al Owner**: los rechazos, salvo `already_checked_in`.
  - Al **miembro**: "Se registró tu entrada con tu documento" (§7).
- Mientras tanto, los rechazos siguen en la campana del panel, como hoy.

## 11. Migración

- `attendance.checkin_settings.grace_days` se mueve a Memberships (`GSM-modulo-memberships.md` §13).
- `checkin_rejection_reason` suma cuatro valores.
- `attendance` suma `VoidedAt`, `VoidedBy`, `VoidReason`, `AuthorizedBy`, `AuthorizationReason`,
  `LocationCheck` y el método `document`. El índice único por miembro y día se reemplaza por una
  validación en el caso de uso, porque con entradas múltiples puede haber varias filas el mismo día.

## 12. Propuesta de sub-etapas

| Sub-etapa | Qué |
|---|---|
| **A1** | Recepción en dos pasos (tarjeta + botones), una entrada por día, motivos de rechazo nuevos, "Dejar pasar". Depende de MS1 (Memberships) para el `AccessStatus`. |
| **A1b** | Entradas múltiples habilitadas por el staff, con máximo diario y 2 horas entre entradas. |
| **A2** | Anular entradas: permiso `ManageAttendance`, motivo, `CheckInVoided`, devolución de la tiquetera. |
| **A3** | Cuenta de dispositivo para el kiosco: rol `kiosk`, emparejamiento, revocación. |
| **A4** | Entrada con documento en el kiosco (depende de A3). |
| **A5** | Ocupación con permanencia de 2 horas (corrige B4). |
| **A6** | Geolocalización con el QR fijo: coordenadas y radio del gym, `location_check`, exclusión de racha y logros (§9). |
| **A7** | Avisos por push (depende del módulo Notifications). |
| Después | Hardware: torniquetes, huella, tarjetas. QR en cada máquina (Equipment). |
| Después | **Evitar que un miembro entre sin registrar su entrada** (sobre todo con tiquetera). Con software solo no se puede garantizar; solo un torniquete lo garantiza. Opciones evaluadas el 2026-10-07 y aplazadas: (1) la app exige registrar la entrada antes de usar la rutina, reservar o reportar máquinas, con interruptor por gym; (2) lista "Actividad sin entrada" en el panel; (3) verificación en sala: el staff registra la entrada desde su teléfono al encontrar a alguien sin registrar. Las clases ya registran la entrada solas. |
