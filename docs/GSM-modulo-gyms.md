# Módulo Gyms — replanteamiento (v2)

> **Estado:** construido en la v2 (rama `v2`, verificado con tests el 2026-10-20). Describe el diseño vigente; lo que quedó sin hacer o sin verificar está en `V2-PENDIENTES.md`. Las frases «hoy…» que contrastan con lo anterior describen la v1, previa a esta versión.

## 1. Propósito

El gym como cliente del sistema: **quién es** (datos, marca, ubicación, horario), **quiénes son sus
dueños**, **cómo empieza a usar el sistema** (alta y onboarding) y **qué funciones tiene encendidas**.

## 2. Decisiones tomadas (2026-10-07)

| Tema | Decisión |
|---|---|
| Alta | **Solo el SuperAdmin** da de alta gyms (venta asistida). **El Owner recibe un enlace para crear su contraseña**: el SuperAdmin deja de conocerla. |
| Onboarding | **Asistente obligatorio** al primer ingreso. El panel no se habilita hasta completar los pasos obligatorios, entre ellos **registrar máquinas**. |
| Datos nuevos | **Dirección y coordenadas**, **horario de atención**, **razón social** y **redes sociales**. |
| Zona horaria | Sigue siendo una sola para todo el sistema (`America/Bogota`). Se revisa si hay gyms fuera de Colombia. |
| Dueños | **Varios Owners por gym**, con los mismos poderes, y **traspaso** de la propiedad. Siempre debe quedar al menos uno. |
| Gym cerrado | **No se registran entradas con el gym cerrado** según su horario, por ninguna vía (§3.2). |
| Días de apertura | **El gym abre todos los días**, domingos y festivos incluidos, aunque sea con otro horario (§3.1). |
| Prueba gratis | **Opcional, la decide el SuperAdmin al crear cada gym**, con los días que quiera. Sin prueba, el gym nace activo con su primer pago (§4; `GSM-modulo-superadmin.md` §V2). |

## 3. Datos del gym

| Grupo | Campo | Oblig. | Notas |
|---|---|---|---|
| Identidad | `Name`, `Slug` | Sí | Igual que hoy. |
| | `LegalName` | No | **Nuevo.** Razón social, para recibos. |
| | `Nit`, `Phone`, `Email` | No | Igual. `Phone` pasa a formato E.164, como en Members. |
| Marca | `LogoUrl`, `PrimaryColor`, `SecondaryColor` | | Igual. La app del miembro ya usa los colores. |
| Ubicación | `Address`, `City` | **Sí** | **Nuevo.** Se piden en el onboarding. |
| | `Latitude`, `Longitude` | No | **Nuevo.** Se marcan con un pin en un mapa. **Son obligatorias para usar el QR fijo**, porque la geocerca las necesita (`GSM-modulo-attendance.md` §9). |
| | `GeofenceRadiusMeters` | | **Nuevo.** 150 por defecto. |
| Redes | `InstagramUrl`, `FacebookUrl`, `TikTokUrl`, `WebsiteUrl`, `WhatsAppPhone` | No | **Nuevo.** Se muestran en la app del miembro. `WhatsAppPhone` en E.164, para abrir el chat con el gym. |
| Operación | `Capacity` | No | Igual. Pasa a usarse para la ocupación (corrige B4). |
| Comercial | `Plan`, `Status` | | Igual; los maneja SuperAdmin. |
| Dueños | ~~`OwnerId`~~ | | **Se quita** (§6). |

### 3.1 Horario de atención — `GymOpeningHours` (nueva)

- **Por día de la semana**, con uno o varios tramos: "lunes 5:00–12:00 y 15:00–22:00".
- **Abierto 24 horas** (casilla por día): ese día no tiene tramos y el gym nunca está cerrado. Un tramo normal cierra como máximo a las 23:59; no hay tramos que crucen la medianoche. Decidido 2026-10-11, pendiente de construir (G3b).
- **Todos los días tienen horario** (decidido 2026-10-07): se asume que el gym abre de lunes a
  domingo, y el domingo puede tener un horario distinto. El formulario exige al menos un tramo por día, o la casilla de 24 horas.
