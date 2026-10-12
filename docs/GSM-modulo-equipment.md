# Módulo Equipment (Equipos) — especificación

> **Estado:** propuesta en revisión (2026-10-07). Nada de esto está construido. Módulo nuevo que
> nace del replanteamiento de Routines (`GSM-modulo-routines.md`, sección "Catálogo de ejercicios").

**Schema:** `equipment` · **Lo consume:** Routines (por `IEquipmentReader`) · **Consume:** Members
(`IMemberReader`, para los reportes de miembros)

## 1. Propósito

El inventario físico del gym: **qué máquinas y equipos tiene, cómo son (foto), dónde están y si se
pueden usar hoy.** Hoy el sistema no sabe nada de eso. Solo existe una lista fija de tipos de equipo
pegada a cada ejercicio, donde "máquina" vale lo mismo para una prensa que para una extensión de
cuádriceps.

Alcance decidido (2026-10-07):

| Nivel | Qué | Estado |
|---|---|---|
| 1 | El gym sabe qué tipos de equipo tiene, y el catálogo y las alternativas lo respetan | Entra |
| 2 | Inventario de máquinas concretas, **elegidas de un catálogo del sistema**, con foto, zona y ejercicios ligados | Entra |
| 3 | Estado (disponible, mantenimiento, fuera de servicio), historial, reportes de fallas y notificaciones | Entra |
| — | Mantenimiento preventivo | **Después** |
| 4 | QR pegado en cada máquina | **Después** |
| — | Inventario como paso obligatorio del onboarding del gym | **Después** (§10) |

## 2. Decisiones tomadas

| Tema | Decisión |
|---|---|
| Cómo se arma el inventario | El owner o el staff **eligen sus máquinas de un catálogo del sistema** (con imagen de referencia) e indican cuántas tienen. Si una máquina no está en el catálogo, la crean como propia. |
| Quién gestiona | **Owner y staff** agregan máquinas, cambian estados y atienden reportes. **Solo el Owner** da de baja una máquina. |
| Reportes de miembros | Permitidos por defecto, con el flag del gym `MembersCanReportEquipment` para apagarlos. |
| Notificaciones | **Cada reporte de falla avisa al owner y al staff** en el panel (§7). |
| Reportar mientras entrena | En la app, la pantalla del ejercicio que el miembro está haciendo tiene un botón **"Reportar falla"** de dos toques (§6). |

## 3. Modelo de datos

### 3.1 `EquipmentType` (enum, cambia de dueño)

Es la lista que hoy vive en Routines como `Equipment` (barra, mancuerna, kettlebell, máquina, polea,
multipower, banco, rack, banda elástica, TRX, balón medicinal, colchoneta, cajón, cuerda, peso
corporal, máquina de cardio). **Pasa a ser de este módulo y se llama `EquipmentType`**, para que el
nombre del módulo no choque con el del enum.

- Los valores no cambian (`smith_machine`, `cardio_machine`…), así que el JSON de la API sigue igual.
- Routines lo sigue usando en `Exercise.Equipment`: "este ejercicio necesita estos tipos de equipo".
- La migración mueve el tipo de Postgres al schema `equipment`.

### 3.2 `EquipmentModel` — catálogo del sistema (nuevo)

Las máquinas y equipos comunes de un gym, para que nadie tenga que escribirlos desde cero. Es global,
como el catálogo de ejercicios del sistema, y **solo lo edita el SuperAdmin**.

| Campo | Tipo | Notas |
|---|---|---|
| `Id` | uuid | |
| `Name` | texto ≤ 120 | "Prensa de piernas 45°", "Multipower", "Polea doble (crossover)", "Banco plano". |
| `Type` | `EquipmentType` | |
| `ImageUrl` | texto | **Imagen de referencia**, para que se reconozca la máquina aunque el gym no suba su foto. |
| `Description` | texto ≤ 1000 | Qué es y para qué sirve. |
| `DefaultInstructions` | texto ≤ 1000 | Ajustes típicos. El gym puede reemplazarlos por los de su máquina. |
| `TrackedPerUnit` | bool | `true` en máquinas (se registra una por unidad); `false` en implementos sueltos (mancuernas, discos), que van con cantidad. |
| `IsActive` | bool | |

- Se siembra por **migración de datos** (~40 modelos: prensas, poleas, máquinas de pecho y espalda,
  bancos, racks, multipower, cintas, bicicletas, elípticas, remos, mancuernas, discos, kettlebells…),
  porque tiene que existir también en producción.
