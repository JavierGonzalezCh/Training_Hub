# Plan de construcción — Check-in con QR del gym (Etapa 12)

> **Estado: completado (2026-10-05).** Sub-etapas 12.1–12.6 construidas. Desviaciones respecto al plan:
> - `Mode` viaja como `string` (`Rotating`/`Static`) en los DTOs, no como enum: NSwag generaba `int` y rompía la lectura (ver CLAUDE.md).
> - `regenerate` en modo rotativo responde 409 sin crear fila (la fila ya existe en modo fijo).
> - El panel de "Intentos rechazados hoy" del dashboard es un panel a ancho completo bajo el feed, no una tercera columna.
> - El kiosco rota el código solo al caducar el actual (el anterior sigue valiendo una ventana más).
> - Verificado en navegador (12.4 y 12.5): kiosco, campana, dashboard, ajustes y flujo del miembro, en los tres temas y a 375 px. No verificado: la vista previa de impresión del cartel, y el escáner nativo en un dispositivo real; el build de Windows de GSM.Mobile falla en este entorno desde antes de la etapa. Decidido con el usuario el 2026-10-05.
>
> Documento de trabajo para el modelo que lo ejecute. Complementa `CLAUDE.md` y `docs/GSM-especificacion-tecnica.md`: **si algo aquí los contradice, ellos mandan**, salvo en lo que este plan cambia a propósito y que está marcado como **Cambio**.
>
> **Ritmo:** una sub-etapa por vez, **sin empezar la siguiente sin confirmación explícita del usuario**. Cada una termina compilando, con su migración aplicada, sus endpoints respondiendo y la suite completa en verde. Al acabar cada sub-etapa, informa qué se hizo, qué se verificó y qué no.

---

## Qué cambia y por qué

**Hoy:** cada miembro tiene un QR personal fijo (`members.qr_code`). Un recepcionista lo escanea en `/attendance/checkin`, o el miembro pulsa "Registrar mi entrada" en `/app` (solo si el gym tiene `CheckinQr` activo).

**Problemas de hoy:**
- Hace falta una persona en la puerta escaneando códigos.
- El autoregistro del miembro no comprueba que esté en el gym: puede pulsarlo desde casa.
- El interruptor `CheckinQr` solo oculta el botón; el backend no lo comprueba.
- El QR personal es fijo: una captura sirve para siempre y se puede compartir.

**Después:** **el gym muestra un QR en la entrada y el miembro lo escanea con su teléfono.** La API solo acepta el autoregistro con un código válido del gym. No hace falta nadie en la puerta.

### Decisiones del usuario (no reabrir)

| # | Decisión |
|---|---|
| 1 | **El gym elige el tipo de QR** en Configuración: **rotativo** en una pantalla (cambia cada 30 s) o **impreso fijo** (regenerable). Por defecto: **rotativo**. |
| 2 | **Días de gracia configurables por gym** tras vencer la membresía. Dentro de la gracia la entrada se registra con aviso; **pasada la gracia, no se registra**. Por defecto: **3 días**. |
| 3 | **Cada intento rechazado se notifica al gym**, dentro del panel (no hay push ni email configurados). |
| 4 | **El miembro escanea de dos formas:** con la cámara del teléfono (el QR es un enlace) **y** con un escáner dentro de la app nativa (MAUI). La web no necesita escáner propio: la cámara del teléfono hace ese trabajo. |
| 5 | **Se elimina el QR personal del miembro**: de la app, del panel, de la API y de la base. |

### Decisiones de diseño de este plan

- **Todo vive en el módulo Attendance.** Ajustes, códigos e intentos rechazados son de Attendance (schema `attendance`). **No hay escritura cruzada nueva.** Attendance lee `IGymReader` (flags) e `IMembershipReader` (vigencia), que ya existen.
- **El staff en recepción no está sujeto a la gracia.** El registro manual siempre se acepta (con el aviso visible): si hay una persona en el mostrador, decide ella.
- **El interruptor `CheckinQr` se reinterpreta** como "los miembros pueden registrarse con el QR del gym", y **se aplica en el servidor**.
- **El kiosco usa una sesión de staff normal.** Un staff inicia sesión en la tablet y abre `/kiosk`. Una cuenta de "dispositivo" solo para el kiosco queda **fuera de alcance**; anótalo como pendiente.
- **Sin scheduler**, como en todo el proyecto. El QR rotativo no se genera en segundo plano: se calcula al pedirlo.