- **Festivos de Colombia:** el gym **abre en festivos**, con su propio horario ("festivos:
  7:00–13:00"), que se pide junto con el horario semanal. El sistema calcula los festivos del año,
  incluidos los que se corren al lunes (Ley Emiliani), y a cada uno le aplica el horario de festivo.
  Cada festivo se puede ajustar.
- **Excepciones por fecha** — `GymSpecialDay`: otro horario en una fecha concreta ("24 dic:
  6:00–12:00"). **Cerrar un día completo** es posible solo como excepción explícita y con
  confirmación ("¿Seguro? Ese día nadie podrá entrar y las clases se cancelan"), para casos como un
  arreglo del local. No es lo normal.
- **Dónde se usa:**
  - **App del miembro:** "Abierto · cierra a las 22:00" o "Cerrado · abre mañana a las 5:00", en el
    inicio y en la página del gym.
  - **Ocupación** (Attendance): solo se muestra con el gym abierto.
  - **Horas pico:** solo se grafican las horas de atención.
  - **Clases:** solo se pueden agendar dentro del horario (`GSM-modulo-classes.md`, v2).
  - **Entradas:** con el gym cerrado no se registran (§3.2).

### 3.2 Entradas con el gym cerrado

Decidido 2026-10-07: **no se registran entradas con el gym cerrado.**

- Vale para **todas las vías**: QR, documento en el kiosco **y recepción**. Recepción **no** tiene
  "Dejar pasar" para este caso, a diferencia de una membresía vencida.
- Rechazo nuevo en Attendance: `gym_closed`. La app dice "El gimnasio está cerrado. Abre mañana a las
  5:00". El kiosco, como en todos los rechazos, dice "Comunícate con un asesor".
- **Si el gym abre antes o cierra después un día puntual**, el staff lo refleja en el horario como
  excepción de ese día (§3.1), y desde ese momento se aceptan entradas.
- **Las clases solo se agendan dentro del horario del gym** (decidido 2026-10-07; ver
  `GSM-modulo-classes.md`, "Replanteamiento v2"). Por eso la asistencia a una clase también respeta
  esta regla, sin excepción.
- **Un gym sin horario cargado no rechaza nada.** Solo pasa con gyms anteriores al onboarding, porque
  en el onboarding el horario es obligatorio.
- `already_checked_in` y el resto de los rechazos se evalúan después de este: con el gym cerrado no se
  llega a mirar la membresía.

## 4. Alta de un gym (SuperAdmin)

1. El SuperAdmin crea el gym con su nombre, slug, NIT, plan y **nombre y email del Owner**. **Ya no
   escribe contraseña.**
2. Identity crea la cuenta del Owner **sin contraseña**, como ya hace con el staff, y genera un enlace
   de activación: `/activar?email=…&code=…`, que vence en 72 horas.
3. El Owner recibe el enlace por correo ("Bienvenido a GSM, activa tu cuenta"). Al abrirlo crea su
   contraseña y entra directo al onboarding (§5).
4. **Ficha del gym en SuperAdmin:**
   - Muestra a los Owners, si ya activaron la cuenta y en qué paso del onboarding van.
   - **"Reenviar acceso"**, que genera un enlace nuevo y anula el anterior.
   - **"Copiar enlace"**, para mandarlo por WhatsApp si el correo no llega. Queda registrado en la
     auditoría de SuperAdmin, igual que la impersonación, porque quien tiene el enlace puede activar
     la cuenta. El enlace deja de servir en cuanto el Owner crea su contraseña.
5. **Prueba opcional** (decidido 2026-10-07, en la revisión de SuperAdmin): el SuperAdmin elige en el
   alta si el gym tiene prueba y cuántos días. Sin prueba, el alta registra el primer pago y el gym nace
   activo. Antes el handler de `GymCreated` creaba siempre 30 días de prueba a precio 0; ahora la
   prueba es opcional (`GSM-modulo-superadmin.md` §V2).

Resuelve dos huecos de la v1: el enlace en vez de código y la ficha de SuperAdmin, que no mostraba
al Owner ni permitía reenviarle el acceso.

## 5. Onboarding — asistente obligatorio

Al primer ingreso, el Owner ve el asistente en `/onboarding`, no el panel. Puede salir y volver: el
avance se guarda paso por paso.

| Paso | Qué | Obligatorio |
|---|---|---|
| 1. Tu gym | Nombre, logo, colores, razón social, NIT, teléfono, email, redes. | Nombre (ya viene); lo demás opcional |
| 2. Ubicación | Dirección, ciudad y pin en el mapa. | **Dirección y ciudad.** El pin, solo si usará QR fijo. |
| 3. Horario | Horario de los 7 días y de festivos (con una plantilla para empezar: L–V 5:00–22:00, S 7:00–14:00, D y festivos 7:00–13:00). | **Sí** |
| 4. Máquinas | "Agregar del catálogo": cuadrícula con imágenes, cantidad y zona (`GSM-modulo-equipment.md` §8). | **Sí, al menos una** |
| 5. Planes | Al menos un plan. Plantillas para empezar (Día, Mensual, Trimestral, Anual) donde solo se pone el precio. También matrícula y días de gracia. | **Sí, al menos uno** |
| 6. Entrada al gym | Modo del QR (rotativo o fijo), emparejar el kiosco, entrada con documento, aforo. | No |
| 7. Tu equipo | Invitar staff y otros Owners, con sus permisos. | No |

- **El panel se habilita al completar los pasos 1 a 5.** Antes, cualquier ruta del panel lleva al
  asistente. Lo controla el frontend, con `GET /api/gym/onboarding`, que dice qué pasos faltan.
- **Al terminar,** el dashboard muestra una lista "Para completar" con lo opcional que quedó
  pendiente: kiosco, staff, fotos propias de las máquinas, primeros miembros.
- **El staff** que entra a un gym con el onboarding sin terminar ve "Tu gym todavía se está
  configurando" y no puede hacer nada más.
- **El SuperAdmin puede marcar el onboarding como omitido** para un gym, con motivo, en casos de
  soporte (por ejemplo, una migración de datos hecha por el equipo). Queda en la auditoría.
- **Gyms existentes:** la migración marca su onboarding como completado, para no bloquearlos. Como el
  gym de demostración no tiene máquinas, ve la lista "Para completar".
- Estado guardado en `gyms.gym_onboarding` (`GymId`, pasos completados, `CompletedAt`,
  `SkippedBy`, `SkipReason`). Cada paso se marca cuando su dato existe de verdad (por ejemplo, el
  paso 4 se completa cuando Equipment tiene al menos una máquina, leído por `IEquipmentReader`), no
  porque se haya presionado "Siguiente".

## 6. Varios Owners y traspaso

- **`Gym.OwnerId` desaparece.** Los Owners de un gym son las cuentas de Identity con rol `owner` y ese
  `gym_id`. Pueden ser varios, con los mismos poderes.
- **Agregar un Owner:** cualquier Owner invita por email (nace sin contraseña, con enlace de
  activación, como el staff), o **asciende a un staff** existente.
- **Quitar un Owner:** cualquier Owner puede quitar a otro, o quitarse a sí mismo. **No se puede quitar
  al último.** El Owner quitado se desactiva o, si se elige, queda como staff.
- **Traspaso:** es agregar al nuevo dueño y quitarse uno mismo. El panel lo ofrece como un flujo
  guiado: "Traspasar el gym".
- **Contacto de facturación:** uno de los Owners se marca como contacto para SuperAdmin (cobros del
  SaaS, avisos de suscripción). Si se quita, pasa a otro Owner.
- **El SuperAdmin** también puede agregar y quitar Owners. Es el caso de soporte "el dueño perdió el
  acceso" o "vendió el gym".
- **Los avisos para "el Owner"** (máquina insegura, anonimizaciones próximas…) van a **todos** los
  Owners.
- Todo cambio de Owners queda registrado: quién, a quién y cuándo.
- Se gestiona en Configuración → Equipo, en la misma pantalla que el staff.

## 7. Configuración ordenada

Con el replanteamiento, cada módulo tiene sus ajustes. **Cada ajuste sigue viviendo en su módulo**,
pero el Owner los ve juntos en `/settings`, ordenados por tema:

| Sección | Ajustes | Módulo dueño |
|---|---|---|
| Gym | Datos, marca, ubicación, horario, festivos, redes | Gyms |
| Equipo | Owners, staff y permisos (`ViewMemberHealth`, `Trainer`, `ManageAttendance`) | Identity |
| Membresías | Gracia, días de aviso (7), matrícula | Memberships |
| Entrada al gym | Modo del QR, kioscos, entrada con documento, permanencia asumida, aforo, radio de la geocerca | Attendance + Gyms |
| Rutinas | Semanas de revisión, los miembros pueden crear rutinas | Routines |
| Equipos | Los miembros pueden reportar fallas | Equipment |
| Comunicaciones | Igual que hoy | Communications |
| Logros | Igual que hoy | Achievements |
| Miembros | Textos de las autorizaciones, plazo de anonimización (24 meses) | Members |
| Módulos | Funciones encendidas o apagadas | Gyms |

**Funciones (flags)** de `GymFeatureFlags`:

- Se **quita** `CommunityComments`, que no hacía nada.
- Se **suman** `CheckinByDocument` (Attendance §7, apagado por defecto) y `MembersCanReportEquipment`
  (Equipment, encendido por defecto).

## 8. App del miembro

Página nueva **"Mi gimnasio"** (`/app/gimnasio`): logo, dirección con mapa y botón "Cómo llegar",
horario con abierto/cerrado, ocupación, teléfono, WhatsApp y redes. En el inicio de la app se muestra
"Abierto · cierra a las 22:00".

## 9. Lo que cambia en otros módulos

- **Identity:** Owners sin contraseña con enlace de activación; varios Owners por gym; contacto de
  facturación; ascender staff a Owner.
- **SuperAdmin:** el alta deja de pedir contraseña; la ficha muestra Owners y onboarding, con
  "Reenviar acceso", "Copiar enlace" y "Omitir onboarding" (auditados).
- **Attendance:** usa coordenadas y radio (geocerca) y el horario (§10).
- **Equipment, Memberships:** el onboarding usa sus pantallas de "Agregar del catálogo" y de planes.

## 11. Propuesta de sub-etapas

| Sub-etapa | Qué |
|---|---|
| **G1** | Alta con invitación: Owner sin contraseña, enlace de activación, "Reenviar acceso" y "Copiar enlace" en SuperAdmin. Prueba opcional en el alta (con SuperAdmin S1). |
| **G2** | Datos nuevos: dirección, coordenadas, razón social, redes. Página "Mi gimnasio" en la app. |
| **G3** | Horario de atención, excepciones y festivos de Colombia. Abierto/cerrado en la app. Rechazo `gym_closed` en Attendance. |
| **G4** | Varios Owners, traspaso y contacto de facturación. |
| **G5** | Onboarding: asistente, bloqueo del panel, lista "Para completar", omitir desde SuperAdmin. Depende de E1 (máquinas) y de las plantillas de planes (Memberships). |
| **G6** | Configuración ordenada por secciones y cambios de flags. |
