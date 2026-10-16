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
| N2 | **Unida** (67304c3) | Sin Firebase: detrás de `IPushSender`. Promoción no puede ser importante (añadido al unir) |
| A1 | Pendiente (va en serie con MS1b, G3b y C1) | — |
| C1 | **Unida** (rama `v2-C1-clases-horario-b`) | Horario del gym en crear/editar (422), reservar y asistir (409), cobertura del día de la sesión, cancelación perezosa, clases fuera de horario. Ver preguntas C1 |
| M3 | **Unida** | Salud en `GET/PUT .../health` con `ViewMemberHealth`, registro de consultas, alerta en lista y ficha. Ver preguntas M3 |
| I5 | **Unida** | `/app/entrar`: código de 6 dígitos por correo (hash, 10 min, 5 intentos), marca del gym por slug. Ver preguntas I5 |
| P3 | **Unida** | Caja por turno con arqueo, movimientos, anulaciones en efectivo, reporte imprimible, cierre diario ampliado. **Corrige B2**. Ver preguntas P3 |
| P2 | **Unida** (07be3c8) | Rama `v2-P2-permisos-pagos-b`. Nota al rechazar añadida al unir |
| N4 | **Unida** (a99b982) | Preferencias por categoría y canal, horario sin avisos, categoría del anuncio (CO1). Ver pregunta 22. Suite 393 + 187 |
| I1 | **Unida** | Config nueva `Identity:PublicWebBaseUrl` (producción). Suite 343 + 156 | los correos de cuenta siguen por el camino de Identity (moverlos a Notifications queda pendiente) |
| E2 | **Unida** (7f34d95) | Estado, historial, vencida, `IEquipmentReader` (nivel 1 sin conectar: E3). Alargar el arreglo añadido al unir |
| I7 | **Unida** (d9e368b) | Cuenta `kiosk` con código de emparejamiento; sesión sin vencimiento hasta revocar |
| A1 | **Unida** | Recepción en dos pasos (tarjeta), una entrada por día (409, intento `already_checked_in` sin campana), motivos `membership_frozen`/`pass_exhausted`/`member_inactive`. Migración V2_A1_M1b |
| M1b | **Unida** | Entrenador asignado validado contra `Trainer`; se limpia al perder el permiso o desactivar la cuenta |
| C5 | **Unida** | Fecha de fin inclusiva de la serie. Ver preguntas C5 |
| A1b | **Unida** | Regla de entradas múltiples por miembro (2–5, 2 h entre entradas); el índice por miembro y día deja de ser único. Ver preguntas A1b |
| M4 | **Unida** | Mediciones de peso/altura con IMC, protegidas con `ViewMemberHealth`. Ver preguntas M4 |
| I6 | **Unida** | Cambio de email con código; Members sincroniza las fichas por `UserEmailChanged`. Ver preguntas I6 |
| A2 | **Unida** | Anular entradas (`ManageAttendance`, motivo, 7 días, `CheckInVoided`); lo anulado deja de contar. Ver preguntas A2 |
| S2 | **Unida** | Planes del SaaS (`saas_plans`, Basic 150 / Pro 500 / Enterprise), límite propio por gym, bloqueo de membresías nuevas, uso en panel y `/admin`. Ver preguntas S2 |
| C3 | **Unida** | Plazo de cancelación, ventana, máximo de reservas, penalización por inasistencias con levantamiento. Ver preguntas C3 |
| R4 | **Unida** | Series una por una (JSON), superseries, registro por serie, planeado vs hecho. Migración irreversible en datos. Ver preguntas R4 |
| CO7 | **Unida** | Recordatorios N días antes, el día que vence y el último día de gracia; sin migración. Ver preguntas CO7 |
| C2 | **Unida** | Instructor de la lista de entrenadores u "Otro", "Mis clases" (`Trainer`), marca "sin entrenador activo". Ver preguntas C2 |
| E3 | **Unida** | Ligas ejercicio↔modelo (sistema) y ejercicio↔máquina (gym), nivel 1 "solo lo que se puede hacer", avisos en el editor y en la app. Ver preguntas E3 |
| N5 | **Unida** | Avisos programados (recordatorio de clase 10 min antes, al instructor, cumpleaños listo para CO5); vencimientos de madrugada salen a las 8:00. Ver preguntas N5 |
| CO2 | **Unida** | Audiencias por segmento (estado, plan, clase, etiqueta), destinatarios fijados al publicar, feed filtrado. Ver preguntas CO2 |
| CO5 | **Unida** | Cumpleaños por la tarea diaria (`BirthdayGreetingDue`, 8:00, apagado por defecto), 29-feb → 28-feb, aviso en recepción y dashboard. Suite 668 + 329. Ver preguntas CO5 |
| G1+S1 | **Unida** | Owner invitado sin contraseña, reenviar acceso, prueba opcional o primer pago. Falta S1b (fin de la prueba) |
| G3b | **Unida** | Día 24 h = tramo 00:00–23:59 guardado, sin migración. Ver preguntas G3b |
| R2b | **Unida** | `routine_days.added_on` (migración `V2_R2b_RoutineDayAddedOn` + `SqlScript("R2b")`); `IRoutineReader.GetCalendarStartAsync`. Ver preguntas R2b |
| MS1b | **Unida** | Estado `scheduled` calculado; sin migración. Ver preguntas MS1b |
| L1 | **Unida** | Reglas heredadas cubiertas por tests; otorgamiento retroactivo con vista previa (2 endpoints solo-Owner). Suite 691 + 334. Ver preguntas L1 |
| R3b | **Unida** | `weight_distance`; Paseo del granjero la usa. Dos migraciones: el enum en `V2_R3b_N1b_S1b`, el `UPDATE` en `V2_R3b_LG2_Scripts` |
| N1b | **Unida** | `notifications.required_permissions` (`text[]`); campana, contador y push filtrados por permiso. Ver pregunta N1b |
| S1b | **Unida** | `trial_ended` derivado, `trial_grace_days` (3) por gym; tras la gracia, `disabled` (antes 15 días en `overdue`). Ver preguntas S1b |
| LG2 | **Unida** | `business_rejections`, `security_events`, `slow_requests` (particionadas por mes). Suite 722 + 339. Ver preguntas LG2 |
| A3 | **Unida** | `/kiosk` solo con la cuenta de dispositivo (`RequireKiosk`), emparejamiento en `/kiosk/emparejar`, sin nombres; sin migración. Ver preguntas A3 |
| MS2 | **Unida** | Precio acordado con motivo, matrícula por gym (primera membresía), un solo cobro por el total desde el diálogo. Ver preguntas MS2 |
| LG3 | **Unida** | `external_failures` y `client_errors`; `POST /api/logs/client` anónimo (20/min, 40 KB); borde de errores en los layouts. Android sin compilar. Ver preguntas LG3 |
| I4+G4 | **Unida** | Varios Owners, ascender, quitar (nunca el último, candado), traspaso en dos llamadas, contacto de facturación. Suite 756 + 367. Ver preguntas I4 |
| MS3 | **Unida** | Planes en días o meses del calendario (`duration_unit`, `duration_value` renombrada), `cycle_day` para no perder días en cadena. Ver preguntas MS3 |
| A4 | **Unida** | Entrada con documento en el kiosco (ajuste `by_document`, apagado), rechazo genérico, bloqueo de 5 fallos / 1 min con `document_entry_locked`. Ver preguntas A4 |
| R5 | **Unida** | Alternativas recomendadas (tabla + ~31 pares sembrados) y sugeridas; "Cambiar por…" y "Hacer otro ejercicio hoy". Ver preguntas R5 |
| LG4 | **Unida** | `/admin/logs`: grupos con estado, búsqueda por código (prefijo ≥ 4), reintento de eventos (sin refrescadores aún). Suite 800 + 390. Ver preguntas LG4 |
| A5 | **Unida** | Ocupación (personas distintas, 2 h configurables) en dashboard y `/app`. **Corrige B4**. Ver preguntas A5 |
| L2 | **Unida** | Logros `tenure_months` y `referrals`, evaluados en check-in y por eventos de membresía; 2 logros por defecto más. Ver preguntas L2 |
| C4 | **Unida** | Lista de espera con oferta y plazo (60 min, recortado a 15 min del inicio), perezosa + tarea periódica; candado al reservar. Ver preguntas C4 |
| MS4 | **Unida** | Congelamientos en `membership_freezes` (reglas por plan, programados, historial); los `paused` migrados a congelamientos abiertos. Suite 850 + 416. Ver preguntas MS4 |
| L4 | **Unida** | Logros `manual` que otorga o retira el staff con motivo (retiro lógico); nunca se desbloquean solos. Ver preguntas L4 |
| CO4 | **Unida** | Imagen del anuncio y publicación programada por la tarea periódica (resuelve CO2-5). Ver preguntas CO4 |
| MS5 | **Unida** | Tiqueteras: consumo por evento `CheckInRegistered` (tabla `pass_usages`), devolución al anular, sin gracia; cierra C1-1 y el pendiente de CO7. Ver preguntas MS5 |
| E4 | **Unida** | Reportes de fallas (app y staff), campana agrupada, `unsafe` urgente con correo al Owner, flag `MembersCanReportEquipment`. Suite 902 + 463. Ver preguntas E4 |

