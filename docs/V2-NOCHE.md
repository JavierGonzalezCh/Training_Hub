# Bitácora de la noche — construcción de la v2

> Integrador autónomo (Opus) sobre la rama `v2`, empezada el 2026-10-07 por la noche.
> Implementadores: agentes Sonnet en worktree, uno por sub-etapa (`docs/V2-BUILD-PLAN.md` §5).
> `master` no se toca. Solo se crean/borran bases `gsm_test_*` y `gsm_dev_*`.

## Carriles, bases y puertos

| n | Carril | Base de tests | API / Web |
|---|---|---|---|
| 0 | Cimientos (X0, AP1, LG1, T1) | `gsm_test_x0`, `gsm_test_ap1`, `gsm_test_lg1`, `gsm_test_t1` | 7300 / 7301 (cada tarea de cimientos usa la suya) |
| 1 | Identidad | `gsm_test_identidad` | 7310 / 7311 |
| 2 | Miembros | `gsm_test_miembros` | 7320 / 7321 |
| 3 | Rutinas / Catálogo | `gsm_test_rutinas` | 7330 / 7331 |
| 4 | Pagos | `gsm_test_pagos` | 7340 / 7341 |
| 5 | Avisos | `gsm_test_avisos` | 7350 / 7351 |
| 6 | Apps | `gsm_test_apps` | 7360 / 7361 |
| 7 | Plataforma | `gsm_test_plataforma` | 7370 / 7371 |
| 8 | Membresías | `gsm_test_membresias` | 7380 / 7381 |
| 9 | Gyms | `gsm_test_gyms` | 7390 / 7391 |
| 10 | Comunicaciones | `gsm_test_comunicaciones` | 7400 / 7401 |
| 11 | Puerta | `gsm_test_puerta` | 7410 / 7411 |
| 12 | Clases | `gsm_test_clases` | 7420 / 7421 |
| 13 | Logros | `gsm_test_logros` | 7430 / 7431 |
| 14 | Logs | `gsm_test_logs` | 7440 / 7441 |

## 1. Resumen

