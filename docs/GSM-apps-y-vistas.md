# Apps y vistas — replanteamiento (v2)

> **Estado:** construido en la v2 (rama `v2`, verificado con tests el 2026-10-20). Describe el diseño vigente; lo que quedó sin hacer o sin verificar está en `V2-PENDIENTES.md`. Las frases «hoy…» que contrastan con lo anterior describen la v1, previa a esta versión.

## 1. Qué cambió

**Antes de la v2:**

| Quién | Web | App nativa (`GSM.Mobile`) |
|---|---|---|
| Miembro | `/app`, pensada para celular (en un computador se ve angosta) | Sí (`GSM.Member.UI`) |
| Staff y Owner | Panel en `/`, pensado para escritorio | **No** |
| SuperAdmin | `/admin` | No |
| Kiosco | `/kiosk` | No |

**v2 (decidido 2026-10-07):**

| Quién | Web | App nativa |
|---|---|---|
| Miembro | `/app`, **adaptable**: celular y escritorio | Sí |
| Staff y Owner | Panel en `/`, **adaptable** | **Sí: todo el panel**, con lo diario primero |
| SuperAdmin | `/admin` | No (solo web) |
| Kiosco | `/kiosk` | No (pantalla fija del gym) |

Una persona que es **staff y también entrena** en su gym puede usar los dos modos (§4).

## 2. Decisiones tomadas (2026-10-07)

| Tema | Decisión |
|---|---|
| Staff en el celular | **Todo el panel** está en la app y es adaptable; la navegación del celular pone **primero lo diario**. Configuración, planes y catálogos funcionan, pensados para pantalla grande (§5). |
| Doble rol | Quien es staff y entrena en el mismo gym tiene **los dos accesos** y cambia entre **"Modo staff"** y **"Modo miembro"** (§4). |
| Miembro en la web | **Adaptable a escritorio**: menú lateral y varias columnas en pantalla grande (§6). |
| Seguridad del staff en la app | **Huella, rostro o PIN** al volver a la app en modo staff tras 15 minutos (§7). |

## 3. Estructura del frontend

| Proyecto | Qué tiene | Lo usan |
|---|---|---|
| `GSM.Client` | Cliente de la API (sin cambios) | Todos |
| `GSM.UI.Shared` | Componentes comunes, sesión, tema (sin cambios) | Todos |
| `GSM.Member.UI` | Pantallas del miembro (ya existe) | Web y app |
| **`GSM.Panel.UI`** (nuevo) | **Pantallas del panel del gym**, que hoy están dentro de `GSM.Web` (47 páginas y diálogos) y su layout | Web y app |
| `GSM.Web` | Host web: monta `GSM.Panel.UI`, `GSM.Member.UI`, el SuperAdmin y el kiosco | Navegador |
| `GSM.Mobile` | Host nativo: monta `GSM.Panel.UI` y `GSM.Member.UI` | Android (iOS después) |

- Mover el panel a `GSM.Panel.UI` es el mismo patrón que ya se usó con `GSM.Member.UI` en F7.
- Solo dos pantallas usan JavaScript del navegador (el kiosco y copiar el enlace de invitación). El
  kiosco se queda en `GSM.Web`; copiar al portapapeles pasa por una interfaz con implementación web y
  nativa, como ya se hizo con el escáner (`IQrScanner`).
- **Conviene hacerlo antes que las demás sub-etapas de la v2**: así cada pantalla nueva del panel nace
  directamente en la librería compartida y adaptable, en lugar de construirse en `GSM.Web` y moverse
  después.

## 4. Doble rol: modo staff y modo miembro

- **Cambio en Identity** (`GSM-modulo-identity.md` §3.1): una persona puede tener **hasta dos accesos por
  gym**, uno de equipo (`owner` o `staff`) y uno de `member`. Antes era uno solo.
- El acceso de miembro exige una ficha en Members, como cualquier miembro: con su membresía, su
  rutina y sus datos.
- **Cambio de modo:** en el menú, "Pasar a modo miembro" / "Pasar a modo staff". Emite un token para el
  otro acceso, sin volver a pedir contraseña (como cambiar de gym).
  - **Web:** el modo staff vive en `/` y el modo miembro en `/app`.
  - **App:** abre en el último modo usado.
- **En modo miembro**, la persona es un miembro más: registra su entrada con el QR, su membresía cuenta
  para el límite del plan, y los permisos de staff no aplican.
- **Si su membresía la paga o no** lo decide el gym: puede asignarle un plan de cortesía (precio 0, con
  motivo; `GSM-modulo-memberships.md` §6).
- El selector de gyms muestra cada acceso por separado: "Gimnasio Demo · Staff" y "Gimnasio Demo ·
  Miembro".

## 5. Panel en el celular

**Navegación en el celular** (barra inferior):

| Botón | Lleva a |
|---|---|
| Inicio | Dashboard del gym, en su versión para celular (`GSM-dashboards.md`) |
| Recepción | Buscar miembro → tarjeta → registrar entrada, renovar y cobrar |
| Miembros | Búsqueda y ficha |
| Clases | "Mis clases" (entrenadores) u horario del día, con asistencia |
| Más | Avisos, caja, pagos, máquinas y fallas, comunicaciones, planes, configuración |