| A7, MS6, C6, L5 | **Unidas** | Suite arreglada el 2026-10-16 (consulta de premios, reloj de los tests de A7, test de MS6) |
| MS7 | **Unida** | Planes de grupo. Ver preguntas MS7 |
| C7 | **Unida** | "Estoy en clase". Ver preguntas C7 |
| L6 | **Unida** | Ranking voluntario. Ver preguntas L6 |
| S3 | **Unida** | Avisos de la suscripción a los Owners. Ver preguntas S3 |
| A6 | **Unida** | Geocerca con QR fijo; Android sin probar. Ver preguntas A6 |
| R7 | **Unida** | Progreso y récords; evento `PersonalRecordSet`. Ver preguntas R7 |
| G5 | **Unida** | Onboarding obligatorio; gyms existentes marcados como completos. Ver preguntas G5 |
| M5 | **Unida** | "Mi perfil" del miembro. Ver preguntas M5 |
| S4 | **Unida** | Catálogos del sistema en `/admin/catalog`. Ver preguntas S4 |
| LG5 | **Unida** | Alertas al SuperAdmin (`logs.platform_alerts`). Ver preguntas LG5 |
| C8 | **Unida** | Aviso de clase nueva como anuncio. Ver preguntas C8 |
| L3 | **Unida** | Logros de récords y volumen. Ver preguntas L3 |
| G6 | **Unida** | Configuración por secciones; fuera `CommunityComments`. Ver preguntas G6 |
| R8 | **Unida** | Fecha de revisión de rutinas. Ver preguntas R8 |
| M6 | **Unida** | Anonimización (+R10 y consumidores). La tarea admite limitarse a un gym (tests). Ver preguntas M6 |
| R9 | **Unida** | Aviso de salud en el editor del staff. Ver pregunta R9 |
| CO6 | **Unida** | Ya estaba cubierto por N4; solo tests nuevos |
| D3 | **Unida** | Panel del SuperAdmin. Ver preguntas D3 |
| R11 | **Unida** | Plantillas de rutina por gym. Ver pregunta R11 |
| AP6 | **Unida** | Miembro en escritorio (Inicio y menú lateral). Ver pregunta AP6 |
| AP7 | **Unida** | Tablas apiladas en celular. **Falta mirarlo a 375 px** |
| AP4 | **Unida** | Modo staff / miembro con el `switch-gym` de I2 |
| MS8+L7 | **Unida** | "Mi membresía" y "Mi progreso". Ver preguntas MS8/L7 |
| D1 | **Unida** | Panel del gym por bloques y permisos. Ver preguntas D1 |
| R6 | **Unida** | Imagen y video del ejercicio (R2 sin configurar). Ver pregunta R6 |
| AP3+N3 | **Unida** | Panel en la app y push Android (FCM). **Compila para Android; sin probar en dispositivo** |
| AP5 | **Unida** | Bloqueo con huella o PIN. **Compila para Android; sin probar en dispositivo** |
| D2 | **Unida** | Inicio del miembro (`GET /api/me/home`) |
| V1, DP | Pendiente | Necesitan servicios externos (R2, Resend, Firebase, Dokploy) y un Android real |
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
18. **[DECIDIDA 2026-10-11: (a) y (b) se aceptan; (c) `includeInactive` restringido al Owner en la API, con test.]** **E1: nombres de las unidades y catálogo.** Agregar "Bicicleta estática × 3" crea "Bicicleta estática 1…3" (la spec decía "Bicicleta 1…3" sin decir cómo acortar). Se sembraron 55 modelos (la spec decía ~40). El listado con bajas (`includeInactive`) no está restringido al Owner en la API, aunque la UI solo lo ofrece al Owner.
19. **[DECIDIDA 2026-10-11: (a) copiar un enlace nuevo anula los anteriores (SecurityStamp), hecho con test; (b) resuelta con el token de 15 min (P11); (c) se deja para G1.]** **I1: activación y sesiones.** (a) Cada "Copiar enlace" genera un enlace nuevo y no invalida los anteriores; todos mueren cuando la persona define su contraseña. (b) Cambiar la contraseña cierra las otras sesiones revocando sus refresh tokens, pero sus access tokens viven hasta 60 min (relacionado con la pregunta 11). (c) El alta del Owner por SuperAdmin todavía pide contraseña; la invitación por enlace del Owner llega con G1.
20. **[DECIDIDA 2026-10-11: se mantiene lo del agente — una versión nueva no cierra el candado; consultar con el abogado si algún cambio exige reaceptar (opción futura "exige volver a aceptar").]** **M2: qué cuenta como autorización de datos vigente.** El agente la definió como "la aceptación más reciente, de cualquier versión, no revocada": publicar un texto nuevo deja a la persona "pendiente de aceptar la versión vigente" pero **no** cierra el candado de salud y medidas. ¿O debe cerrarlo hasta que acepte la versión nueva? Además: un menor no puede aceptar en la app (lo hace el acudiente en recepción); la exoneración no se puede revocar (422).
21. **[DECIDIDA 2026-10-11: (a), (b) y (d) se aceptan; (c) se resuelve con la casilla "Abierto 24 horas" por día, PENDIENTE de construir: tarea G3b.]** **G3: decisiones del agente.** (a) Un rechazo por `gym_closed` **no** crea intento rechazado ni aviso en la campana (no es accionable). (b) La asistencia por clase se valida con el gym abierto **a la hora de inicio de la clase**, no a la hora en que se marca. (c) Un tramo cierra como máximo a las 23:59: no se puede expresar un gym 24 h. (d) Las excepciones solo pueden ser de hoy en adelante.
22. **[DECIDIDA 2026-10-11: solo Novedades y Promociones; logros y penalizaciones salen siempre. Notifications §9 corregido.]** **N4: el horario sin avisos solo retiene Novedades y Promociones** (Comunicaciones §G). El catálogo de Notifications §9 marca "Aplica" también para logros y penalizaciones; hay contradicción entre los dos docs. Hoy no cambia nada (esos avisos no tienen correo ni push), pero hay que decidirlo antes de N2. Además: el `Down` de la migración de N4 no se puede ejecutar (Postgres no quita el valor `skipped` del enum).
6. **[DECIDIDA 2026-10-11: (a), (b) y (c) se aceptan; el modo es de la rutina.]** **R2: decisiones del agente para validar.** (a) Cambiar el modo (semanal ↔ rotativa) por `PUT` crea la rutina nueva, desactiva la anterior y **reinicia `starts_on` a hoy**; (b) en semanal no hay filas de descanso y mandar `dayOfWeek` en una rotativa es 422; (c) en semanal, "duración del ciclo" se muestra como "N días de entrenamiento".
5. **[DECIDIDA 2026-10-11: cada día se juzga con la rutina vigente ese día; PENDIENTE de construir: tarea R2b.]** **R1 → R2: racha y días retirados.** Al retirar un día, deja de contar en el calendario de racha **también hacia atrás** (el calendario solo ve los días vigentes). Para el modo semanal (R2) conviene decidir si la racha se calcula con el calendario vigente en cada fecha.

**Decisiones del 2026-10-16:**

- **MS6-1:** Memberships leía `IPaymentReader` y Payments ya leía Memberships (segundo ciclo entre módulos). Se rompe: Memberships declara el puerto `IMembershipPaidAmountProvider` y Payments lo implementa en Infrastructure. Hecho.
- **Resto de MS6, C6, L5 y A7** (las preguntas de esa tanda no se habían anotado aquí): MS6 — el crédito se calcula sobre lo pagado sin matrícula, más el crédito heredado; ajuste manual de 0 a lo pagado; los días congelados programados no cuentan como usados; con una membresía programada detrás de la vigente el cambio da 409; no cobra matrícula ni cuenta para el límite del SaaS; se permite cambiar entre tiquetera y plan por tiempo. C6 — una clase de entrada libre no se liga a máquinas (422); la disponibilidad sale del estado actual de la máquina y su `ExpectedBackOn`; si se dan de baja todas las máquinas el cupo queda en 0 sin aviso; una oferta ya hecha se respeta al confirmar. L5 — ¿se entrega un premio vencido?, ¿la entrega necesita permiso propio?, un logro desactivado con premio pendiente se ve en recepción pero no en `/app`, el aviso de vencimiento va solo a la bandeja. A7 — el push de rechazos viene apagado por defecto también para el Owner; sin app el aviso de entrada con documento llega solo a bandeja y correo.

**Decisiones del 2026-10-12 (preguntas de E2, P2, I7 y N2):**