---

## Diseño del código del gym

### Formato

El código va dentro del QR como parámetro de un enlace: `{BaseUri del frontend}app/checkin?t={código}`.

| Modo | Código | Carga firmada |
|---|---|---|
| Rotativo | `r{ventana}.{firma}` | `{gymId:N}\|r\|{ventana}` |
| Fijo | `s{versión}.{firma}` | `{gymId:N}\|s\|{versión}` |

- `ventana` = `floor(unixSeconds / 30)`, sacado de `IClock.UtcNow`. **Nunca `DateTimeOffset.UtcNow` suelto.**
- `versión` = entero ≥ 1 guardado en los ajustes del gym. "Regenerar" lo incrementa, y el cartel anterior deja de servir.
- `firma` = HMAC-SHA256 de la carga con la clave `Checkin:SigningKey`, en base64url y truncada a 22 caracteres.

### Validación

- La carga se recalcula con el `gymId` **del JWT del miembro**, nunca con uno que venga en el código. Así un código de otro gym falla solo.
- Compara las firmas con `CryptographicOperations.FixedTimeEquals`.
- Rotativo: acepta la ventana actual y la anterior (hasta 60 s de vida, para cubrir el tiempo de escanear y cargar).
- **Solo vale el modo vigente del gym.** Si el gym pasa de fijo a rotativo, los carteles impresos dejan de servir, y viceversa.
- Código inválido o caducado → **422** (`ValidationDomainException`): "El código QR no es válido o ya caducó. Escanea de nuevo." **No se registra como intento rechazado:** es un error de lectura, no un intento de entrar sin pagar.

### Clave

- Va en la configuración `Checkin:SigningKey`, al estilo de `Jwt:SigningKey`:
  - vacía en `appsettings.json`;
  - con un valor de desarrollo en `appsettings.Development.json`;
  - con al menos 32 caracteres;
  - la API **no arranca** si falta.
- Anótalo en el README, en "Pendiente de configurar", para producción.
- La interfaz `ICheckinCodeSigner` va en `Application/Attendance/Abstractions` (`Generate(gymId, mode, version, now)` y `Validate(gymId, code, mode, version, now)`). La implementación va en Infrastructure, porque la criptografía es infraestructura.

---

## Punto de partida y convenciones que hay que respetar

Lee `CLAUDE.md` entero antes de empezar. Lo que más se suele romper:

- **Tiempo:** `IClock.Today` para días de negocio y `IClock.UtcNow` para instantes. `EndDate` de una membresía es **el último día cubierto, inclusive**: está vencida cuando `today > EndDate`.
- **Errores:** 422 para una invariante o un código inválido, 409 para un choque de estado (membresía vencida pasada la gracia), 403 para algo prohibido (`CheckinQr` apagado), 404 para lo que no existe. 400 solo para la forma del request (FluentValidation).
- **Enums de Postgres:** se registran en **tres sitios**: `GsmDbContext`, `DependencyInjection` y `GsmDbContextFactory`. Si tienen valores de varias palabras, van con **`SnakeCaseNameTranslator`** y el miembro C# en PascalCase (`MembershipExpired`), nunca con guion bajo (ver la nota de `class_booking_status` en CLAUDE.md).
- **Un caso de uso por clase**, con `HandleAsync`, inyectado directo en el controller.
- **Migraciones:** `Etapa12_{n}_{Nombre}`. Se aplican a mano:
  ```bash
  cd src/Backend
  dotnet ef database update --project GSM.Infrastructure
  ```
  **No uses `--startup-project GSM.API`.** La base de tests la migra sola `ApiFactory`.
- **Tests:** son de integración contra Postgres (`dotnet test` desde `src/Backend` o sobre `tests/GSM.IntegrationTests`). Usa `MutableClock` para el tiempo. Para probar por HTTP con la identidad real, sigue el patrón de `SubscriptionGateHttpTests`. **No modifiques datos sembrados por migraciones.**
- **Eventos:** se publican **después** del commit.