La campana de avisos va arriba, siempre visible.

**Reglas para adaptar las pantallas:**

- **Tablas → tarjetas** en pantallas angostas. `ServerTable<T>` gana un modo tarjeta, así que la mayoría
  de las listas se adaptan con un solo cambio.
- **Diálogos → pantalla completa** en el celular.
- **Imprimir** (reporte de la caja) → en la app, **compartir un PDF**.
- **Fotos** (miembro, máquina, reporte de falla) → en la app abre **la cámara del teléfono**.
- Punto de corte entre celular y escritorio: 960 px (el `md` de MudBlazor).
- **Prioridad:** primero se adaptan las pantallas diarias de la tabla de arriba. Configuración, planes,
  catálogo de ejercicios y editor de rutinas funcionan en el celular, pero se pulen para pantalla
  grande.

**Push para el staff:** con la app nativa, el staff en Android recibe push en la app. El panel
instalable como PWA (`GSM-modulo-notifications.md` §8) sigue sirviendo para quien no instale la app,
y para iPhone mientras no exista la app de iOS.

## 6. El miembro en la web de escritorio

- `/app` en pantalla grande usa **menú lateral** en lugar de la barra inferior.
- **Varias columnas** donde aporta:
  - Inicio: a la izquierda, plan, entrada y entrenamiento de hoy; a la derecha, próxima clase,
    progreso y novedades.
  - Rutina: la rutina del día al lado de su progreso por ejercicio.
  - Clases: el horario de la semana completo, en vez de día por día.
  - Mi progreso: calendario de racha y logros lado a lado.
- El registro de entrada con QR sigue siendo cosa del celular. En la web de escritorio se muestra
  "Registra tu entrada desde tu celular o en recepción".
- El login propio del miembro (`/app/entrar`) funciona igual en el computador.

## 7. Bloqueo de la app en modo staff

- Al volver a la app en **modo staff** después de **15 minutos** en segundo plano, pide **huella,
  rostro o el PIN del teléfono**.
- Si el teléfono no tiene bloqueo configurado, la app pide crear un **PIN propio de 4 a 6 dígitos**
  para el modo staff.
- **El modo miembro no lo pide.**
- No reemplaza a la sesión: si el token venció, se vuelve a iniciar sesión normalmente.
- Solo en la app nativa. En el navegador, el panel funciona como hoy.

## 8. Riesgos

- **La app Android nunca se ha probado en un teléfono real.** Con el staff usándola para cobrar y
  registrar entradas, deja de ser opcional probarla: hace falta un dispositivo o emulador.
- **iOS** sigue esperando una cuenta de Apple Developer y una Mac.
- El panel es la parte más grande del frontend; adaptarlo entero es trabajo considerable aunque se haga
  por partes.

## 9. Propuesta de sub-etapas

| Sub-etapa | Qué |
|---|---|
| **AP1** | Extraer `GSM.Panel.UI` de `GSM.Web` **sin cambios visibles**, con portapapeles por interfaz. Antes que el resto de la v2. |
| **AP2** | Panel adaptable: navegación del celular, tablas a tarjetas, diálogos a pantalla completa. Primero las pantallas diarias. |
| **AP3** | Panel dentro de `GSM.Mobile`: login único, cámara, PDF compartido, push del staff en la app. Probado en un Android real. |
| **AP4** | Doble rol: hasta dos accesos por gym y cambio de modo (con Identity I2). |
| **AP5** | Bloqueo con huella, rostro o PIN en modo staff. |
| **AP6** | Vista de escritorio del miembro en `/app`. |
| **AP7** | Pulir para celular las pantallas no diarias (configuración, planes, rutinas, catálogos). |

## 11. Una app o dos; marca blanca (decidido 2026-10-19)

- **Se mantiene una sola app nativa** (`GSM.Mobile`) para miembros, staff y Owner. El staff **no** queda limitado al PC: entrenadores y Owner trabajan desde el celular. El modo staff/miembro (§4) y el bloqueo (§7) cubren el doble rol y la privacidad.
- **A futuro, modelo híbrido:** la app genérica GSM sigue siendo la de todos (miembros de cualquier gym y todo el staff); como plan premium, cada gym puede tener su **app de marca blanca solo de miembro** (nombre, logo y colores del gym). El staff nunca usa una app de marca blanca.
- **Por qué la de marca blanca es solo de miembro:** el staff que trabaja en varios gyms tendría que instalar una app por gym; un arreglo del panel obligaría a republicar todas; Apple rechaza más fácilmente apps clonadas con un sistema de gestión genérico dentro.
- **Lo que costará cuando se construya:** un segundo host que cargue solo `GSM.Member.UI` con el gym, colores e ícono fijos; compilación automatizada por gym; publicación en la cuenta de desarrollador del propio gym (Apple lo exige); una app Firebase por paquete; republicar todas en cada cambio del miembro. La marca del gym ya existe en sus datos y se reutiliza.
- **Disparadores para hacerlo:** gyms dispuestos a pagar marca blanca. Separar el panel en su propia app genérica solo si aparecen reseñas confusas de miembros o el tamaño de descarga molesta.