| Tarea | Estado | Notas |
|---|---|---|
| X0 | **Unida** (b2249ce) | 162 integración + 68 bUnit en verde |
| AP1 | **Unida** (269d85d) | Verificada en el navegador: login, dashboard, miembros, configuración, clases y pagos cargan sin errores de consola |
| LG1 | **Unida** (522275b + 8a09bb2) | Ver preguntas 1–3 (para LG4). Hizo falta un arreglo al unir (ver §4) |
| R1 | **Unida** (7de0916) | Corrige B1. Suite 183 + 74 en verde |
| T1 | **Unida** (f62d2f5) | 67 archivos; no había datos sembrados con "socio". **Ola 0 cerrada**; `CLAUDE.md` actualizado |
| N1 | **Unida** (41b55e1) | Ver preguntas 7–8. Snapshot y Designer rehechos al unir |
| I2 | **Unida** | `GymAccess`, selector y cambio de gym, invitaciones que reconocen cuentas. Sin verificar en navegador ni en Android |
| P1 | **Unida** (12e6e24) | 8 métodos; el JSON los saca en PascalCase (`BankTransfer`), como el resto de enums. Suite 186 + 80 |
| M1 | **Unida** | Ver pregunta 9. Snapshot rehecho al unir |
| R2 | **Unida** (merge de R2) | Suite 198 + 87. Ver pregunta 6 |
| R3 | **Unida** (174ff30) | Ver pregunta 10 (clasificación del catálogo). Suite 260 + 116 |
| S5 | **Unida** | Índice único parcial; la migración aborta si hay más de un superadmin (no elige cuál queda). El seeder de desarrollo falla si ya hay otro superadmin con otro email. Suite 262 + 116 |
| S7 | **Unida** | Ver pregunta 13. Se colgó una vez esperando un proceso; reanudada sin pérdida |
| I3 | **Unida** (b3d2802) | Perfil como enum fijo; invitar sin perfil = Recepción. Ver pregunta 11. Suite 272 + 123 |
| MS1 | **Unida** | Gracia y "por vencer" en `memberships.membership_settings`; `AccessStatus`; "Dejar pasar" con motivo; renovación con vista previa; estado comercial en lista, ficha, recepción y dashboard. Ver preguntas 14–16. Suite 300 + 141 |
| AP2 | **Unida** (9cf3061) | **Verificada en el navegador** a 1280 y 375 px: menú igual en escritorio, barra inferior, hoja "Más", diálogo a pantalla completa. También verificado el login de I2. Suite 272 + 134 |
| S6 | **Unida** | Modos `read`/`write`, firma "GSM", auditoría por petición. Ver pregunta 17. **Ola 1 cerrada**; suite 309 + 144; `CLAUDE.md` actualizado |
| B3 | **Unida** | Causa: nada fijaba la cultura y el selector no tenía `ToStringFunc`. Marcado corregido en `BUGS.md`. bUnit 135 |
| G2 | **Unida** | Sin mapa con pin (solo lat/long y "Ver en el mapa"). Suite 327 + 150 |
| E1 | **Unida** | 55 modelos sembrados (lista en el commit del agente); sin imágenes (ícono por tipo); foto del inventario sin verificar con R2. Ver pregunta 18. Suite 354 + 162 |
| G3 | **Unida** | Festivos 2026 verificados contra el calendario oficial. Ver pregunta 21 |
| CO3 | **Unida** | Botón de WhatsApp en ficha, recepción y listas; plantillas por gym. `window.open` sin verificar en MAUI |
| M2 | **Unida** | Texto base de las autorizaciones = **borrador pendiente de revisión legal**. Ver pregunta 20 |
| N2 | En curso | sin Firebase: se construye detrás de `IPushSender`, sin verificar con el servicio real |
| A1 | En curso (Ola 3) | — |
| C1 | En curso (Ola 3) | — |
| P2 | En curso (Ola 3) | — |
| N4 | **Unida** (a99b982) | Preferencias por categoría y canal, horario sin avisos, categoría del anuncio (CO1). Ver pregunta 22. Suite 393 + 187 |
| I1 | **Unida** | Config nueva `Identity:PublicWebBaseUrl` (producción). Suite 343 + 156 | los correos de cuenta siguen por el camino de Identity (moverlos a Notifications queda pendiente) |

Línea base antes de empezar: 160 integración + 68 bUnit, todo verde.

## 2. Preguntas para el dueño

Ninguna bloquea trabajo de esta noche; todas afectan a tareas futuras.

