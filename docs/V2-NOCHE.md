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
| T1 | En curso | — |
| I2 | En curso | camino crítico |
| P1 | En curso | — |
| R2 | En curso | — |
| S5 | **Retenida** | depende de I2 (ver §4) |

Línea base antes de empezar: 160 integración + 68 bUnit, todo verde.

## 2. Preguntas para el dueño

Ninguna bloquea trabajo de esta noche; todas afectan a tareas futuras.

1. **LG1 → LG4: ¿se pueden reintentar los eventos con campos sensibles?** `GSM-logs.md` §3 manda limpiar `code`, `token`… antes de guardar, y §4.3 pide guardar el evento completo "para poder reintentarlo". Choque: `MemberInvited.Code` queda `[REDACTED]`, y reintentarlo enviaría un correo sin código. Opciones: (a) esos eventos se marcan como no reintentables en el panel; (b) el payload de `event_handler_failures` no se limpia de códigos de un solo uso (sí de contraseñas y salud); (c) el reintento vuelve a leer el dato del módulo dueño en lugar del payload.
2. **LG1 → LG4: código de rastreo corto.** El `traceId` es de 32 hex y el código que ve el usuario son sus 6 primeros caracteres (§3: "código 7F3A2C"). Dos peticiones pueden compartir el código corto, así que la búsqueda en LG4 va por prefijo y puede devolver más de una. Opciones: (a) aceptarlo (colisiones rarísimas a este volumen); (b) generar un código corto único aparte.
3. **LG1: tope del payload de eventos.** El agente no recortó el payload a 4 KB (un JSON cortado no se puede reintentar); el tope es 64 KB y por encima se guarda `{"_truncated":true}`. Aceptado provisionalmente; confirmar.
4. **R1: registrar sobre una línea retirada.** La spec no lo dice; el agente respondió **409**. Y mover una línea de un día a otro conservando su id se rechaza con 422 (hay que retirar y crear). Confirmar ambas.
5. **R1 → R2: racha y días retirados.** Al retirar un día, deja de contar en el calendario de racha **también hacia atrás** (el calendario solo ve los días vigentes). Para el modo semanal (R2) conviene decidir si la racha se calcula con el calendario vigente en cada fecha.

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
- **Worktrees:** los agentes trabajan en worktrees creados por la herramienta (`.claude/worktrees/`, ignorado por git), no en `../gsm-lanes/`. `scripts/new-lane.ps1 -SkipWorktree` sirve para crear solo las bases.

## 5. Registro cronológico

- Línea base: 160 + 68 en verde.
- Lanzadas T1, I2, P1 y R2 (4 agentes).
- AP1 unida y verificada en el navegador. LG1 unida (con arreglo del `.gitignore`); 175 + 73. R1 unida; 183 + 74. B1 marcado corregido en `BUGS.md`.
- X0 unida (b2249ce); suite 162 + 68 en verde. Lanzadas AP1 y LG1 en paralelo (`gsm_test_ap1`, `gsm_test_lg1`).
- Inicio. Leídos `CLAUDE.md`, `docs/V2-BUILD-PLAN.md`, `docs/BUGS.md`. Creada `gsm_test_x0`.