- **E2:** el nivel 1 (filtro "lo que se puede hacer en mi gym") lo conecta **E3**. Alargar el arreglo repitiendo el estado con fecha nueva: **hecho**.
- **P2-1:** el aviso de "anulación solicitada" llega al Owner, a quien tiene `VoidPayments` y a quien tiene `ViewFinances` (solo ve) → tarea **N1b**, pendiente.
- **P2-2:** "lo que cobró en su turno" va en **P3**.
- **P2-3:** nota opcional al rechazar, con aviso personal a quien pidió (`VoidRequestRejected`): **hecho** (migración `V2_P2b_RejectionNote`).
- **P2-4:** anular directo cierra la solicitud pendiente: aceptado.
- **I7-5:** la sesión del kiosco no vence hasta que el Owner la revoca: aceptado.
- **I7-6:** cerrar `/kiosk` a la sesión normal del staff va en **A3**.
- **N2-7:** una promoción no puede marcarse como importante (Ley 1581): **hecho**. Resto de N2 aceptado (Firebase sin probar; importante sin fecha queda hasta archivarse; no cuenta para el límite de fijados).

**Preguntas de la tanda del 2026-10-14 (para responder mañana):**

- **M3-1 (desvío de la spec):** §5 dice que `GET /api/members/{id}` trae la salud vacía sin permiso. El agente dejó la ficha solo con `HasHealthAlerts` y puso el detalle en `GET /api/members/{id}/health`, así el registro de consultas refleja abrir la pestaña y ningún otro endpoint filtra salud. ¿Se acepta?
- **M3-2:** sin autorización de datos aceptada, `PUT .../health` responde **409** y `GET` responde 200 con `consentRequired=true`. ¿Otro código?
- **M3-3:** el miembro leyendo su propia salud queda para **M5** (`/api/me/profile`); `/health` da 403 al miembro. Confirmar.
- **M3-4:** la consulta de un SuperAdmin impersonando no va a `health_access_log` (ya queda en `superadmin.impersonation_actions`). Confirmar.

- **C1-1:** las tiqueteras aún no existen (MS5); la regla de cobertura de clases por entradas queda pendiente para cuando existan.
- **C1-2:** cuando el gym cambia su horario, las sesiones futuras con reservas que ya no caben se cancelan **de forma perezosa al abrir el horario del staff** (un GET escribe). ¿Se prefiere un evento `OpeningHoursChanged` de Gyms o la tarea periódica?
- **C1-3:** el aviso al miembro sale por `ClassSessionCancelled` con motivo "Gym cerrado"; revisar el texto.
- **[DECIDIDA 2026-10-16: se guardan los que están al día y la respuesta trae `NotMarked` con quién quedó fuera y por qué. Hecho, con test.]** **C1-4:** marcar asistencia con un asistente que no está al día rechaza **el lote entero** nombrando a la persona. ¿O solo a esa persona?
- **C1-5:** 409 para "gym cerrado"/"no está al día" al reservar o marcar; 422 al crear/editar fuera de horario. Confirmar.
- **C1-6:** la cobertura de la asistencia se mide en la fecha de la sesión, no hoy. Confirmar.
- **C1-7:** el roster no muestra "Gym cerrado" (solo el horario). ¿Hace falta?

- **I5-1:** si la persona es staff y miembro, el código **solo abre sus accesos de miembro**. ¿O todos?
- **I5-2:** solo puede pedir código quien tiene un acceso de miembro activo; los demás reciben la misma respuesta 202 sin correo. Confirmar.
- **I5-3:** sin Resend, el código **no aparece en el log** (correo marcado sensible). ¿Se permite verlo solo en Development?
- **I5-4:** el enlace de invitación `/app/registro?code=…` no lleva el slug del gym, así que la primera vez no sale la marca. ¿Se agrega `&gym=slug` al enlace?
- **I5-5:** si el correo falla, la respuesta sigue siendo 202 sin aviso. Confirmar.
- **I5-7:** el límite de 5 códigos por hora no se le avisa al miembro (solo no le llega). ¿Mensaje explícito?
- Observación de I5: `RefreshTokenService.IssueAsync` usa `DateTimeOffset.UtcNow` en vez de `IClock` (candidato a `BUGS.md`).

- **[DECIDIDA 2026-10-16: caja compartida, como está (una sede, un cajón; cada cobro queda firmado por quien lo hizo). Sin cambios.]** **P3-1:** la caja "olvidada" = abierta hace más de **16 h**. Antes de eso, cualquier staff cobra en efectivo en la caja abierta de otro. ¿Se bloquea al staff que no abrió la caja?
- **P3-2:** mover dinero y cerrar: solo quien abrió la caja o el Owner (`ViewFinances` no basta). Confirmar.
- **P3-3:** un retiro mayor al efectivo esperado se permite sin límite. ¿Se bloquea?
- **P3-4:** la barra de la caja solo está en `/payments`, no en Recepción. ¿Se lleva también a Recepción?
- **P3-5:** el filtro "persona" de la lista de turnos usa un endpoint solo-Owner y se oculta a quien solo tiene `ViewFinances`. ¿Hace falta un endpoint de equipo para ellos?
- **P3-6 (técnico, sin decidir):** dos aperturas simultáneas las rechaza el índice único, pero esa carrera da 500 en vez de 409. Arreglo pendiente pequeño.
- **Observación de C1:** reservar una sesión que ya empezó puede dejar la sesión materializada sin reserva (inofensivo; preexistente).

- **C5-1:** la fecha de fin de la serie es **inclusiva** (ese día aún hay clase). Confirmar.
- **C5-2:** se permite poner una fecha de fin ya pasada (siempre que no sea anterior al inicio). Confirmar.
- **C5-3:** si se acorta la serie y ya hay reservas después del nuevo fin, esas sesiones siguen y **no se cancelan ni se avisa**. ¿Se cancelan y se notifica?
- **C5-4:** la fecha de fin es independiente de dar de baja la clase. Confirmar.

- **M4-1:** las mediciones (leer, añadir, borrar) exigen `ViewMemberHealth`; un entrenador o recepción sin ese permiso no ve pesos. ¿O basta ser staff?
- **M4-2:** leer mediciones **no** se registra en `health_access_log`. ¿Debe registrarse?
- **M4-3:** borrar una medición deja una fila de historial **sensible** (solo la ve quien tiene `ViewMemberHealth`). Confirmar.
- **M4-4:** las mediciones del propio miembro y la gráfica de evolución quedan para M5. Confirmar.
- **M4-6:** se permiten varias mediciones el mismo día; peso y altura se redondean (2 y 1 decimales) antes de validar. Confirmar.

- **A1b-1:** el rechazo por las 2 horas usa el motivo `already_checked_in` (con la hora en que puede volver). ¿Motivo propio `too_soon`?
- **A1b-2:** las 2 horas aplican también a la recepción del staff (todas las vías). Confirmar.
- **A1b-3:** `valid_until` de la regla es inclusivo. Confirmar.
- **A1b-5:** asistir a una clase no crea una segunda entrada si ya hubo una ese día, aunque el miembro tenga entradas múltiples. Confirmar.
- **A1b-6:** "Check-ins hoy" del dashboard y los conteos de SuperAdmin cuentan **entradas**, no personas. ¿Se cambian a personas distintas? (lo aclara A5)
- **[DECIDIDA 2026-10-16: el ingreso es idempotente — candado por miembro y un escaneo repetido en 2 min devuelve la misma entrada (200). Hecho, con tests.]** **Riesgo A1b:** sin el índice único por miembro y día, dos escaneos simultáneos pueden registrar dos entradas. ¿Se cierra con un candado por miembro?

- **[DECIDIDA 2026-10-16: se pide un código al correo actual además del nuevo (migración `V2_I6b_EmailChangeCurrentCode`). Hecho, con tests.]** **I6-1:** cambiar el email **no pide la contraseña actual** (el miembro entra con código). Riesgo: una sesión robada puede mover el email. ¿Se exige?
- **I6-2:** si el email nuevo ya es de otra cuenta, 409 con mensaje claro (permite saber si un email existe). ¿O 202 sin enviar nada?
- **I6-3:** demasiados pedidos = 409 (no hay 429 en el dominio); código malo/vencido = 422. Confirmar.
- **I6-4:** el formulario quedó en las pantallas de cuenta actuales (`/cuenta/contrasena`, `/app/cuenta/contrasena`); "Mi perfil" va en M5. Confirmar.
- **I6-5:** se manda un aviso al correo viejo (no lo pedía la spec); no se cierran las otras sesiones. Confirmar.

- **A2-1:** la ventana para anular es por día de negocio, `fecha >= hoy - 7` (8 días de calendario contando hoy). ¿Correcto?
- **A2-2:** el miembro nunca ve sus entradas anuladas; el staff solo con "Ver anuladas". Confirmar.
- **A2-3:** anular una entrada **no avisa al miembro**. ¿Debe avisarse?

- **S2-1:** el enum `GymPlan` (en `gyms.gyms.plan` y `GymCreated`) se conserva; manda `SaasPlanId`. ¿Se elimina el enum ya (toca Gyms)?
- **S2-2:** dos altas simultáneas al borde del límite pueden pasarse por una (sin candado). ¿Aceptable?
- **S2-3:** el recuento incluye miembros desactivados que tengan membresía vigente. ¿Se excluyen?
- **S2-4:** bajar el límite por debajo del uso no expulsa a nadie, solo bloquea membresías nuevas. Confirmar.
- **Observación S2:** la spec menciona `TrialGraceDays` en `GymSubscription` y S1 no lo trae.