### Entorno (Windows)

- Hay Git Bash y Windows PowerShell **5.1**. **No hay `pwsh`**, así que `scripts/generate-client.ps1` falla (usa `-SkipCertificateCheck`). Para regenerar el cliente, reproduce sus pasos a mano, con la API arrancada:
  1. `curl -sk https://localhost:7219/openapi/v1.json -o src/Frontend/GSM.Client/openapi-v1.json`
  2. En `src/Frontend/GSM.Client`: `dotnet tool restore` y `dotnet nswag run nswag.json`.
  3. Reaplica el parche de `DaysOfWeek` que describe el script: `ICollection<int>` → `ICollection<string>` en las 2 ocurrencias.
  4. Revisa el diff de `Generated/`. Si aparece una **colección de enum** nueva en una respuesta, sospecha el mismo defecto de NSwag.
- Muchos archivos usan **CRLF**. Al editar con scripts, respeta el salto de línea de cada archivo.
- Los servidores de desarrollo se arrancan con las herramientas de vista previa (`.claude/launch.json`: `api` en 7219 y `web` en 7220), no con Bash. **Usa la API por HTTPS (7219):** por HTTP redirige, y la redirección pierde la cabecera `Authorization`.
- Los cambios en `.razor` y `.cs` del frontend necesitan reiniciar el servidor `web`. Los de `wwwroot` (CSS/JS) se ven con recargar la página.

---

# 12.1 — Ajustes de check-in y código del gym

**Objetivo:** el gym configura el modo de QR y los días de gracia, y el panel puede pedir el código vigente. Todavía no cambia cómo se registra la entrada.

### Trabajo

1. **Domain** (`GSM.Domain/Attendance/`)
   - `CheckinMode` (enum CLR): `Rotating`, `Static`.
   - `CheckinSettings` (`BaseEntity`, `ITenantEntity`), uno por gym:
     - campos: `GymId`, `Mode`, `GraceDays`, `StaticCodeVersion`;
     - `Default(gymId)` → `Rotating`, 3 días, versión 1;
     - `Update(mode, graceDays)` valida `0 ≤ graceDays ≤ 30` (si no, `ValidationDomainException`);
     - `RegenerateStaticCode()` incrementa la versión.
2. **Infrastructure**
   - `CheckinSettingsConfiguration`: schema `attendance`, tabla `checkin_settings`, índice único `ix_checkin_settings_gym_id`.
   - Enum `attendance.checkin_mode` (`rotating`, `static`), registrado en los tres sitios.
   - `CheckinSettingsRepository` con `GetAsync(gymId)` y `Add`.
   - `HmacCheckinCodeSigner` implementa `ICheckinCodeSigner`, con opciones `CheckinOptions { SigningKey }` validadas al arrancar.
3. **Application** (`GSM.Application/Attendance/`)
   - **Ajustes perezosos.** Si el gym no tiene fila, se usan los valores por defecto **sin persistir**. La fila se crea en el primer `PUT` o en el primer "regenerar". Así no hace falta migrar datos para los gyms existentes: es el mismo patrón perezoso del congelador de racha.
   - Casos de uso:
     - `GetCheckinSettingsHandler`
     - `UpdateCheckinSettingsHandler`
     - `GetKioskCodeHandler`: devuelve el código del modo vigente y, en rotativo, su caducidad, que es el fin de la ventana actual.
     - `RegenerateStaticCodeHandler`
   - `GetKioskCodeHandler` lanza **403** si `CheckinQr` está apagado (léelo por `IGymReader.GetFeatureFlagsAsync`).
   - Validador FluentValidation de `UpdateCheckinSettingsRequest`.
4. **API** (en `AttendanceController` o en un `CheckinSettingsController` nuevo bajo `/api/attendance`)

   | Método | Ruta | Política | Respuesta |
   |---|---|---|---|
   | GET | `/api/attendance/settings` | RequireStaff | `CheckinSettingsResponse(Mode, GraceDays)` |
   | PUT | `/api/attendance/settings` | RequireOwner | `CheckinSettingsResponse` |
   | GET | `/api/attendance/kiosk-code` | RequireStaff | `KioskCodeResponse(Mode, Code, ExpiresAtUtc?)` |
   | POST | `/api/attendance/kiosk-code/regenerate` | RequireOwner | `KioskCodeResponse` (solo en modo fijo; en rotativo, 409) |

   La versión del código fijo **no** sale en las respuestas: solo el código.