1. **[DECIDIDA 2026-10-11: (c) el reintento pide el dato vigente al módulo dueño; GSM-logs.md §4.3 actualizado.]** **LG1 → LG4: ¿se pueden reintentar los eventos con campos sensibles?** `GSM-logs.md` §3 manda limpiar `code`, `token`… antes de guardar, y §4.3 pide guardar el evento completo "para poder reintentarlo". Choque: `MemberInvited.Code` queda `[REDACTED]`, y reintentarlo enviaría un correo sin código. Opciones: (a) esos eventos se marcan como no reintentables en el panel; (b) el payload de `event_handler_failures` no se limpia de códigos de un solo uso (sí de contraseñas y salud); (c) el reintento vuelve a leer el dato del módulo dueño en lugar del payload.
2. **[DECIDIDA 2026-10-11: (a) se acepta; búsqueda por prefijo.]** **LG1 → LG4: código de rastreo corto.** El `traceId` es de 32 hex y el código que ve el usuario son sus 6 primeros caracteres (§3: "código 7F3A2C"). Dos peticiones pueden compartir el código corto, así que la búsqueda en LG4 va por prefijo y puede devolver más de una. Opciones: (a) aceptarlo (colisiones rarísimas a este volumen); (b) generar un código corto único aparte.
3. **[DECIDIDA 2026-10-11: se acepta el tope de 64 KB.]** **LG1: tope del payload de eventos.** El agente no recortó el payload a 4 KB (un JSON cortado no se puede reintentar); el tope es 64 KB y por encima se guarda `{"_truncated":true}`. Aceptado provisionalmente; confirmar.
4. **[DECIDIDA 2026-10-11: (a) 409 aceptado; nace la tarea R12 (desactivar ejercicio con reemplazo masivo, motivo y aviso; quitar de una rutina también avisa). (b) el historial de pesos va atado al ejercicio (`exercise_id`) y lo acompaña al moverlo de día.]** **R1: registrar sobre una línea retirada.** La spec no lo dice; el agente respondió **409**. Y mover una línea de un día a otro conservando su id se rechaza con 422 (hay que retirar y crear). Confirmar ambas.
7. **[DECIDIDA 2026-10-11: los avisos del miembro se purgan a los 90 días, leídos o no; los del staff sin atender se conservan. Hecho, con test.]** **N1: purga de avisos.** La spec dice "los avisos leídos de más de 90 días se borran (la regla actual de Comunicaciones)", pero la regla actual borraba **todo** lo de más de 90 días. Se siguió la spec: un aviso **sin leer nunca se purga**. ¿Correcto, o se purga todo lo viejo?
8. **[DECIDIDA 2026-10-11: (a) los anuncios no entran en la bandeja; se agrega el "anuncio importante" (solo Owner, se salta horario y preferencias, fijo en el feed) dentro de N2.]** **N1: anuncios y bandeja del miembro.** Cada anuncio crea un aviso por miembro (para el correo y el push futuro), pero **no aparece en la bandeja ni en el contador del miembro**, porque ya vive en el feed con su propio contador y se contaría doble. ¿Se unifican en N4?
9. **[DECIDIDA 2026-10-11: se aprueban las cuatro; el filtro por etiqueta ignora mayúsculas (hecho, con test).]** **M1: reglas que puso el agente.** (a) `JoinedAt` en el futuro → 422; (b) una ficha que ya tiene fecha de nacimiento no puede quedar sin ella → 422; (c) etiquetas: se guardan como se escriben, sin duplicados ignorando mayúsculas, el filtro es exacto (sensible a mayúsculas), máximo 20 de 40 caracteres; (d) "ficha incompleta" se deriva (falta fecha de nacimiento o el teléfono no es E.164), no se guarda.
10. **[DECIDIDA 2026-10-11: se aprueba la clasificación; Paseo del granjero medirá peso + distancia, PENDIENTE: tarea R3b.]** **R3: clasificación de los 58 ejercicios del sistema** (tipo, qué se mide, unilateral, nivel). La propuso el agente; está en la migración `V2_R3.sql`. Revisar sobre todo: Paseo del granjero = distancia; Salto a la cuerda = cardio/tiempo con músculo pantorrillas; Escaladores = cardio/tiempo; unilaterales: remo a una mano, patada de tríceps, zancadas, sentadilla búlgara, patada de glúteo, subida al cajón. Además: duplicar nombra la copia "{original} (copia)"; un ejercicio oculto sigue siendo asignable por id desde la API (solo sale del buscador y del editor); el SuperAdmin recibe 403 al ocultar/duplicar (no tiene gym).
11. **[DECIDIDA 2026-10-11: (b) access token de 15 min; también cubre la 19b.]** **I3: un permiso quitado sigue vivo hasta 60 minutos.** Quitar un permiso revoca los refresh tokens de ese acceso (§5.3), pero el access token ya emitido vale hasta 60 min porque no hay lista de revocación. La spec dice "un permiso sensible no puede seguir activo una hora después de quitarlo": hoy queda justo en el límite. Opciones: (a) aceptarlo; (b) bajar la vida del access token (p. ej. 15 min); (c) comprobar el permiso contra la base en los endpoints sensibles (salud, finanzas, anulaciones).
12. **[DECIDIDA 2026-10-11: (a) sin página de avisos; basta la campana.]** **AP2: "Avisos" en "Más".** El plan pone "Avisos" en la hoja "Más" del celular, pero los avisos son la campana (siempre visible arriba); no hay página de avisos, así que no aparece. ¿Hace falta una página?
13. **[DECIDIDA 2026-10-11: churn solo de clientes activos al inicio; "pruebas no convertidas" aparte (columna `trials_not_converted`, migración V2_P13). Hecho, con test.]** **S7: definición de churn.** Se usó la de la spec (gyms perdidos ÷ activos al inicio del mes). Un gym en prueba que nunca pagó y se inhabilita cuenta como perdido pero no estaba en los activos, así que el churn podría pasar del 100 %. ¿Contar solo los perdidos que eran activos al inicio? Además: "activos" = al día + en mora; "convertidos de prueba a pago" es aproximado (no hay historial de pagos que diga cuál fue el primero); para saber el estado de cada gym al inicio del mes, cada fila guarda una foto de los estados al cierre (`gym_states`).
14. **[DECIDIDA 2026-10-11: las canceladas se ignoran para el estado; spec corregida.]** **MS1: membresía cancelada.** La spec dice "la última fue cancelada → `no_plan`". El agente ignora siempre las canceladas, así que *cancelada + una vencida anterior* da `expired`, no `no_plan`. ¿Cuál es la regla?
15. **[DECIDIDA 2026-10-11: estado nuevo `scheduled` (programada); spec §10 actualizada. PENDIENTE de construir: tarea MS1b.]** **MS1: membresía que empieza en el futuro.** Un miembro cuya única membresía empieza mañana queda `no_plan` y bloqueado hoy. La spec no lo define. ¿Debería tener otro estado (p. ej. "programada")?
16. **[DECIDIDA 2026-10-11: corregido ya; /kiosk solo muestra el QR, "Bienvenido" y "¿Problemas para ingresar?".]** **MS1/A3: el kiosco todavía muestra nombres.** `/kiosk` sigue mostrando los nombres de pila de las últimas entradas y de los intentos rechazados; la spec (Memberships §5.3) dice que solo muestra el QR, "Bienvenido" y "¿Problemas para ingresar?…". Se dejó para A3 (kiosco con cuenta de dispositivo). ¿Correcto, o se corrige antes?
17. **[DECIDIDA 2026-10-11: (a) restringido; /api/admin/* da 403 con el token de impersonación salvo impersonate/end. Hecho, con test.]** **S6: un token de impersonación en modo escritura también sirve para `/api/admin/*`** (su rol real sigue siendo superadmin; antes de S6 solo pasaba con GET). Es la misma persona y queda auditado. ¿Se restringe el token de impersonación a los endpoints del gym?
18. **E1: nombres de las unidades y catálogo.** Agregar "Bicicleta estática × 3" crea "Bicicleta estática 1…3" (la spec decía "Bicicleta 1…3" sin decir cómo acortar). Se sembraron 55 modelos (la spec decía ~40). El listado con bajas (`includeInactive`) no está restringido al Owner en la API, aunque la UI solo lo ofrece al Owner.
19. **I1: activación y sesiones.** (a) Cada "Copiar enlace" genera un enlace nuevo y no invalida los anteriores; todos mueren cuando la persona define su contraseña. (b) Cambiar la contraseña cierra las otras sesiones revocando sus refresh tokens, pero sus access tokens viven hasta 60 min (relacionado con la pregunta 11). (c) El alta del Owner por SuperAdmin todavía pide contraseña; la invitación por enlace del Owner llega con G1.
20. **[DECIDIDA 2026-10-11: se mantiene lo del agente — una versión nueva no cierra el candado; consultar con el abogado si algún cambio exige reaceptar (opción futura "exige volver a aceptar").]** **M2: qué cuenta como autorización de datos vigente.** El agente la definió como "la aceptación más reciente, de cualquier versión, no revocada": publicar un texto nuevo deja a la persona "pendiente de aceptar la versión vigente" pero **no** cierra el candado de salud y medidas. ¿O debe cerrarlo hasta que acepte la versión nueva? Además: un menor no puede aceptar en la app (lo hace el acudiente en recepción); la exoneración no se puede revocar (422).
21. **[DECIDIDA 2026-10-11: (a), (b) y (d) se aceptan; (c) se resuelve con la casilla "Abierto 24 horas" por día, PENDIENTE de construir: tarea G3b.]** **G3: decisiones del agente.** (a) Un rechazo por `gym_closed` **no** crea intento rechazado ni aviso en la campana (no es accionable). (b) La asistencia por clase se valida con el gym abierto **a la hora de inicio de la clase**, no a la hora en que se marca. (c) Un tramo cierra como máximo a las 23:59: no se puede expresar un gym 24 h. (d) Las excepciones solo pueden ser de hoy en adelante.
22. **[DECIDIDA 2026-10-11: solo Novedades y Promociones; logros y penalizaciones salen siempre. Notifications §9 corregido.]** **N4: el horario sin avisos solo retiene Novedades y Promociones** (Comunicaciones §G). El catálogo de Notifications §9 marca "Aplica" también para logros y penalizaciones; hay contradicción entre los dos docs. Hoy no cambia nada (esos avisos no tienen correo ni push), pero hay que decidirlo antes de N2. Además: el `Down` de la migración de N4 no se puede ejecutar (Postgres no quita el valor `skipped` del enum).
6. **[DECIDIDA 2026-10-11: (a), (b) y (c) se aceptan; el modo es de la rutina.]** **R2: decisiones del agente para validar.** (a) Cambiar el modo (semanal ↔ rotativa) por `PUT` crea la rutina nueva, desactiva la anterior y **reinicia `starts_on` a hoy**; (b) en semanal no hay filas de descanso y mandar `dayOfWeek` en una rotativa es 422; (c) en semanal, "duración del ciclo" se muestra como "N días de entrenamiento".
5. **[DECIDIDA 2026-10-11: cada día se juzga con la rutina vigente ese día; PENDIENTE de construir: tarea R2b.]** **R1 → R2: racha y días retirados.** Al retirar un día, deja de contar en el calendario de racha **también hacia atrás** (el calendario solo ve los días vigentes). Para el modo semanal (R2) conviene decidir si la racha se calcula con el calendario vigente en cada fecha.

## 3. Gestiones operativas pendientes

Las del plan §4 (ninguna se hace esta noche):

- Servidor y ambiente de pruebas con Dokploy.
- Cuenta de Cloudflare R2.
- Cuenta de Resend y dominio verificado.
- Proyecto de Firebase por ambiente.
- Teléfono Android o emulador (AP3, N3, A6, escáner QR).
- Imágenes de ejercicios y máquinas con licencia.
- Revisión legal (Ley 1581, Ley 2300).

## 4. Riesgos y desvíos aceptados al unir

- **Nombres de rama:** el plan §2.4 propone `v2/<id>-<nombre>`, pero Git no admite `v2/...` si existe la rama `v2`. Se usa **`v2-<id>-<nombre>`**. Conviene corregir §2.4 del plan.
- **X0, enums por módulo:** en lugar de `GsmDbContext.{Módulo}.cs` con el mapeo de enums, cada módulo declara sus enums en `Infrastructure/{Módulo}/{Módulo}Enums.cs` (`IModuleEnums`), descubiertos por reflexión; una sola fuente para el modelo, el proveedor y el factory de diseño. Solo hay un parcial (`GsmDbContext.Identity.cs`) porque solo había un `DbSet`. La migración de comprobación salió vacía.
- **S5 depende de I2, no van en paralelo:** el plan pone S5 en la Ola 1 junto a I2, pero S5 (`AppUser.IsSuperAdmin` único con índice parcial) usa el booleano `IsSuperAdmin` que **introduce I2** (`GSM-modulo-identity.md` §3: la cuenta deja de llevar `Role`). Hoy el SuperAdmin es `UserRole.SuperAdmin`. S5 se lanza después de unir I2.
- **Tareas de la Ola 1 con pantallas del panel** (P1, I2, N1, AP2) esperan a que AP1 esté unida, para no chocar con la mudanza a `GSM.Panel.UI`.
- **`.gitignore` ignoraba las carpetas `Logs/`** (regla `[Ll]ogs/` de la plantilla de Visual Studio): el código de LG1 en `Application/Common/Logs` e `Infrastructure/Logs` no entró en la rama del agente. Se agregó la excepción `!src/**/[Ll]ogs/` y se copiaron los archivos (8a09bb2).
- **`scripts/generate-client.ps1` exigía `pwsh`** (`-SkipCertificateCheck`); en esta máquina solo hay Windows PowerShell 5.1. Ahora funciona en los dos.
- **R1:** el índice `(routine_id, position)` de `routine_days` deja de ser único (reordenar días chocaba fila por fila; el dominio garantiza 1..N entre los vigentes). Los tests viejos que borraban rutinas ahora borran antes los registros (por `Restrict`). No existe DELETE de rutinas, así que `Restrict` no rompe ningún flujo.
- **M1 incluye las notas internas (`MemberNote`, §3.3 de Members)** aunque ninguna sub-etapa las nombra: la migración de §9 copia el contacto de emergencia viejo a una nota interna, así que hacen falta en M1.
- **P1:** el enum `PaymentMethod` cambia su numeración (0..7 en el orden de la spec); los filtros por entero del cliente se actualizaron con él. En la base viaja por nombre, así que no afecta a los datos.
- **Migraciones al unir (§2.3):** cuando el snapshot chocó (N1, M1, R3) **no** se regeneraron las migraciones de los agentes, porque las tres tienen `Up` escrito a mano con un orden que importa (N1: crear tipos → tablas → copiar → soltar viejas; M1: `AddColumn` → `SqlScript` → `DropColumn` para no perder el contacto de emergencia; R3: anotaciones del enum `muscle` para que EF no emita `ADD VALUE`). En su lugar: snapshot regenerado desde el modelo unido (comprobado con una migración temporal cuyo contenido era exactamente lo de la tarea) y Designer de la migración reescrito. Variante del §2.3 que conviene documentar en el plan.
- **N1 retiró los enums viejos de Comunicaciones** (`EmailKind`, `EmailStatus`, `NotificationType` de `communications`): las migraciones históricas usan copias `Legacy*` en `Migrations/LegacyCommunicationsEnums.cs`.
- **Interferencias semánticas al unir** (no son choques de texto): los tests de P1, R2, R3 y M1 sembraban `AppUser` con `Role`/`GymId` o leían el login viejo; se adaptaron a I2.
- **Riesgo: un test de integración falla de forma esporádica** (1 de cada ~5 corridas completas tras unir N1); no se pudo aislar todavía. Las corridas repetidas pasan.
- **Incidente — la base de desarrollo `gsm` quedó migrada a la v2.** Dos agentes (CO3 y M2) corrieron `dotnet ef database update` sin `--connection`, y esa herramienta apunta a `gsm` (lee `GSM_MIGRATIONS_CONNECTION`, no `ConnectionStrings__Postgres`). Cada uno revirtió su propia migración, pero las anteriores quedaron aplicadas: **`gsm` está en `V2_G2_GymProfile`**, con sus datos (43 miembros, 24 usuarios, 23 accesos creados por la migración de I2). No se borró nada; M2 avisa que, al pasar por `V2_S7` hacia atrás y adelante, una tabla vacía de métricas se recreó. Para usar `gsm` con la `v2` hay que aplicar las migraciones que faltan (`dotnet ef database update`). Desde entonces los prompts exigen `--connection` explícito.
- **Worktrees:** los agentes trabajan en worktrees creados por la herramienta (`.claude/worktrees/`, ignorado por git), no en `../gsm-lanes/`. `scripts/new-lane.ps1 -SkipWorktree` sirve para crear solo las bases.

## 5. Registro cronológico

- Línea base: 160 + 68 en verde.
- Lanzadas T1, I2, P1 y R2 (4 agentes).
- Lanzadas N2 (cierra la Ola 2) y A1, C1, P2 (Ola 3). Los prompts ahora exigen `--connection` en `dotnet ef`.
- CO3, M2, G3 y N4 unidas (393 + 187). Cada unión tuvo choques de "ambos agregaron algo" (lectores de Members, `ReadResponseAsString`, inicio de la app, lista de pestañas) y el alias `EquipmentType` en Designers anteriores a E1; snapshot regenerado en CO3 y G3; cliente regenerado en cada una. **De la Ola 2 solo falta N2 (push web).**
- Reanudación tras el tercer corte por límite de uso. I1 unida (343 + 156; conflicto en `MemberLayout`, cliente regenerado). E1 unida (354 + 162; hubo que poner el alias `EquipmentType` en los Designers de S6, I1 y G2). Lanzadas G3 y CO3; M2 reanudada.
- G2 unida (327 + 150). I1 terminada sin unir; E1 y M2 en curso. Sesión cortada por el límite de uso.
- S6 unida (309 + 144): **Ola 1 cerrada**. Lanzada M2. Cliente regenerado (idéntico al unido, solo cambia `openapi-v1.json`). `CLAUDE.md` actualizado con las notas de I2, I3, N1, MS1, AP2 y S6.
- S7 y MS1 unidas (300 + 141). En MS1 hubo que combinar a mano el diálogo de asignar con el arreglo de B3. Lanzadas G2 y E1.
- B3 unida. Lanzada I1.
- I3 y AP2 unidas (272 + 134); AP2 y el login de I2 verificados en el navegador con una base temporal `gsm_dev_integ` (ya borrada). Lanzadas S6 y B3.
- Segundo corte por límite de uso (reinicio 8:00): I3, MS1, AP2 y S7 quedaron a medias. Reanudadas por la mañana; el worktree de S7 se había perdido sin nada commiteado y se rehace desde cero.
- Lanzadas I3, MS1, AP2 y S5. S5 unida (262 + 116). Lanzada S7.
- I2, N1, M1 y R3 unidas (en ese orden). Cliente regenerado desde la API unida: idéntico al unido salvo `openapi-v1.json`; el parche de `DaysOfWeek` del script fallaba con CRLF y se arregló. Suite 260 + 116.
- R2 unida (conflicto trivial en un comentario de `RoutineForm.razor`); 198 + 87. Incidente: el agente de R2 corrió una vez sus tests contra `gsm_test` (sin la variable), aplicándole V2_R2 y truncando tablas de prueba; sin daño (la migración quedó igual en `v2`). Lanzada R3.
- P1 unida (186 + 80). Lanzada M1.
- Los cuatro agentes en curso (I2, P1, R2, N1) se cortaron por el límite de uso de la API (reinicio 21:50); reanudados con su contexto y su worktree intactos.
- T1 unida (183 + 74). **Ola 0 cerrada**; `CLAUDE.md` actualizado (§6.3). Lanzada N1. Decisión de alcance del integrador: N1 no mueve los correos de cuenta de Identity (los toca I1) para no chocar con I2.
- AP1 unida y verificada en el navegador. LG1 unida (con arreglo del `.gitignore`); 175 + 73. R1 unida; 183 + 74. B1 marcado corregido en `BUGS.md`.
- X0 unida (b2249ce); suite 162 + 68 en verde. Lanzadas AP1 y LG1 en paralelo (`gsm_test_ap1`, `gsm_test_lg1`).
- Inicio. Leídos `CLAUDE.md`, `docs/V2-BUILD-PLAN.md`, `docs/BUGS.md`. Creada `gsm_test_x0`.