- **C3-1:** ¿un valor 0 debe apagar una regla? Hoy el mínimo es 1 en límite de inasistencias, ventana y máximo de reservas; solo el plazo de cancelación admite 0.
- **C3-2:** "bloqueado hasta el X" es inclusivo (7 días contando el día en que se llega al límite). Confirmar.
- **C3-3:** una cancelación tardía cuenta como inasistencia aunque luego vuelva a reservar; marcar asistencia la borra. Confirmar.
- **C3-4:** la penalización se levanta desde "Penalizados" en Clases, no desde la ficha del miembro. ¿También en la ficha?
- **C3-5:** los eventos `BookingPenaltyApplied`/`Lifted` aún **no avisan al miembro** (falta un consumidor en Notifications; ¿va en C8?).

- **R4-2:** una serie no exige objetivo (reps, tiempo o distancia); solo mínimo 1 serie por línea. ¿Se exige?
- **R4-3:** las líneas de una superserie no tienen que tener el mismo número de series; el backend no exige mínimo 2 líneas (la UI suelta las de una sola). Confirmar.
- **R4-4:** los campos que no aplican al tipo de medida se descartan en silencio (no 422). Confirmar.
- **R4-5:** planeado vs hecho se compara en el cliente: gana la peor medida; una serie al fallo se cumple con ≥ 1 repetición. Confirmar.
- **R4 (dato):** la migración es irreversible en datos (las repeticiones no numéricas pasan a notas). Cuando entre R3b (peso + distancia), añadir esa medida a las series.

- **CO7-1:** `ReminderDaysBefore = 0` apaga **los tres** recordatorios (antes, vence hoy, último día de gracia). ¿O solo el de "N días antes"?
- **CO7-3:** los tres avisos comparten tipo e ícono; se distinguen por un `ReferenceId` derivado (MD5 de `membresía:paso`). ¿Tipos de aviso propios?
- **CO7-4:** revisar el texto del último día de gracia ("Hoy es tu último día para renovar sin perder tu fecha").
- **CO7 (para MS5):** cuando existan tiqueteras, excluir las que no tienen gracia del aviso de último día.

- **C2-1:** al cambiar el instructor de una clase también se reasignan las sesiones futuras ya materializadas (las pasadas no). Confirmar.
- **C2-2:** "Mis clases" exige el permiso `Trainer` (403 sin él). ¿O cualquier staff con lista vacía?
- **C2-3:** cualquier staff (no solo el instructor) puede marcar asistencia o cancelar cualquier sesión. Confirmar.

- **E3-1:** las ligas propias del gym **reemplazan** a las de modelo en ese gym (no suman). Confirmar.
- **E3-2:** un ejercicio ligado a un modelo que el gym no tiene cae a la regla de tipos de equipo. Confirmar.
- **E3-3:** no hay pantalla de SuperAdmin para ligar ejercicios a modelos (solo API y siembra); va con S4.
- **E3-5:** revisar los 16 pares ejercicio↔modelo sembrados en `V2_E3.sql` (propuesta del agente).

- **N5-1:** si se reserva dentro de los 10 minutos previos al inicio, **no** hay recordatorio. ¿Uno inmediato?
- **N5-2:** un recordatorio de vencimiento generado de madrugada sale a las 8:00 aunque el staff haya renovado entretanto. ¿Se revalida al enviar?
- **N5-3:** el recordatorio del instructor se crea con la primera reserva; si todas se cancelan, lo recibe igual; una sesión sin reservas no lo genera. Confirmar.
- **N5-4:** el recordatorio del instructor no se puede apagar (no está en las preferencias del staff). ¿Se agrega?
- **N5-6 (hueco de Classes):** desactivar una clase no cancela sus reservas futuras ni sus recordatorios.
- **N5-7:** `BookingPenaltyApplied` sigue sin aviso al miembro (ver C3-5).

- **CO2-1:** dentro de un filtro los valores suman (O); entre filtros, "y". Confirmar.
- **CO2-2:** publicar un segmento sin miembros da 422. Confirmar.
- **CO2-3:** el filtro de plan solo cuenta membresías que cubren hoy (no en gracia ni congelados); el de clase cuenta reservas y asistencias de los últimos 30 días. Confirmar.
- **CO2-4 (hueco):** `GET api/announcements/email-audience` ignora el segmento: la confirmación por email puede decir "a N miembros" con N mayor. ¿Se corrige ya?
- **CO2-5 (para CO4):** la publicación programada corre sin usuario; los readers de la audiencia necesitarán el gym explícito.

- **CO5-1:** si la tarea no corre en todo el día, ese cumpleaños no se felicita (no se recupera). Confirmar.
- **CO5-2:** "la noche anterior" se implementó como la primera ejecución del día con envío a las 8:00. Confirmar.
- **CO5-3:** el "regalo" va dentro del texto libre, sin campo aparte. ¿Hace falta?
- **CO5-4:** el texto de la felicitación automática y la plantilla de WhatsApp de cumpleaños (CO3) son independientes. ¿Se unifican?
- **CO5:** la felicitación viene **apagada por defecto** por gym. Confirmar.

**Preguntas de la tanda MS1b/G3b/R2b/L1 (2026-10-15):**

- **G3b-1:** un tramo 00:00–23:59 escrito a mano se lee de vuelta como "24 horas" (no hay columna propia). ¿Aceptable, o columna `is_24h`?
- **G3b-2:** una clase de 23:30 a 00:30 no cabe ni en un día 24 h (no se cruza la medianoche). Sigue la spec; confirmar.
- **G3b-3:** la casilla 24 h no existe para el horario de festivos ni para las excepciones por fecha. Confirmar.
- **R2b-1:** un día retirado deja de contar desde el mismo día del cambio (ese día ya usa la rutina nueva). Confirmar.
- **R2b-2:** en el modo rotativo no se guarda la historia exacta del ciclo; cada fecha se reconstruye con los días vigentes ordenados por posición. Si se agregan o quitan días a mitad de un ciclo, puede no coincidir con lo que el miembro vio. ¿Se acepta?
- **MS1b-1:** el rechazo por membresía programada se guarda como `no_membership` (solo lo distingue el mensaje). ¿Motivo propio `membership_scheduled` (exige `ALTER TYPE`)?
- **MS1b-2:** una membresía programada no cuenta para el límite de miembros del plan SaaS. Confirmar.
- **MS1b-3:** los recordatorios de vencimiento (CO7) no tienen en cuenta el estado programado. ¿Hace falta algo?
- **L1-1:** la regla "QR fijo sin ubicación no cuenta para logros ni racha" queda para A6 (filtrar en `AttendanceReader.Attendances`).
- **L1-2:** crear/editar y otorgar son dos llamadas desde la UI (no atómicas; otorgar es idempotente y se puede repetir). ¿Aceptable?
- **L1-3:** el otorgamiento calcula racha, sesiones y clases miembro por miembro (solo las visitas van en bloque). Suficiente para un gym normal; confirmar.
- **L1-4:** se pregunta por el otorgamiento solo al crear, al cambiar el tipo o al bajar el valor (subirlo no quita logros). Confirmar.

**Preguntas de la tanda R3b/N1b/S1b/LG2 (2026-10-15):**

- **R3b (observación):** el registro por serie (R4) no descarta los campos que no aplican a la medida; solo lo controla la UI.
- **N1b-1:** quien solo tiene `ViewFinances` ve el aviso de anulación pero no puede cerrarlo a mano (403). Confirmar.
- **S1b-1:** "nunca pagó" = `PaidThrough <= TrialEndsOn`, sin mirar los pagos: una renovación de cortesía cuenta como pago. ¿Se acepta?
- **S1b-2:** tope de 60 días para la gracia tras la prueba (lo puso el agente; la spec no lo dice). ¿Se confirma o se quita?
- **S1b-3:** `OverdueGraceDays` por gym (spec §V2) sigue fijo en 15; los avisos "Tu prueba terminó" quedan para S3.
- **LG2-1:** las peticiones a `/internal/jobs/*` son lentas a propósito y llenarán `slow_requests`. ¿Se excluyen (ya tienen `job_runs`)?
- **LG2-2:** un 403 por permiso queda en `business_rejections` y en `security_events`; un 403 de dominio solo en la primera. Confirmar.
- **LG2-3:** `account_locked` solo sale en el intento que bloquea; los siguientes quedan como `login_failed`. Confirmar.
- **LG2-4 (pendiente técnico):** `code_attempts_exceeded` no se registra. El agente creyó que el login con código (I5) no existía, pero **sí existe**: falta engancharlo. `document_entry_locked` espera a A4. Tampoco hay test de `permission_denied` ni de `account_locked`.

**Preguntas de la tanda A3/MS2/LG3/I4+G4 (2026-10-15):**