5. **Config:** `Checkin:SigningKey` en `appsettings.json` (vacía) y en `appsettings.Development.json` (valor de desarrollo, ≥ 32 caracteres). La factory de tests debe aportar una clave.

### Migración
`Etapa12_1_CheckinSettings`: enum `attendance.checkin_mode` y tabla `attendance.checkin_settings`.

### Aceptación (tests de integración)
- Un gym sin fila de ajustes devuelve `Rotating` y 3 días, y no se crea ninguna fila.
- `PUT` con `graceDays = 31` → **400**: el rango es forma del request y lo rechaza el validador de FluentValidation. La invariante del dominio (`Update` → 422) queda como red de seguridad y se prueba en un test de dominio o de caso de uso. `PUT` como Staff → 403.
- Dos `GET kiosk-code` en la misma ventana devuelven el mismo código. Avanzando el `MutableClock` 60 s, el código cambia.
- `regenerate` en modo fijo cambia el código. En rotativo → 409.
- Con `CheckinQr` apagado, `GET kiosk-code` → 403.
- Aislamiento: el código de un gym no se puede generar ni leer desde otro.
- Suite completa en verde.

---

# 12.2 — Check-in del miembro con el código, días de gracia e intentos rechazados

**Objetivo:** el miembro solo puede registrarse con un código válido de su gym. La gracia decide si entra. Los rechazos quedan registrados y el panel puede consultarlos.

### Trabajo

1. **Contratos.** **Cambio:** `CheckInRequest(Guid? MemberId, string? GymCode)`. Se eliminan `QrCode` y `Method`.
   - Staff/Owner: `MemberId` es obligatorio, y el método siempre es `Manual`.
   - Miembro: `GymCode` es obligatorio, y el método siempre es `Qr`.
   - Si falta el que corresponde → 422.
   - `CheckInResponse` añade `DateOnly? MembershipEndsOn` e `int? GraceDaysLeft` (no nulo solo cuando entra dentro de la gracia).
   - Ajusta `AttendanceValidators` (`GymCode` con máximo 64).
2. **`IMembershipReader`:** añade `GetLatestEndDateAsync(memberId)` → `DateOnly?`. Es el `EndDate` máximo entre las membresías del miembro, en cualquier estado; las pausadas cuentan con su `EndDate`. Implémentalo en `MembershipReader`.
3. **Domain** (`GSM.Domain/Attendance/`)
   - `CheckinRejectionReason` (enum CLR): `MembershipExpired`, `NoMembership`.
   - `CheckinAttempt` (`ITenantEntity`):
     - campos: `MemberId`, `Reason`, `AttemptedAt`, `AttemptDate` (día de negocio), `AttemptCount`, `MembershipEndedOn?`, `AcknowledgedAt?`, `AcknowledgedBy?`;
     - métodos: `Repeat(now)` (incrementa el contador y actualiza `AttemptedAt`) y `Acknowledge(userId, now)`.
