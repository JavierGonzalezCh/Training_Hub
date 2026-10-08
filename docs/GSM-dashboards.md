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

## 4. Pendiente de decidir

- Nada por ahora.