- **A3-1:** el cartel del QR fijo ya no se imprime desde la sesión del staff (solo desde el dispositivo). ¿Vista de impresión para el Owner en Configuración → Check-in?
- **A3-2:** revocar un kiosco corta el refresh, pero su token sigue sirviendo el código hasta 15 min. Aceptado en I7; confirmar.
- **A3-3:** el kiosco ya no muestra el nombre del gym (no tiene endpoint). ¿Se agrega un GET pequeño para el rol `kiosk`?
- **A3-4:** se quitó "Kiosco" del menú (los botones llevan a "Configurar kiosco" en `/settings`). El kiosco es solo web.
- **MS2-1:** la matrícula se configura en la pestaña Check-in de `/settings` (solo Owner). ¿Otra ubicación?
- **MS2-2:** se cobra matrícula solo si el miembro nunca tuvo membresía (las canceladas cuentan como historial). Confirmar.
- **[DECIDIDA 2026-10-16: "pendiente de pago" visible — `AmountDue` en la lista de membresías de la ficha y bloque `Unpaid` en "Requiere atención" del dashboard; no bloquea la entrada. Hecho, con tests.]** **MS2-3:** el servidor no impide una membresía sin pago (el cobro obligatorio lo impone el diálogo). Evitarlo exigiría escritura cruzada o chequeo diferido. ¿Se acepta?
- **LG3-1:** R2 sin test (el `AmazonS3Client` se crea en el constructor). ¿Inyectar `IAmazonS3`?
- **LG3-2:** la app Windows (MAUI) no reporta errores (la spec no lista `windows`). ¿Se agrega?
- **LG3-3 (para LG5):** "5 fallas seguidas del mismo proveedor" no puede saber si hubo un éxito entre medias (no se registran éxitos). Decidir antes de LG5.
- **I4-1:** `Gym.OwnerId` no se eliminó (la spec dice que desaparece): queda como "fundador histórico" sin efecto. ¿Se elimina en otra tanda?
- **I4-2:** al bajar a un Owner a staff, perfil Recepción por defecto. Confirmar.
- **I4-3:** el cambio de contacto de facturación no se audita. ¿Se audita?
- **I4-4:** el traspaso son dos llamadas desde la UI (primero agregar, luego quitarse). Si el nuevo no activó su cuenta, el gym queda con un Owner pendiente. "Copiar enlace" para el Owner quedó fuera.

**Preguntas de la tanda MS3/A4/R5/LG4 (2026-10-15):**

- **MS3-1:** al reanudar un congelamiento se corre el fin pero no el día del ciclo; la siguiente renovación arranca un ciclo nuevo si no coincide. ¿Esperado? (ligado a MS4)
- **MS3-2:** la membresía no guarda la unidad ni la duración del plan; si MS5/MS6 las necesitan, habrá que guardarlas.
- **A4-1:** "Bienvenido/a, Ana" (no hay dato de género). ¿Se queda así?
- **A4-2:** con el QR apagado, el kiosco muestra "desactivado" aunque el documento esté encendido. ¿Debe funcionar el documento sin QR?
- **[DECIDIDA 2026-10-16: se queda en memoria (un solo servidor); revisar si algún día hay varias instancias.]** **A4-3:** el bloqueo por intentos vive en memoria (un reinicio lo limpia; no sirve con varias instancias).
- **A4-4:** el documento se compara sin tipo: dos miembros con el mismo número y distinto tipo son rechazados los dos.
- **A4-5:** el ajuste se llama `ByDocument` y vive en `attendance.checkin_settings`, no en `GymFeatureFlags` (la spec dice `CheckinByDocument`). ¿Se acepta?
- **R5-1:** se agregaron PUT/DELETE de alternativas por gym sin pantalla en el panel (la del SuperAdmin es S4). ¿Hace falta pantalla para el staff?
- **R5-2:** un gym no puede ocultar una alternativa del sistema (422). ¿Debería?
- **R5-3:** si salen menos de 3 sugeridas se completa con el mismo grupo muscular; las recomendadas salen aunque el gym no tenga la máquina (marcadas). Confirmar.
- **R5-4:** "hizo X en lugar de Y" se guarda, pero el staff no tiene pantalla de historial del miembro donde verlo.
- **LG4-1:** huellas propias para `external` (`proveedor|operación|estado`) y `client` (`plataforma|tipo`). Confirmar.
- **LG4-2:** `logs.error_groups` solo guarda estado y notas; conteos y gyms se calculan al leer. LG5 puede necesitar contadores escritos.
- **LG4-3:** `MemberInvited` no es reintentable (el código se limpia): falta el refrescador de Members (decisión del 2026-10-11). ¿Se construye o basta reinvitar?
- **[DECIDIDA 2026-10-16: el reintento corre en el gym del evento (`GymExecutionScope`). Hecho, con test.]** **LG4-4 (riesgo):** el reintento corre el manejador como SuperAdmin (sin gym, filtro apagado). No se auditaron los ~20 manejadores; los que usen `RequireGymId()` fallarían. ¿Contexto de gym para el reintento?
- **LG4-5:** las vistas de rechazos, seguridad, lentitud y procesos del §7 no se construyeron (la fila LG4 no las pide). ¿Tarea aparte?

**Preguntas de la tanda A5/L2/C4/MS4 (2026-10-15):**

- **A5-1:** "Check-ins hoy" sigue contando entradas, no personas (la ocupación sí cuenta personas). Es la A1b-6.
- **A5-2:** la spec pone la ocupación del miembro "junto a las horas pico que ya existen", pero el miembro no tiene horas pico (solo el staff). ¿Se le exponen?
- **L2-1:** la antigüedad son días cubiertos ÷ 30 (spec §3.1), no meses del calendario. Confirmar.
- **L2-2:** un referido cuenta con una membresía no cancelada de precio > 0, no con un pago registrado (evita depender de Payments). ¿Se acepta?
- **L2-3:** si el staff anota el referidor cuando el amigo ya tenía membresía, el logro llega en el próximo check-in o membresía, no al instante. ¿Evento de Members?
- **L2-4:** un referido inactivo sigue contando. Confirmar.
- **C4-1:** subir el cupo de una clase no llega a las sesiones ya materializadas (copian el cupo), así que no libera ofertas. ¿Se propaga?
- **C4-2:** al cancelar una sesión, la lista de espera queda `expired` sin aviso. ¿Aviso propio?
- **C4-3:** si el miembro deja de estar al día con una oferta abierta, no puede confirmar pero la oferta aparta el cupo hasta vencer. ¿Debe saltar al siguiente?
- **MS4-1:** los planes (nuevos y migrados) quedan con `MaxFreezeDays = 0`, como pide §13: hasta configurarlos solo el Owner congela, con override. ¿Otro valor por defecto (p. ej. 30)?
- **MS4-2:** un congelamiento abierto que supera el límite del plan solo acredita hasta el límite (salvo override). ¿Se acredita todo?
- **MS4-3:** un programado no puede empezar antes de hoy ni fuera de la vigencia, pero sí terminar después del fin actual. Confirmar.
- **MS4-4:** "por vencer" y los días que quedan cuentan los días de un congelamiento futuro ya programado (el fin ya está corrido). Confirmar.
- **MS4-5 (= MS3-1):** correr el fin por un congelamiento no mueve el día del ciclo; la renovación siguiente arranca ciclo nuevo si no coincide.
- **Integración MS4 ↔ L2:** MS4 reemplazó `paused`/`paused_on` por congelamientos; el `PausedDays` de L2 se retiró y la antigüedad descuenta los tramos congelados.

**Preguntas de la tanda L4/CO4/MS5/E4 (2026-10-15):**

- **L4-1:** cualquier staff retira un logro manual (igual que otorgarlo). Confirmar.
- **L4-2:** volver a otorgar un logro retirado no genera aviso nuevo (misma fila, aviso idempotente). ¿Avisar otra vez?
- **L4-3:** se guarda el id de quien otorgó, pero la UI no muestra su nombre (exigiría leer de Identity).
- **L4-4:** en `/app/logros` los manuales no ganados se ven bloqueados. ¿Se ocultan?
- **L4-5:** el aviso dice "Desbloqueaste «X»" también para los manuales. ¿Otro texto?
- **CO4-1:** un programado que ya no puede salir (audiencia vacía o caducado) vuelve a borrador solo con un log. ¿Avisar al autor?
- **CO4-2:** el correo y el push aún no incluyen la imagen (el dato ya viaja en el evento).
- **CO4-3:** dos ejecuciones simultáneas de la tarea podrían publicar el mismo anuncio (sin claim atómico). Con una sola tarea de Dokploy no debería pasar.
- **MS5-1:** se permiten varias tiqueteras vigentes; se gasta la que vence primero. ¿O se rechaza comprar otra?
- **MS5-2:** si un plan por tiempo cubre el día, manda y la tiquetera no se toca. Confirmar.
- **MS5-3:** una tiquetera cobra matrícula si es la primera membresía (§4.1 literal). Confirmar.
- **MS5-4:** la lista "Por vencer" y el dashboard siguen contando solo por fecha (no por ≤ 2 entradas).
- **MS5-5:** agotar la tiquetera no avisa al miembro.
- **E4-1:** el aviso "tu reporte se resolvió" usa la categoría `News` (configurable, respeta el horario sin avisos). Confirmar.
- **E4-2:** el texto al miembro es neutro ("Lo que reportaste de «X» ya quedó resuelto"). Confirmar.
- **E4-3:** al volver una máquina a `available`, los reportes abiertos no se ofrecen cerrar automáticamente (solo se listan).
- **E4-4:** un reporte `unsafe` nunca se agrupa con otros. Confirmar.
- **Encontrado al integrar (corregido):** `IMemberWaitlistClient` (C4) no estaba registrado en el cliente; `/app/clases` habría fallado en la app real (los tests registran sus propios falsos).