4. **`CheckInHandler`.** El flujo para el **miembro** queda así:
   1. Resolver al miembro por `UserId`, como hoy.
   2. Si `CheckinQr` está apagado → 403: "Tu gimnasio no tiene activado el check-in con QR."
   3. Si el miembro está inactivo → 409, como hoy.
   4. Validar `GymCode` con `ICheckinCodeSigner` contra el modo y la versión vigentes. Si no vale → 422.
   5. Si ya entró hoy, devolver el registro existente (200), como hoy. **Antes** de la regla de gracia: quien ya entró no vuelve a ser rechazado.
   6. Aplicar la regla de gracia (`today = IClock.Today`, `end = GetLatestEndDateAsync`):
      - membresía activa (`IsActiveAsync`) → entra;
      - `end` existe y `today <= end + GraceDays` → entra, con `GraceDaysLeft = (end + GraceDays) - today` y `MembershipEndsOn = end`;
      - si no → **rechazo**: registrar o repetir el intento, publicar `CheckInRejected` y lanzar **409** con un `detail` legible: "Tu membresía venció el {fecha}. Pasa por recepción para renovarla." o "No tienes una membresía activa. Pasa por recepción.".
   7. Crear la entrada, publicar `CheckInRegistered` y evaluar los logros, como hoy.

   Para el **staff**: sin código y sin la regla de gracia. Se registra siempre, con `MembershipExpired`, `MembershipEndsOn` y `GraceDaysLeft` informados para que el aviso se vea en recepción.

   **El intento se guarda antes de lanzar la excepción.** `UnitOfWork.SaveChangesAsync` abre su propia transacción por llamada, así que lo guardado no se revierte al lanzar. Verifica en un test que el intento persiste después del 409.

   **Deduplicación:** si ya hay un intento **pendiente** (sin `AcknowledgedAt`) del mismo miembro **el mismo día**, se llama a `Repeat(now)` en lugar de crear una fila nueva. Un miembro que insiste diez veces no llena la bandeja.
5. **Evento** `CheckInRejected(AttemptId, GymId, MemberId, Reason)` en `Attendance/Events`. Hoy no tiene consumidor: añádelo a la lista de "publicados sin consumidor" de la especificación, como disparador reservado para push o email.
6. **Intentos para el panel.** Casos de uso y endpoints (todos RequireStaff):

   | Método | Ruta | Respuesta |
   |---|---|---|
   | GET | `/api/attendance/attempts?from=&to=&pendingOnly=&page=&pageSize=` | `PagedResult<CheckinAttemptItem(Id, MemberId, MemberName, AttemptedAt, AttemptCount, Reason, MembershipEndedOn?, AcknowledgedAt?)>` |
   | GET | `/api/attendance/attempts/pending-count` | `{ count }` |
   | POST | `/api/attendance/attempts/{id}/acknowledge` | 204 |
   | POST | `/api/attendance/attempts/acknowledge-all` | 204 |

   `MemberName` sale por `IMemberReader`, no con un join a `members` (no hay FK entre schemas). Sigue el patrón de `GetExpiringMembershipsHandler`.
7. **Infrastructure:**
   - `CheckinAttemptConfiguration`: tabla `attendance.checkin_attempts`, índices `(gym_id, attempt_date)` y `(gym_id, acknowledged_at)`.
   - Enum `attendance.checkin_rejection_reason` (`membership_expired`, `no_membership`) con **`SnakeCaseNameTranslator`**, registrado en los tres sitios.
   - El repositorio correspondiente.

### Migración
`Etapa12_2_CheckinAttempts`: enum `attendance.checkin_rejection_reason` y tabla `attendance.checkin_attempts`.

### Aceptación (tests de integración)
- **Miembro:**
  - sin `GymCode` → 422;
  - con un código de otro gym → 422;
  - con un código rotativo de hace 2 ventanas → 422;
  - con el código fijo de la versión anterior a regenerar → 422.
- **Con `CheckinQr` apagado:** el miembro recibe 403, y el staff sigue pudiendo registrar a mano.
- **Gracia de 3 días, membresía que terminó ayer:** entra con `GraceDaysLeft = 2`.
- **Membresía que terminó hace 4 días:** 409, se crea el intento, y un segundo intento el mismo día repite la fila (`AttemptCount = 2`) en lugar de crear otra.
- **Sin ninguna membresía:** 409 con motivo `NoMembership`.
- **Con `GraceDays = 0`:** vencida ayer → 409.
- **Ya entró hoy y después venció:** 200 con `AlreadyCheckedInToday`, sin intento.
- **Staff:** registra a un miembro vencido hace 10 días sin rechazo, y la respuesta trae `MembershipExpired = true`.
- **Panel:** `pending-count` refleja los intentos pendientes; `acknowledge` y `acknowledge-all` los vacían.
- `AttendanceAndAchievementsTests` adaptado a la firma nueva de `CheckInRequest` y en verde.
- Suite completa en verde.

---

# 12.3 — Retirar el QR personal del miembro

**Objetivo:** el QR personal desaparece de la API y de la base.