- **Las imágenes de referencia son trabajo de contenido, no de código**, y necesitan licencia: no se
  pueden copiar de internet. Mientras falten, el catálogo muestra un ícono por tipo.

### 3.3 `EquipmentItem` — una máquina o equipo del gym

| Campo | Tipo | Oblig. | Notas |
|---|---|---|---|
| `Id`, `GymId` | uuid | | |
| `ModelId` | uuid? | No | Modelo del catálogo del que salió. `null` = máquina propia del gym, creada a mano. |
| `Name` | texto ≤ 120 | Sí | Se precarga del modelo y se puede cambiar: "Prensa 45°", "Bicicleta 3". |
| `Type` | `EquipmentType` | Sí | Se copia del modelo. |
| `PhotoUrl` | texto | No | **Foto de la máquina real**, subida a R2. Si falta, se muestra la imagen del modelo. Si tampoco hay, el ícono del tipo. El panel marca "sin foto propia". |
| `Zone` | texto ≤ 60 | No | "Zona de pierna", "Segundo piso". Autocompleta con las zonas ya usadas en el gym. |
| `Brand`, `Model` | texto | No | |
| `Instructions` | texto ≤ 1000 | No | Se precarga del modelo. Ajustes de esta máquina; lo ve el miembro. |
| `Quantity` | int ≥ 1 | Sí | Solo mayor que 1 en implementos sueltos (`TrackedPerUnit = false`). |
| `Status` | enum `available`, `maintenance`, `out_of_service` | Sí | §4. |
| `ExpectedBackOn` | `DateOnly?` | No | En mantenimiento o fuera de servicio: cuándo se espera que vuelva. |
| `AcquiredOn` | `DateOnly?` | No | |
| `Notes` | texto ≤ 1000 | No | Internas, solo staff. |
| `IsActive` | bool | | Baja lógica (se vendió o se botó). Distinto de "fuera de servicio". |

**Unidades:** las máquinas (`TrackedPerUnit = true`) se registran **una fila por unidad**, porque
importa saber cuál está dañada. Si el gym elige "Bicicleta estática × 3", se crean "Bicicleta 1",
"Bicicleta 2" y "Bicicleta 3". Los implementos sueltos van en una sola fila con cantidad, y el estado
vale para el conjunto.

### 3.4 `EquipmentStatusChange` — historial de estado

Una fila por cambio: `ItemId`, estado anterior y nuevo, `Reason` (obligatorio al pasar a
mantenimiento o fuera de servicio), `IssueId?` (si viene de un reporte), quién, cuándo. Solo se
añaden filas. Es el historial de mantenimiento de la máquina.

### 3.5 `EquipmentIssue` — reporte de falla

| Campo | Notas |
|---|---|
| `ItemId`, `GymId` | |
| `ReportedByUserId`, `ReportedByRole` | Miembro o staff. |
| `Reason` | enum `not_working`, `damaged_part`, `strange_noise`, `missing_part`, `dirty`, `unsafe`, `other`. Es lo que permite reportar con dos toques. |
| `Description` | Opcional, ≤ 1000. **Obligatoria si `Reason = other`.** |
| `PhotoUrl` | Opcional. |
| `Status` | `open`, `in_progress`, `resolved`, `dismissed`. |
| `ResolvedAt`, `ResolvedByUserId`, `Resolution` | Al cerrar. `Resolution` es obligatorio. |
| `CreatedAt` | |

## 4. Reglas de estado

- **Solo `available` se puede usar.** `maintenance` (en arreglo o mantenimiento programado) y
  `out_of_service` (dañada, sin fecha) sacan la máquina de las rutinas de ese día.
- **Un reporte no cambia el estado por sí solo.** El staff lo revisa y decide. Así un reporte
  equivocado o malintencionado no saca una máquina de servicio.
  - **Excepción: `unsafe`** (puede lastimar a alguien). Tampoco cambia el estado, pero la notificación
    sale marcada como **urgente** (§7).
- Al pasar un reporte a `in_progress`, el panel ofrece poner la máquina en mantenimiento en el mismo
  paso.
- Al volver a `available` se borra `ExpectedBackOn`, y los reportes abiertos de esa máquina ofrecen
  cerrarse.
- **Al resolverse un reporte**, el miembro que lo hizo recibe "La Prensa 45° que reportaste ya funciona.
  ¡Gracias por avisar!" (Notifications). Un reporte descartado no genera aviso.
- Si `ExpectedBackOn` pasa y la máquina sigue sin volver, aparece como **"vencida"**. Se calcula; no
  hay tarea programada.