- **C7-1:** "ya entró al gym hoy" se lee con `IAttendanceReader` (Classes → Attendance en lectura; Attendance ya consume eventos de Classes). ¿Aceptable o se prefiere otra vía?
- **C7-2:** con reserva cancelada o marcada `no_show` por el staff, el miembro no puede marcarse (409, "habla con recepción"). Confirmar.
- **C7-3:** en clase con cupo, sin reserva no se puede marcar; una oferta de lista de espera sin confirmar no cuenta. Ventana ±15 min del inicio. Confirmar.
- **C7-4:** "Estoy en clase" cancela el recordatorio de 10 min del miembro (C8 aún no existe).
- **S3-1:** "se inhabilitará pronto" en pruebas solo sale si `trial_grace_days > 3` (con 3 coincide con "tu prueba terminó"). Confirmar.
- **S3-2:** el aviso de límite de miembros solo va a los Owners; el SuperAdmin no recibe aviso propio (su cartera ya muestra usados/límite). ¿Hace falta?
- **S3-3:** "pago registrado" no avisa en la renovación sin pago ni en el primer pago del alta. El motivo de la suspensión no se muestra al Owner. Enlace de los avisos = "/" (no hay pantalla de suscripción en el panel). ¿Se crea una?
- **S3-4:** avisos de calendario una vez por periodo; un gym inhabilitado que sigue sin pagar no recibe más.
- **L6-1:** sin fecha de nacimiento cuenta como menor y no puede participar en el ranking. Confirmar.
- **L6-2:** el ranking exige `ShowRanking` (nuevo, por defecto encendido) y `ShowGamification`.
- **L6-3:** alias único por gym sin distinguir mayúsculas (409), sin foto ni apellido. Salirse borra la participación y retira también del podio del mes anterior.
- **L6-4:** empates: gana quien llegó antes al número de días, luego antigüedad en el ranking; sin posiciones compartidas.
- **L6-5:** cuando exista A6, las entradas fuera del radio no deben contar en el ranking (revisar al unir A6).

- **MS7-1:** en un plan de grupo la matrícula la paga solo el titular (si es su primera membresía); los beneficiarios no pagan matrícula ni precio.
- **MS7-2:** cada renovación crea un grupo nuevo con el mismo titular; conserva a los beneficiarios por defecto.
- **MS7-3:** un grupo no admite cambio de plan (409): se cancela y se asigna otro. El titular no se puede transferir; cancelar al titular cancela el grupo, cancelar a un beneficiario lo saca.
- **MS7-4:** solo planes por tiempo pueden ser de grupo (cupos 1..500). No se agrega beneficiario con congelamiento abierto/programado.
- **MS7-5:** al congelar, cada integrante valida su límite; si uno no cabe, no se congela nadie.

- **R7-1:** la primera marca de un ejercicio es línea base, no récord (el "primer récord" de L3 sale con la segunda sesión que mejora).
- **R7-2:** el evento `PersonalRecordSet` solo se publica para ejercicios `RepsWeight` (peso, volumen de serie, volumen de sesión); los demás tipos tienen récords visibles pero sin evento. Las rutinas auto-asignadas cuentan para el progreso, no para logros.
- **R7-3:** el progreso lo ve cualquier staff, sin permiso especial. ¿Debería exigir `Trainer`?

- **A6-1:** cuentan para racha/logros/ranking las entradas `inside` y las sin dato (historial, QR rotativo, documento, recepción); se excluyen `outside` y `denied`. Ocupación y tiqueteras cuentan todas. Ubicación inválida = `denied`; el servidor nunca rechaza por ubicación.
- **A6-2:** se añadió `GET /api/attendance/checkin/location-policy` para no pedir permiso de ubicación con el QR rotativo.
- **A6-3:** si la entrada del día no cuenta (fuera del radio), volver a escanear da `already_checked_in` (salvo entradas múltiples). ¿Se permite reintentar?
- **A6-4:** la parte Android (`MauiDeviceLocation`) está sin probar en dispositivo.

- **G5-1:** Gyms lee de Equipment, Memberships, Members e Identity para los pasos (solo lecturas; hay ciclo de namespaces con Equipment/Members). ¿Se mueve a un compositor aparte, como `MemberStreak`?
- **G5-2:** el paso de dirección exige dirección y ciudad, no el pin del mapa. ¿Condicionar el pin al QR fijo?
- **G5-3:** las plantillas de planes (Día, Mensual, Trimestral, Anual) viven en el cliente; matrícula y gracia quedan fuera del asistente.
- **G5-4:** completado es definitivo (no vuelve a bloquear). Los gyms existentes quedan marcados como completos. El bloqueo solo está en el frontend y falla abierto. El SuperAdmin impersonando no se bloquea.
- **G5-5:** la omisión desde SuperAdmin se audita en `gym_onboarding` y en el log, no en `impersonation_actions`.

- **M5-1:** en "Mi perfil" el miembro no edita objetivo ni género (solo contacto, emergencia, salud y foto). ¿Debe poder fijar su objetivo?
- **M5-2:** campos de identidad en el PUT: distinto a lo guardado → 403, igual → se acepta. Email distinto → 422 (va por el cambio con código de I6). El miembro no borra mediciones.
- **M5-3:** sin autorización de datos, la salud tampoco se muestra (no solo no se edita). Un menor ve su salud pero no la edita.
- **S4-1:** en el catálogo del sistema nada se borra: ejercicios y modelos de máquina solo se desactivan; desactivar un modelo no toca las máquinas ya creadas por los gyms.
- **S4-2:** no hay "ejercicios que usan este modelo" ni ligado desde la pestaña de Máquinas (exigiría que Equipment lea Routines).
- **LG5-1:** las alertas del SuperAdmin no pasan por `notifications.notifications` (exige `gym_id`): viven en `logs.platform_alerts` y Notifications solo da el canal (correo y push). ¿O una campana propia del SuperAdmin?
- **LG5-2:** `/admin` todavía no registra push: hasta entonces el push de las alertas queda `skipped` y solo llega el correo.
- **LG5-3:** "5 fallas seguidas" del proveedor = 5 o más en 10 min (no se guardan los éxitos). Un grupo resuelto que reaparece no vuelve a dar "error nuevo". Toda tarea `*_job` se asume al menos cada 30 min.

- **G6-1:** "entrada con documento" se deja en `CheckinSettings.ByDocument` (A4), no se duplica como flag del gym como pide §7. ¿Migrar el dato al flag?
- **G6-2:** Membresías, Rutinas y Equipos no tienen sección propia todavía (sus flags siguen en "Módulos"; la gracia vive en la pestaña Check-in). Se añadió la sección "Clases", que §7 no lista.
- **G6-3:** se quitó `CommunityComments` de punta a punta (columna incluida): cambio incompatible del contrato; la app nativa debe regenerarse junto con la web.

- **C8-1:** el aviso de clase nueva es un anuncio en Novedades ("Nueva clase: …"), con interruptor "Avisar a los miembros" al crear (editar nunca avisa); respeta la preferencia de Novedades. Sin botón "Ver horario" (el aviso apunta a `/app`); los textos no llevan salón ni número de reservas.
- **L3-1:** el récord cuenta por día y ejercicio (no por tipo de marca); el volumen excluye calentamientos (la spec dice "todas las series") y solo rutinas asignadas.
- **L3-2:** el volumen se evalúa al primer registro del día o al batir un récord; registros posteriores del mismo día esperan al siguiente evento. Los gyms existentes no reciben el logro "Primer récord" (solo los nuevos).

- **R8-1:** "marcar revisada" corre la fecha a hoy + N semanas (no a la fecha anterior + N); cambiar el ajuste no recalcula rutinas ya asignadas. Ventana de la lista: atrasadas y próximos 7 días (fija).
- **R8-2:** las rutinas auto-asignadas no entran a "por revisar". Cualquier staff puede marcar revisada (el entrenador es referencia, no candado). El entrenador ve por defecto solo sus miembros.
- **M6-1:** las bajas que ya existían toman "hoy" como fecha de baja (nada se anonimiza de golpe). ¿Usar la fecha real del historial?
- **M6-2:** se conservan etiquetas (texto libre), mediciones, autorizaciones, género, objetivo y fuente; no se tocan textos libres de otros módulos (motivos de "Dejar pasar", notas de pagos, motivos de cancelación). ¿Se limpian también?
- **M6-3:** también se vacían título y texto de los avisos del miembro y sus correos (desvío más amplio que la spec). La cuenta sin otros accesos se elimina y libera el email.
- **M6-4:** documento anonimizado = `ANON-` + 20 hex (la columna admite 30). Aviso al Owner fijo a 7 días. El staff aún puede añadir notas o mediciones a una ficha anonimizada (sin guarda).
- **R9-1:** el aviso de salud en el editor solo existe en el editor del staff; con `ViewMemberHealth` muestra condiciones y restricciones (no medicamentos) y cada apertura queda en el registro de consultas. ¿Botón "Ver detalle" para no inflar el registro?