### Trabajo
1. **Domain:** quitar `Member.QrCode` y su generación en `Member.Create`.
2. **Infrastructure:**
   - `MemberConfiguration`: quitar la propiedad y el índice `(gym_id, qr_code)`;
   - `MemberReader`: quitar `ResolveByQrAsync` y `QrCode` en la proyección;
   - `MemberRepository`: quitar la comprobación de unicidad del QR y lo que la use en el alta.
3. **Application:**
   - `IMemberReader`: quitar `ResolveByQrAsync`, y `QrCode` de `MemberInfo`;
   - **Cambio:** `CreateMemberResponse(Guid Id)`;
   - quitar `QrCode` de `MemberDetail` y del contrato de "mi ficha" del miembro (líneas 54 y 70 de `MemberContracts.cs`);
   - ajustar `CreateMemberHandler`.
4. **Compilar backend y tests**, y corregir todo lo que dependa de esos miembros.
5. **Regenerar el cliente** (ver Entorno). Esto rompe la compilación del frontend en `Members/Detail.razor`, `MemberQrDialog.razor`, `RecordPaymentDialog.razor` (línea ~64), `Attendance/CheckIn.razor` y `GSM.Member.UI/Pages/Home.razor`. **Arréglalo en esta sub-etapa con el mínimo imprescindible para que compile:**
   - borrar el botón "Ver QR" de la ficha y `MemberQrDialog.razor`;
   - en `RecordPaymentDialog`, dejar de copiar `QrCode`;
   - en recepción, quitar el campo de escaneo y dejar solo la búsqueda manual;
   - en la app del miembro, quitar la tarjeta del QR y el botón de autoregistro, y poner un texto provisional: "Para registrar tu entrada, escanea el QR de la entrada del gimnasio con la cámara de tu teléfono."

   El rediseño de estas pantallas llega en 12.4 y 12.5.
6. **Datos de demostración:** revisar `scripts/seed-demo.ps1`. El check-in por API manda `method`, que ahora se ignora; quítalo para que no confunda.
7. **Documentación:**
   - `GSM-especificacion-tecnica.md`: retirar `ResolveByQrAsync` de la tabla de lectores (§ ~271), `QrCode` del modelo de `Member` (~488), de `RegisterMember` (~626), del endpoint `POST /api/members` (~668) y de `CheckInMember` (~638);
   - describir el nuevo check-in en la sección de Attendance, con enlace a este plan.

### Migración
`Etapa12_3_DropMemberQrCode`: elimina el índice y la columna `members.qr_code`. La migración es **irreversible en datos** (el `Down` recrea la columna, pero con valores nuevos): dilo en el comentario de la migración.

### Aceptación
- `grep -rn "QrCode" src tests` solo devuelve `QrCodeSvg` (el componente que dibuja QR, que se mantiene para el kiosco) y nombres de iconos de MudBlazor.
- Backend, `GSM.Frontend.slnx` y `GSM.Mobile` compilan. `GSM.Mobile` se compila aparte, como indica CLAUDE.md:
  ```bash
  dotnet build src/Frontend/GSM.Mobile -f net10.0-android -p:RuntimeIdentifier=android-arm64
  ```
- Suite de integración y bUnit en verde.

---

# 12.4 — Panel del gym: kiosco, ajustes y avisos

**Objetivo:** el gym puede montar la entrada sin personal y se entera de los intentos rechazados.

El panel tiene **tres temas conmutables** (Forja, Pista y Estudio; ver la memoria del proyecto y `GSM.UI.Shared/wwwroot/gsm.css`). Todo lo nuevo usa los tokens `--gsm-*` y los componentes `PageHeader`, `gsm-panel` y `StatCard`. **Ningún color va a mano.** Compruébalo en los tres temas.

### Trabajo
1. **Configuración** (`/settings`, solo Owner): nueva pestaña **"Check-in"** con:
   - el modo (dos opciones con una línea que explique cada una: "Pantalla en la entrada: el código cambia cada 30 s" / "Cartel impreso: código fijo, regenerable");
   - los días de gracia (0–30);
   - y, solo en modo fijo, el botón **"Regenerar código"**, con un diálogo de confirmación que avise de que el cartel actual dejará de funcionar.

   El interruptor existente "Check-in por QR (app del miembro)" se renombra a **"Los miembros registran su entrada con el QR del gym"**.