- **Alargar el arreglo** (decidido 2026-10-11): repetir el estado actual (mantenimiento o fuera de servicio) con una fecha de regreso distinta y motivo mueve `ExpectedBackOn` y queda en el historial. Repetir sin fecha nueva, o repetir `available`, es 409. En el panel aparece como "{estado} — actualizar fecha de regreso".
- Varios reportes de la misma máquina se agrupan en la notificación ("3 reportes de la Prensa 45°").
  Un miembro tiene como máximo un reporte abierto por máquina.

## 5. Nivel 1 y disponibilidad de ejercicios

**Tipos de equipo del gym:** salen del inventario. El gym tiene un tipo si existe al menos un
`EquipmentItem` activo y disponible de ese tipo. El peso corporal siempre está disponible.

**Un gym sin inventario cargado no filtra nada:** todos los ejercicios cuentan como disponibles. Es el
mismo criterio que con la suscripción: ante un dato que falta, se deja pasar. Esta regla se puede
retirar cuando el inventario sea obligatorio en el onboarding (§10).

**Ejercicios ligados a máquinas** — la relación vive en Routines, en dos niveles:

- **Del sistema:** `routines.exercise_equipment_models` (`exercise_id`, `model_id`). Los ejercicios
  del sistema vienen ligados a los modelos del catálogo; los liga el SuperAdmin.
  **Cuando el gym elige "Prensa de piernas 45°" en su inventario, la "Prensa de piernas" del catálogo
  de ejercicios queda ligada sola a su máquina.** No hay que configurar nada más.
- **Del gym:** `routines.exercise_equipment_items` (`gym_id`, `exercise_id`, `equipment_item_id`), para
  sus máquinas propias o para corregir el ligado automático.

**¿Se puede hacer un ejercicio hoy en este gym?**

1. Si está ligado a máquinas del gym (directo, o por el modelo): sí, si **al menos una** está
   disponible.
2. Si no: sí, si el gym tiene disponibles **todos** los tipos de equipo que pide el ejercicio.
3. Si el gym no tiene inventario: sí.

Routines resuelve esto leyendo `IEquipmentReader`. **Este módulo no sabe nada de ejercicios ni de
rutinas**: la dependencia va en un solo sentido, Routines → Equipment.

**Dónde se nota:**

- **Catálogo de ejercicios:** filtro "Solo lo que se puede hacer en mi gym", activo por defecto cuando
  hay inventario.