- **AP6-1:** solo se adaptó Inicio y el menú lateral a escritorio; Rutina, Clases y Mi progreso funcionan con el contenedor ancho pero sin los dos paneles de la spec (queda `.gsm-m-split` listo). Inicio en escritorio no ofrece escanear ("regístrate desde el celular o en recepción").

- **D3-1:** gym inactivo = no suspendido ni inhabilitado, con 7+ días de vida y 7+ días sin entradas (o nunca). "Arranque" usa el onboarding persistido: un gym cuyo Owner nunca abrió el asistente figura "sin terminar". Solo los bloques nuevos llegan en `null` si fallan.
- **D3-2:** el conteo para "Límites" hace una consulta por gym (N+1); aceptable con pocos gyms.

- **R11-1:** plantillas de rutina solo por gym (sin plantillas del sistema), copia sin vínculo con las rutinas asignadas; el cuerpo se guarda como JSON. Borrado físico. Cualquier staff las crea y borra (¿limitar a Owner/`Trainer`?). Al asignar no se renombra.

- **D1-1:** el límite de miembros del plan sigue como tarjeta aparte (no viaja en el bloque); la afluencia no se filtra por el horario del gym (rango 5–21 h); "por vencer y en gracia" máx. 10 filas, los vencidos pasada la gracia solo como conteo.
- **D1-2:** "Mis miembros" solo para quien tiene `Trainer` (clases de hoy, rutinas por revisar, miembros sin rutina). La caja: con `ViewFinances` se ven todas las abiertas; el resto, solo la suya.
- **MS8-1:** "Mi membresía" (`/app/membresia`) no muestra dinero ni motivos internos; solo se llega desde el perfil (Inicio no enlaza todavía).
- **L7-1:** "Mi progreso" vive en `/app/logros` (pestañas Racha, Logros, Ranking) y `/app/progreso` pasa a llamarse "Mis récords". La barra inferior sigue diciendo "Logros". La mejor racha se recalcula (aproximada, no se guarda); los días futuros de la rutina no se marcan en el calendario.
- **AP4-1:** el cambio de modo usa el `switch-gym` de I2 (sin backend nuevo). En la app nativa, un staff en modo staff verá "no encontrado" hasta AP3 (panel en la app).
- **AP7-1:** las tablas de planes, ejercicios y equipos se apilan en celular con `DataLabel` (no tarjetas de `ServerTable`). Falta mirarlas a 375 px en el navegador.

- **R6-1:** video del ejercicio: solo MP4 hasta 50 MB (la spec no fija límite de video). No hay "quitar medio" sin reemplazarlo; vaciar la URL deja el archivo huérfano en R2. Un archivo compartido con un duplicado no se borra mientras alguien lo use.

- **AP5-1:** el bloqueo en modo staff también salta al abrir la app en frío (no solo tras 15 min en segundo plano); 5 PIN erróneos cierran la sesión. PIN propio de 4–6 dígitos (PBKDF2 con sal). Paquete nuevo `Plugin.Fingerprint` 2.1.5 (solo Android), **sin verificar que compile en .NET 10**; alternativa: `Xamarin.AndroidX.Biometric`. Nada probado en dispositivo.

- **AP3-1:** el panel ya se monta en `GSM.Mobile` y el push de Android usa FCM inicializado por código desde `GET /api/push/config` (sin `google-services.json`). Paquete `Xamarin.Firebase.Messaging` 124.1.2.2 **sin verificar**. Nada probado en dispositivo; falta proyecto de Firebase (`com.gsm.mobile`) y `Notifications:Fcm:*`.
- **AP3-2:** tocar un aviso de otro gym solo navega, no cambia de gym (la spec dice que sí). El staff no tiene "olvidé mi contraseña" en la app. Si el token FCM rota con la app cerrada, se actualiza en la siguiente apertura.

## 3. Gestiones operativas pendientes

Las del plan §4 (ninguna se hace esta noche):

- Servidor y ambiente de pruebas con Dokploy.
- Cuenta de Cloudflare R2.
- Cuenta de Resend y dominio verificado.
- Proyecto de Firebase por ambiente.
- Teléfono Android o emulador (AP3, N3, A6, escáner QR).
- Imágenes de ejercicios y máquinas con licencia.
- Revisión legal (Ley 1581, Ley 2300).
- **Despliegue (DP):** configurar `ForwardedHeaders` detrás del proxy de Dokploy; sin eso, la IP de `logs.security_events` y del límite de peticiones por IP sería la del proxy, no la del cliente (salió de LG2).

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
- **Riesgo: un test de integración falla de forma esporádica** (1 de cada ~5 corridas completas tras unir N1). **Pista (2026-10-11):** los que fallan son tests de migración (`MultiGymAccountTests.Migration_turns_existing_accounts_into_gym_accesses`, `NotificationsTests.The_migration_moves_the_inbox...`); pasan solos. Probablemente bajan y suben migraciones mientras otras clases usan la misma base en paralelo: candidatos a una colección de xUnit sin paralelismo.
- **Incidente — la base de desarrollo `gsm` quedó migrada a la v2.** Dos agentes (CO3 y M2) corrieron `dotnet ef database update` sin `--connection`, y esa herramienta apunta a `gsm` (lee `GSM_MIGRATIONS_CONNECTION`, no `ConnectionStrings__Postgres`). Cada uno revirtió su propia migración, pero las anteriores quedaron aplicadas: **`gsm` está en `V2_G2_GymProfile`**, con sus datos (43 miembros, 24 usuarios, 23 accesos creados por la migración de I2). No se borró nada; M2 avisa que, al pasar por `V2_S7` hacia atrás y adelante, una tabla vacía de métricas se recreó. **Resuelto 2026-10-11:** respaldo en `C:ProyectosGymbrogsm-backupsgsm-antes-v2-2026-10-11.dump` (pg_dump, formato custom) y `gsm` migrada hasta `V2_P13_TrialsNotConverted`; la API arranca contra ella sin errores. Desde entonces los prompts exigen `--connection` explícito.
- **Worktrees:** los agentes trabajan en worktrees creados por la herramienta (`.claude/worktrees/`, ignorado por git), no en `../gsm-lanes/`. `scripts/new-lane.ps1 -SkipWorktree` sirve para crear solo las bases.

## 5. Registro cronológico