2. **Kiosco** (`/kiosk`, `RequireStaff`):
   - Layout propio, `KioskLayout`: sin menú lateral ni barra superior, pantalla completa, letra grande y legible a distancia.
   - **Modo rotativo:**
     - QR grande con `QrCodeSvg`; el valor es `{NavigationManager.BaseUri}app/checkin?t={code}`;
     - nombre del gym, hora y un indicador del tiempo que le queda al código;
     - pide un código nuevo poco antes de `ExpiresAtUtc`, con un `PeriodicTimer` o similar que se libere en `Dispose`.
   - **Modo fijo:** el QR más el botón **"Imprimir cartel"**, que llama a `window.print()` con CSS de impresión (solo el QR, el nombre del gym y la instrucción "Escanea con la cámara para registrar tu entrada").
   - **Actividad en vivo**, cada ~5 s:
     - las últimas entradas de hoy: "Bienvenida, Sara · 06:42", sacadas de `GET /api/attendance?from=hoy&to=hoy`;
     - los intentos rechazados de hoy, en rojo: "Acceso denegado — pasa por recepción".
   - Si `CheckinQr` está apagado (403), mostrar el estado explicándolo, con un enlace a Configuración para el Owner.
   - Botón para pantalla completa (Fullscreen API). Opcional: Wake Lock para que la pantalla no se apague, siempre envuelto en try/catch.
   - Enlace "Abrir kiosco" desde Recepción y desde el menú (grupo Operación).
3. **Aviso de intentos rechazados:**
   - Una campana en la barra superior de `MainLayout`, con el número de pendientes. Consulta `pending-count` cada 30 s, y el temporizador se libera en `Dispose`. Necesita nombre accesible ("Intentos de entrada rechazados: 3") y una zona `aria-live="polite"` para los cambios.
   - Al pulsarla: una lista con nombre, motivo, hora y número de intentos, con enlace a la ficha del miembro, más "Marcar como vistos".
   - En el dashboard (`Pages/Home.razor`): un panel **"Intentos rechazados hoy"**, junto a "Últimos check-ins".
4. **Recepción** (`/attendance/checkin`):
   - queda como "Registro manual": búsqueda de miembro y tarjeta de resultado;
   - la tarjeta muestra la gracia ("Dentro de la gracia: le quedan 2 días") o el vencimiento, con el estilo del tema.

### Aceptación
- Verificado en el navegador, en los tres temas, a escritorio y a 375 px:
  - el kiosco rota el QR;
  - el cartel imprimible sale limpio en la vista previa de impresión;
  - la campana sube al provocar un rechazo y baja al marcarlo como visto.
- Sin errores en la consola. Sin desplazamiento horizontal a 375 px.
- Suite completa en verde.

---

# 12.5 — App del miembro (web/PWA): entrar escaneando con la cámara

**Objetivo:** el miembro apunta la cámara del teléfono al QR del gym y queda registrado.

### Trabajo
1. **Ruta `/app/checkin`** en `GSM.Member.UI` (lee `t` de la query):
   - **Sin sesión:** ir a `/login?returnUrl=/app/checkin?t=…`.
   - **Con sesión de miembro:** llamar a `POST /api/attendance/checkin` con `GymCode = t`, y **en cuanto haya respuesta quitar `t` de la URL** (`NavigateTo("/app/checkin", replace: true)`, guardando el resultado en el estado de la página), para que recargar no repita el intento.
   - **Pantalla de resultado**, grande y clara:
     - **entrada registrada:** nombre, hora y logros desbloqueados;
     - **ya habías entrado hoy;**
     - **dentro de la gracia:** "Tu membresía venció el 3 oct. Te quedan 2 días para renovar.";
     - **rechazado (409):** el `detail` de la API, más "El gimnasio ya fue avisado.";
     - **código inválido o caducado (422):** "Vuelve a escanear el código de la entrada.";
     - **check-in desactivado (403).**
   - **Con sesión de staff u owner:** un mensaje que explique que el código es para miembros.