- **Alternativas sugeridas:** nunca proponen un ejercicio que no se puede hacer hoy.
- **Editor de rutinas:** avisa si una línea usa una máquina en mantenimiento ("Prensa 45° en
  mantenimiento hasta el 15 oct"). No impide guardar, porque la rutina dura varias semanas.

## 6. App del miembro

En la pantalla del ejercicio que está haciendo:

- **La foto de la máquina** (la del gym o, si falta, la de referencia), su zona y sus instrucciones de
  ajuste, además de la imagen o el video del ejercicio.
- Si hoy no se puede hacer: "La Prensa 45° está en mantenimiento", con **"Hacer otro ejercicio hoy"**
  y sus alternativas.
- **Botón "Reportar falla"** siempre visible mientras entrena:
  1. Elige el motivo (no funciona, pieza dañada, ruido raro, falta una pieza, sucia, **insegura**,
     otro).
  2. Si quiere, escribe una línea o toma una foto.
  3. Envía. Ve "Gracias, el gym ya fue avisado". Si la máquina tiene varias unidades, se reporta la
     que tiene asignada el ejercicio; si hay más de una posible, elige cuál.
- Si ya tiene un reporte abierto de esa máquina, el botón muestra "Ya reportaste esta máquina" y el
  estado del reporte.

## 7. Notificaciones al owner y al staff

**Todo reporte nuevo genera una notificación en el panel** para el owner y todo el staff del gym.

- Aparece en la **campana del panel**, la misma que hoy avisa de los intentos de check-in rechazados.
  Pasa a ser una campana general con dos tipos de aviso.
- El texto: "**Prensa 45°** — pieza dañada · reportado por Ana Torres · 18:40". Al tocarla se abre el
  reporte, con los botones "En revisión", "Poner en mantenimiento" y "Descartar".
- **Los reportes `unsafe` salen como urgentes:** van primero, en rojo, y el panel muestra además un
  aviso en pantalla.
- Sin leer hasta que alguien del staff abre el reporte. Basta con que una persona lo abra para que
  deje de estar "sin ver", como pasa hoy con los intentos rechazados.
- **El panel consulta la campana periódicamente**, como hoy. No hay tiempo real (WebSockets) ni
  notificaciones push, porque no existen en el sistema.
- **Correo al owner en los reportes `unsafe`**, por la cola de correos de Comunicaciones (evento
  `EquipmentIssueReported`, que Comunicaciones consume). Los demás reportes no generan correo.

## 8. Operaciones

### Owner y staff (panel, nueva sección "Equipos")

| Endpoint | Quién | Qué hace |
|---|---|---|
| `GET /api/equipment/models` | Staff | Catálogo del sistema, con filtros por tipo, para elegir. |
| `POST /api/equipment/from-models` | Staff | Agrega al inventario varios modelos de una vez, con cantidad. Crea una fila por unidad donde corresponde. |
| `GET /api/equipment` | Staff | Inventario, con filtros por tipo, zona y estado. |
| `POST` / `PUT /api/equipment/{id}` | Staff | Crear una máquina propia o editar cualquiera. |
| `POST /api/equipment/{id}/photo` | Staff | Subir la foto. |
| `DELETE /api/equipment/{id}` | **Owner** | Baja lógica. |
| `POST /api/equipment/{id}/status` | Staff | Cambiar estado (motivo obligatorio si no es `available`). |
| `GET /api/equipment/{id}/history` | Staff | Historial de estado y reportes. |
| `GET /api/equipment/issues` | Staff | Reportes, urgentes y abiertos primero. |
| `PUT /api/equipment/issues/{id}` | Staff | Cambiar el estado del reporte o cerrarlo. |

**Pantalla "Agregar del catálogo":** una cuadrícula con la imagen de cada modelo, agrupada por tipo.
Se marcan los que el gym tiene, se pone la cantidad y la zona, y se guarda todo de una vez.

El dashboard suma una tarjeta: **máquinas fuera de servicio y reportes abiertos**.

### Miembro (app)

| Endpoint | Qué hace |
|---|---|
| `GET /api/me/equipment/{id}` | Foto, zona, instrucciones y estado de una máquina. |
| `POST /api/me/equipment/{id}/issues` | Reportar una falla (motivo, línea y foto opcionales). |
| `GET /api/me/equipment/issues` | Sus reportes y en qué van. |

### SuperAdmin

CRUD de `EquipmentModel` y su imagen, y el ligado de ejercicios del sistema a modelos.

## 9. Anonimización de miembros

Los reportes de un miembro anonimizado **se conservan**, porque son el historial de la máquina. Se
vacía la referencia a quién reportó. La descripción se conserva: habla de la máquina, no de la persona.
Lo hace un manejador de `MemberAnonymized` (`GSM-modulo-members.md` §11).

## 10. Después

- **Mantenimiento preventivo:** un intervalo por máquina ("lubricar cada 30 días") y la lista
  "mantenimiento por hacer". Se calcula al vuelo, sin scheduler.
- **QR en cada máquina** (nivel 4): al escanearlo, el miembro ve los ejercicios de esa máquina con su
  video y puede reportar una falla. Reutiliza el QR firmado que ya existe para el check-in.
- **Inventario en el onboarding del gym:** al dar de alta un gym, el owner pasa por un asistente donde
  elige sus máquinas del catálogo (la misma pantalla "Agregar del catálogo"). El objetivo es que
  **ningún gym opere sin máquinas registradas**. Cuando exista, se puede retirar la regla de §5 que deja
  pasar todo a los gyms sin inventario. Se decide al revisar Gyms y el onboarding.

## 11. Propuesta de sub-etapas

| Sub-etapa | Qué |
|---|---|
| **E1** | Catálogo del sistema (`EquipmentModel`, siembra de ~40 modelos) e inventario (`EquipmentItem`): "Agregar del catálogo", máquinas propias, zona, foto, baja solo Owner. Mover `EquipmentType` a este módulo. |
| **E2** | Estado e historial; nivel 1 aplicado en el catálogo de ejercicios y en las alternativas. |
| **E3** | Ligar ejercicios a modelos (sistema) y a máquinas (gym); avisos en el editor; foto, zona e instrucciones en la app. **También conecta el nivel 1** (decidido 2026-10-11): el filtro "Solo lo que se puede hacer en mi gym" del catálogo de ejercicios consume `IEquipmentReader.GetAvailabilityAsync` (hecho en E2) — toca Routines. |
| **E4** | Reportes de fallas: botón en la app, campana general del panel, reportes urgentes, correo al owner por `unsafe`, tarjeta del dashboard, flag `MembersCanReportEquipment`. |

E1 y E2 van **antes que R5** (alternativas) de Routines, porque las sugerencias filtran por
disponibilidad.

## 12. Pendiente de decidir

- Nada por ahora.