- 2026-10-16: tanda C8, L3, G6, R8, M6 unida. La suite completa dio 74 fallos y tardó 45 min: **la tarea de anonimización de M6 recorría todos los gyms con el reloj adelantado hasta 60 meses y anonimizaba miembros (y borraba cuentas) de otros tests en paralelo** (401, 404, concurrencia). Arreglo: la tarea admite `onlyGymId` (solo tests). Bug real encontrado: `UpdateMemberSettingsHandler` añadía la entidad antes de validar (otros 5 handlers de ajustes perezosos tienen el mismo patrón; en producción no se nota porque cada petición tiene su contexto). Después: **1159 integración + 609 bUnit en verde, 12 min**.
- 2026-10-16: unidas R9, CO6, D3, R11, AP6, AP7, AP4, MS8+L7, D1, R6, AP3+N3, AP5 y D2. Conflictos solo en `gsm.css` (bloques al final), `MembersPanelModule` y `Routes.razor` de Mobile. Migración `V2_R11_RoutineTemplates`. **`GSM.Mobile` compila para Android** en esta máquina (Firebase y Plugin.Fingerprint resuelven); no hay emulador. bUnit 663 en verde; el test del kiosco intermitente se estabilizó con esperas de 10 s.
- 2026-10-16: suite de la tanda A7/MS6/C6/L5: 21 fallos de integración + 2 bUnit. Causas: `RewardRow` era un record posicional y EF no traducía el filtro de premios pendientes (tumbaba la tarjeta de recepción); los tests de A7 publicaban con el reloj fijado a mediodía y despachaban antes; un test de MS6 esperaba "Total a cobrar" con devolución. Corregidos (300607f y siguiente). `KioskDeviceUiTests.The_typed_number_is_masked...` falla de forma intermitente.
- 2026-10-16: unidas MS7, C7, L6, S3, A6, R7, G5, M5, S4 y LG5 (dos tandas de agentes que solo escriben código). Un conflicto (dos métodos nuevos en `IIdentityReader`). Migración única `V2_MS7_L6_S3_A6_G5_LG5` con `SqlScript` MS7, L6, G5, LG5. Al integrar: `using GSM.Application.Common` (3), `[EndpointName]` para el onboarding (NSwag numeraba `Onboarding2Async`), **12 clientes sin `ReadResponseAsString`** (8 de tandas anteriores: `CashShifts`, `Owners`, `ClassSettings`, `AdminLogs`… — sus 409/422 llegaban con el mensaje genérico), 2 bUnit (query `t` de A6, cliente de C7 en lista de espera). bUnit 543 en verde. Lanzada la tanda C8, L3, M6 y G6.
- 2026-10-15: L4, CO4, MS5 y E4 unidas (MS5 y L4 combinadas solas en `Detail.razor`). Migración `V2_MS5_E4_L4_CO4` con `SqlScript` MS5, E4, L4, CO4. Al integrar: cliente de la lista de espera sin registrar (hueco de C4), `EndpointName` para `ScheduleAnnouncement` (chocaba con Clases), namespace en un test de E4; un bUnit del kiosco intermitente (pasa 3 de 3). **Suite (en dos mitades): 902 integración + 463 bUnit, todo verde.**
- 2026-10-15: A5, L2, C4 y MS4 unidas (MS4 y C4 se quedaron detenidos una vez y se reanudaron). Conflicto MS4 ↔ L2 en `Membership.cs`: queda el modelo de congelamientos; `CoveredDaysThrough` descuenta los tramos. Migración `V2_MS4_A5_C4_L2` **reordenada**: `SqlScript` MS4/C4/L2 antes de soltar `paused_on`. La primera suite la cortó Claude Code por memoria baja; se corrió en dos mitades. Al integrar: un falso de push en bUnit, `Input` en un campo `Immediate`, conteo de logros por defecto (5 → 7), una fila de logs de corridas anteriores, y **un defecto real de C4**: `UpdateClassSettingsHandler` agregaba la fila antes de validar (un 422 dejaba la fila rastreada). B4 marcado corregido. **Suite: 850 integración + 416 bUnit, todo verde.**
- 2026-10-15: MS3, A4, R5 y LG4 unidas sin conflictos (se cortaron una vez por el límite de uso y se reanudaron). Migraciones `V2_MS3_A4_R5` (EF detectó el renombrado de `duration_days`; `SqlScript` MS3, A4, R5) y `V2_LG4_PanelErrores`. Al integrar: 1 `using` (R5), `AlternativesAllAsync`, el parámetro `documentOnly` en medio de `AttendanceGETAsync`, 5 bUnit y 1 test de A4 con planes duplicados. **Suite: 800 integración + 390 bUnit, todo verde.** Lanzada la tanda MS4, A5, C4 y L2.
- 2026-10-15: A3, MS2, LG3 e I4+G4 unidas (MS2 y A3 combinadas solas en `CheckinTab`). Migraciones `V2_MS2_I4` (`SqlScript("MS2")`, `SqlScript("I4")`) y `V2_LG3_Logs`. Al integrar: 1 `using` (LG3), métodos del panel con sufijo `2` (I4), 18 bUnit (autorización de prueba en recepción por el `RequireRole` de A3, selectores, popover, `Change`) y 2 tests de I4 con contraseñas distintas. **Suite: 756 integración + 367 bUnit, todo verde.**
- 2026-10-15: lanzada la tanda I4+G4, A3, MS2 y LG3 (solo escriben código).
- 2026-10-15: R3b, N1b, S1b y LG2 unidas sin conflictos. Migraciones `V2_R3b_N1b_S1b` (enum + 2 columnas + `SqlScript("N1b")`) y `V2_R3b_LG2_Scripts` (`R3b`, `LG2`). Al integrar: `OnRejected` devuelve `ValueTask` y el test de login ya no exige IP. **La primera corrida dio 10 fallos de LG2 que no se repitieron en dos corridas más** (probablemente la corrida que aplicó las migraciones nuevas); un bUnit de `CommercialStatusUiTests` falló una vez y pasó 3 de 3. **Suite: 722 integración + 339 bUnit, todo verde.**
- 2026-10-15: MS1b, G3b, R2b y L1 unidas. Conflicto en `AchievementMetrics` (L1 extrajo `ComputeStreakAsync`, R2b cambió el inicio del calendario). Migración `V2_R2b_RoutineDayAddedOn`. Al integrar: `Is24Hours` sale `bool?` en el cliente (NSwag con propiedades `init`), test rotativo de R2b añadido y dos expectativas corregidas. **Suite: 691 integración + 334 bUnit, todo verde.**
- 2026-10-15: suite de CO5 corrida en primer plano: **668 integración + 329 bUnit, todo verde. Ola 3 cerrada.** Quitados los 25 worktrees de tareas ya unidas (ramas conservadas; se deja el del primer intento de P2, con 2 archivos sin commitear). Lanzada la tanda: MS1b, G3b, R2b y L1 (solo escriben código).
- 2026-10-15: CO5 unida; migración `V2_CO5_Cumpleanos`, cliente regenerado, todo compila (1 `using`). **La suite no se pudo correr**: Claude Code la detuvo por memoria baja del sistema. Pendiente correrla antes de dar por cerrada la Ola 3.
- 2026-10-15: N5 y CO2 unidas sin conflictos. Migración `V2_N5_CO2` (valores nuevos del enum `notification_type` + audiencia de anuncios). Al integrar: 2 errores de UI (`MultiSelectionTextFunc` recibe textos, no Guid) y 2 bUnit (`data-testid` en `MudSwitch` queda en el `<input>`). **Suite: 655 integración + 326 bUnit, todo verde.** De la Ola 3 solo queda CO5.
- 2026-10-15: lanzadas CO2 y N5. CO5 queda para después (mismo carril que CO2 y usa N5).
- 2026-10-15: CO7, C2 y E3 unidas sin conflictos. Migración `V2_C2_E3` con `SqlScript("E3")` al final. Al integrar: 1 `using`, 2 nombres de cliente (NSwag ignoró los `EndpointName` de E3: `EquipmentAsync`/`EquipmentItemsAsync`), 2 tests que no compilaban, 4 bUnit (menú con "Mis clases", popover) y 2 de C2 (clase a las 7:00 con el reloj fijo a mediodía). **Suite: 638 integración + 323 bUnit, todo verde.** De la Ola 3 quedan CO2, CO5 y N5.
- 2026-10-15: lanzada la cuarta tanda: C2, E3 y CO7 (3 agentes, tras el corte por límite de uso). Quedan de la Ola 3: CO2, CO5 y N5.
- 2026-10-15: C3, R4 y S2 se cortaron por el límite de uso; reanudadas sin pérdida. A2, S2, C3 y R4 unidas sin conflictos. Migración `V2_A2_S2_C3_R4` **reordenada a mano**: EF ponía los `DropColumn` de R4 antes de copiar los datos; ahora va crear → `SqlScript("S2")` → `SqlScript("R4")` → `DropColumn`. Claves JSON de R4 verificadas (PascalCase, sin renombre de la convención). Al integrar: 1 error de backend, 10 de UI (`??` sobre valores no anulables, `PlansAllAsync`), 4 tests bUnit, 1 test de rutinas y **un defecto real de A2**: la lista por miembro ignoraba "ver anuladas". **Suite: 615 integración + 308 bUnit, todo verde.**
- 2026-10-14: lanzada la tercera tanda: C3, A2, R4 y S2.
- 2026-10-14: C5, A1b, M4 e I6 unidas sin conflictos. Migración `V2_C5_A1b_M4_I6`, cliente regenerado. Al integrar: cuarto defecto de NSwag (defaults `int` en `ReceptionCardResponse` salían como texto; se quitaron los defaults), 1 error de UI y un test de C5 que no sembraba membresía (exigida desde C1). **Suite: 574 integración + 279 bUnit, todo verde.**
- 2026-10-14: lanzada la segunda tanda de la noche: C5, A1b, M4 e I6 (solo escriben código).
- 2026-10-14: C1, M3, I5 y P3 unidas. Migración única `V2_P3_M3_I5` (con `SqlScript("P3")`), cliente regenerado. Al integrar: 1 `using` en Payments, 8 errores de UI (parámetro nuevo `withHealthAlerts`, `bool?` del cliente, `InputMode`), 7 tests bUnit (parámetro de query, popover, `Change` en vez de `Input`, semana actual) y 15 de C1 (helpers que liberaban el scope sin esperar la tarea). **Suite: 528 integración + 260 bUnit, todo verde.**
- 2026-10-14 (noche): lanzadas C1 (rama `v2-C1-clases-horario-b`), P3 (corrige B2), M3 e I5; los agentes solo escriben código. Preguntas nuevas irán a §2 para responder mañana.
- 2026-10-12: primera tanda con agentes que solo escriben código (A1, M1b, G1+S1). Se cortaron una vez por conexión y se retomaron. Al integrar: backend compiló a la primera; 2 errores de compilación y 3 de tests, todos menores. Migración única `V2_A1_M1b_MotivosYEntrenador`. **Suite: 460 + 234 en verde.**
- 2026-10-12: E2, P2, I7 y N2 unidas, con la nota al rechazar (P2) y "promoción no es importante" (N2) añadidas al unir. **Suite completa en verde: 432 integración + 219 bUnit.** Dos cortes de la suite por memoria en segundo plano: correrla en primer plano.
- 2026-10-11: resueltas las 22 preguntas (ver §2). Script del cliente arreglado (curl.exe en PS 5.1) y `new-lane.ps1` con `--context`. Base `gsm` respaldada y migrada. **Nuevo límite: 3–4 agentes a la vez** (`V2-BUILD-PLAN.md` §3). Lanzadas N2, P2, I7 y E2; I7 y E2 en sus filas cuando terminen.
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
