# Dashboards — replanteamiento (v2)

> **Estado:** propuesta en revisión (2026-10-07). Nada de esto está construido. Junta datos de casi
> todos los módulos; cada bloque indica de dónde sale. Ver también `GSM-apps-y-vistas.md` (celular y
> escritorio).

## 1. Panel del gym (`/`)

**Hoy:** miembros activos, entradas de hoy, ingresos del mes, por vencer en 7 días, afluencia por
hora, últimas entradas, intentos rechazados y membresías por vencer. Igual para todos.

**v2:** cambia según los permisos de quien entra.

| Bloque | Contenido | Quién lo ve | Origen |
|---|---|---|---|
| Para completar | Pasos opcionales del onboarding que faltan | Owners, hasta completarlos | Gyms §5 |
| Letrero de suscripción | Prueba terminada, mora, inhabilitación próxima, límite de miembros | Owners | SuperAdmin §V2–§V3 |
| **Indicadores** | Miembros al día frente al límite del plan · entradas hoy · personas adentro ahora y % del aforo · por vencer, en gracia y vencidos | Todos | Memberships §10, Attendance §8, SuperAdmin §V1 |
| | Ingresos del mes contra los mismos días del mes anterior (corrige B2) | Owner y `ViewFinances` | Payments |
| **Requiere atención** | Máquinas fuera de servicio y reportes abiertos (inseguras primero) · intentos rechazados hoy | Todos | Equipment, Attendance |
| | Solicitudes de anulación pendientes | Owner y `VoidPayments` | Payments §6 |
| | Caja abierta (por quién, efectivo esperado) · caja olvidada | Owner y `ViewFinances`; el resto ve solo su propia caja | Payments §4 |
| **Hoy** | Clases de hoy con reservas, cupo y lista de espera · cumpleaños de hoy · últimas entradas | Todos | Classes, Communications §F, Attendance |
| Afluencia por hora | Últimos 30 días, solo horas de apertura | Todos | Attendance, Gyms §3.1 |
| Por vencer y vencidos | Lista con botón de WhatsApp y "contactado hace N días" | Todos | Memberships, Communications §B |
| **Mis miembros** | Mis clases de hoy · rutinas por revisar · mis miembros sin rutina | Entrenadores (`Trainer`) | Classes, Routines |

**En el celular** (app o navegador angosto), el orden cambia para lo que se mira de pie en el gym:

1. Requiere atención.
2. Indicadores, en dos filas compactas.
3. Hoy (clases y cumpleaños).
4. Mis miembros (entrenadores).
5. Por vencer y vencidos.

La afluencia por hora y la lista "Para completar" quedan al final.

## 2. Inicio del miembro (`/app`)

**Hoy:** saludo, estado, plan con barra de días, escanear, racha y congeladores, entrenamiento de hoy,
últimas visitas.

**v2**, en orden:

1. **Saludo y estado:** estado comercial; plan con barra de días o "4 de 12 entradas"; letrero cuando
   quedan 7 días o menos (Memberships §5.3).
2. **El gym ahora:** abierto o cerrado, con la hora de cierre o apertura, y la ocupación (tranquilo,
   moderado, lleno) (Gyms §3.1, Attendance §8).
3. **Entrada:** "Escanear", o "Entraste hoy a las 6:12".
4. **Pendientes:** ofertas de la lista de espera por confirmar, con plazo; premios por reclamar
   (Classes, Achievements §4).
5. **Tu entrenamiento de hoy:** el día de la rutina o descanso; fecha de revisión si está cerca
   (Routines).
6. **Tu próxima clase:** la siguiente reserva y enlace al horario (Classes).
7. **Tu progreso:** racha, congeladores y posición en el ranking si participa; abre "Mi progreso"
   (Achievements §8).
8. **Novedades:** el anuncio fijado y cuántos hay sin leer (Communications).

"Últimas visitas" sale: el calendario de "Mi progreso" lo muestra mejor.

**En escritorio:** dos columnas. Izquierda: 1, 3, 5. Derecha: 2, 4, 6, 7, 8.

## 3. Panel del SuperAdmin (`/admin`)

Solo web.

| Bloque | Contenido |
|---|---|
| Ingresos | MRR · cobrado este mes contra el anterior · contratado contra cobrado (cartera pendiente) |
| Requiere acción | Pruebas terminadas sin pago (días antes de inhabilitarse) · en mora · por inhabilitarse · vencen en 15 días |
| Arranque | Owners sin activar la cuenta y onboardings sin terminar, con días desde el alta y "Reenviar acceso" |
| Límites | Gyms al 90 % o más de su límite de miembros |
| Cartera por estado | Prueba, prueba terminada, activos, en mora, inhabilitados, suspendidos |
| Uso por gym | Miembros frente al límite, entradas del mes, ingresos de sus miembros, último movimiento y **alerta de inactividad** (7 días sin entradas) |
| Evolución | 12 meses: MRR, gyms activos, bajas, miembros totales (SuperAdmin §V4) |
| Plataforma | Totales de miembros y entradas |

## 4. Reglas comunes

- **Los dashboards no son un módulo de negocio.** No tienen tablas ni escriben nada: son compositores,
  como `GSM.Application/MemberStreak/`. Viven en `GSM.Application/Dashboard/` (ya existe para el panel
  actual) y leen **solo** por los `I{Módulo}Reader` de cada módulo. Si un bloque necesita un dato que
  ningún reader ofrece, se añade un método de lectura al reader del dueño (§6); nunca se consulta su
  `DbContext`.