2. **Login con `returnUrl`** (`GSM.Web/Pages/Login.razor`): tras iniciar sesión, si `returnUrl` es una ruta local (empieza por `/`, no por `//`, y no trae esquema), navegar ahí en lugar de a `GsmRoles.HomeFor(role)`. Cualquier otro valor se ignora: así no se abre una redirección a sitios externos. Añade un test bUnit o unitario de esa validación.
3. **Inicio del miembro** (`/app`): sustituir el texto provisional de 12.3 por una tarjeta "Registrar entrada" que explique el gesto ("Escanea el QR de la entrada con la cámara de tu teléfono"). Si `IQrScanner.IsSupported` (llega en 12.6), mostrar también el botón **"Escanear QR"**.
4. Definir en `GSM.Member.UI` la interfaz `IQrScanner { bool IsSupported { get; } Task<string?> ScanAsync(CancellationToken) }` y registrar en `GSM.Web` una implementación `UnsupportedQrScanner` (`IsSupported = false`).

### Aceptación
- Verificado en el navegador con un miembro sembrado (las cuentas están en la cabecera de `scripts/seed-demo.ps1`):
  - abrir el enlace que genera el kiosco registra la entrada;
  - abrirlo sin sesión pasa por el login y vuelve;
  - recargar no repite el intento;
  - un miembro vencido fuera de la gracia ve el rechazo, y el aviso aparece en el panel.
- Un `returnUrl=https://otro-sitio` se ignora.
- Suite completa en verde.

---

# 12.6 — App nativa (MAUI): escáner dentro de la app

**Objetivo:** en la app nativa, el miembro pulsa "Escanear QR" y la cámara lee el código sin salir de la app.

**Por qué hace falta:** hasta que haya un dominio de producción con App Links verificados, un enlace escaneado con la cámara del sistema abre el **navegador**, no la app nativa.

### Trabajo
1. En `GSM.Mobile`, implementar `IQrScanner` con una librería de lectura de códigos compatible con **.NET 10 MAUI**. Primera opción: `ZXing.Net.Maui.Controls`. **Comprueba la compatibilidad antes de añadirla**; si no la tiene, busca una alternativa mantenida y explica la elección.
   - La implementación abre una página nativa modal con la vista de cámara y devuelve el primer texto leído, o `null` si el usuario cancela.
   - Registrarla en `MauiProgram`, sustituyendo a la no soportada.
2. **Permisos:**
   - Android: `CAMERA` en `Platforms/Android/AndroidManifest.xml`, pedido en tiempo de ejecución con `Permissions.Camera`;
   - Windows: la capacidad `webcam` en `Package.appxmanifest`.
   - Si el usuario niega el permiso: un mensaje claro con la opción de reintentar.
3. **En `/app`:** el botón "Escanear QR" extrae `t` del enlace leído (o acepta el código suelto si no es un enlace) y navega a `/app/checkin?t=…`, reutilizando todo el flujo de 12.5.

### Aceptación
- `GSM.Mobile` compila para Android y Windows.
- **No verificable en este entorno:** no hay SDK de Android ni emulador (ver CLAUDE.md). Dilo explícitamente al cerrar y deja anotado en CLAUDE.md que falta probarlo en un dispositivo.

---

## Al cerrar la etapa completa

1. **`CLAUDE.md`:**
   - estado actual (etapa 12 hecha);
   - la nota de que el check-in del miembro exige el código del gym, con la regla de gracia;
   - que el QR personal ya no existe;
   - que el kiosco usa una sesión de staff;
   - la clave `Checkin:SigningKey` pendiente de configurar en producción;
   - la fila del plan en la tabla de módulos.
2. **`README.md`:** `Checkin:SigningKey` en "Pendiente de configurar", y cómo abrir el kiosco en local.
3. **Este documento:** cambiar el estado a "completado", con las desviaciones que haya habido.
4. **Pendientes que quedan anotados, no construidos:**
   - cuenta de dispositivo para el kiosco;
   - avisos por email o push de los intentos rechazados (el evento `CheckInRejected` ya existe para eso);
   - App Links verificados en producción para que el enlace abra la app nativa;
   - control de ubicación (geocerca), si algún gym lo pide.