- **Un endpoint por dashboard**, que devuelve todos los bloques de una vez. Un bloque que quien entra no
  puede ver **no viaja** (queda `null`), no responde 403 (decidido 2026-10-08): el panel lo oculta. Los permisos son los de
  `GSM-modulo-identity.md` §5 (`ViewFinances`, `VoidPayments`, `Trainer`).
- **Un bloque que falla no tumba el dashboard.** Si un reader lanza, el bloque vuelve `null` con su
  error registrado en `logs.errors` (`GSM-logs.md` §4.1), y el resto se muestra.
- **Días y meses de negocio** con `IClock.Today` y `IClock.BusinessDayRange`, nunca rangos UTC (es el
  origen de B2 y de otros dos defectos del proyecto).
- **Sin caché ni tiempo real.** Se recalcula al abrir la página y con "Actualizar". Excepción: el
  indicador de "personas adentro ahora" y la caja abierta se refrescan solos cada 60 s mientras la
  página está visible (decidido 2026-10-08). Si una consulta resulta lenta, `logs.slow_requests` lo dirá antes de cachear.
- **Los listados del dashboard son cortos** (máximo 5–10 filas) con "Ver todos", que lleva a la pantalla
  del módulo con el filtro ya puesto. El dashboard no pagina.
- **Impersonación:** el SuperAdmin impersonando ve el panel del gym como un Owner.

## 5. Contratos

| Endpoint | Quién | Sustituye |
|---|---|---|
| `GET /api/dashboard` | Owner y staff del gym | El mismo endpoint de hoy; la respuesta cambia de forma (bloques opcionales del §1) |
| `GET /api/me/home` | Miembro | Nuevo; hoy la pantalla junta varias llamadas (`/api/me/streak`, rutina, clases, feed) |
| `GET /api/admin/dashboard` | SuperAdmin | El mismo endpoint de hoy, ampliado con los bloques del §3 |

- La respuesta de cada uno es un objeto con una propiedad por bloque (`attention`, `indicators`,
  `today`, `finances`, `myMembers`…), todas anulables. Los enums sueltos o en colección viajan como
  `string` en los DTOs (defectos de NSwag de F6 y de la etapa 12).
- Los parámetros de la vista se reducen al mínimo: `GET /api/dashboard` no recibe filtros; la afluencia
  usa siempre los últimos 30 días.

## 6. Lo que cambia en el resto

Lecturas nuevas que cada módulo dueño expone en su `Public/` (solo lectura; cada sub-etapa del §7 añade
las que necesita, y si ya las añadió la tarea del módulo, las reutiliza):

| Módulo | Lectura |
|---|---|
| Memberships | Conteo por estado comercial (al día, por vencer, en gracia, vencidos); lista corta de por vencer y vencidos |
| Attendance | Entradas de hoy, personas adentro y % del aforo, rechazos de hoy, afluencia por hora de 30 días, últimas entradas |
| Payments | Cobrado en un rango de días de negocio; solicitudes de anulación pendientes; cajas abiertas y olvidadas |
| Equipment | Máquinas fuera de servicio y reportes abiertos, inseguras primero |
| Classes | Sesiones de hoy con reservas, cupo y lista de espera; próxima reserva del miembro; ofertas de lista de espera por confirmar |
| Communications | Cumpleaños de hoy; último contacto por WhatsApp de un miembro; anuncio fijado y no leídos |
| Routines | Entrenamiento de hoy del miembro; rutinas por revisar y miembros sin rutina de un entrenador |
| Achievements | Racha, congeladores, posición en el ranking, premios por reclamar |
| Gyms | Abierto/cerrado y próxima apertura o cierre (`IGymReader.IsOpenAt`), pasos de onboarding pendientes |
| SuperAdmin | Estado de la suscripción y límite de miembros (bloque `subscription` actual); métricas mensuales (§V4) |

Frontend: el dashboard del panel vive en `GSM.Panel.UI` y el inicio del miembro en `GSM.Member.UI`,
con el orden de celular y de escritorio de los §1–§2 (`GSM-apps-y-vistas.md` §5–§6).

## 7. Propuesta de sub-etapas

Van en la Ola 5 (`V2-BUILD-PLAN.md`), cuando ya existen casi todos los datos que muestran. Un bloque
cuyo módulo no esté construido todavía **no se inventa**: se deja fuera y queda anotado en la entrega.

| Sub-etapa | Qué | Depende de |
|---|---|---|
| **D1** | Panel del gym: `GET /api/dashboard` por bloques y permisos, orden de celular, refresco de 60 s del aforo y la caja. Reutiliza el comparativo de ingresos corregido en P3 (B2) con su test. | MS1, A5, P2–P3, E4, C4, CO3, CO5, G3, G5, I3, AP2 |
| **D2** | Inicio del miembro: `GET /api/me/home`, ocho bloques del §2 en una llamada, dos columnas en escritorio; sale "Últimas visitas". | MS1, G3, A5, C4, L5, L6, R8, MS8 + L7, AP6 |
| **D3** | Panel del SuperAdmin: bloques del §3, alerta de inactividad (7 días sin entradas), "Reenviar acceso" desde Arranque, evolución de 12 meses. | S1–S3, S7, G1, G5 |

Criterios de aceptación comunes, cada uno con su test:

1. Un usuario sin el permiso de un bloque recibe ese bloque en `null`, no un 403.
2. Un reader que lanza deja su bloque en `null` y el resto de la respuesta llega.
3. Los conteos por día se calculan en la zona del gym (test con `IClock` a las 20:00 hora de Colombia).
4. Los datos de otro gym nunca aparecen (multi-tenancy), salvo en D3.

## 8. Pendiente de decidir

- Nada por ahora.
