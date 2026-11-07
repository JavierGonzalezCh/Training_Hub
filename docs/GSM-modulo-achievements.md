# Módulo Achievements — replanteamiento (v2)

> **Estado:** construido en la v2 (rama `v2`, verificado con tests el 2026-10-20). Describe el diseño vigente; lo que quedó sin hacer o sin verificar está en `V2-PENDIENTES.md`. Las frases «hoy…» que contrastan con lo anterior describen la v1, previa a esta versión.

## 1. Propósito

Motivar a los miembros: **logros** por lo que hacen, **racha** de días cumplidos, **premios** que el gym
decide dar y un **ranking** voluntario.

## 2. Decisiones tomadas (2026-10-07)

| Tema | Decisión |
|---|---|
| Tipos de logro nuevos | **Antigüedad**, **récords personales**, **referidos** y **otorgado por el staff** (§3). |
| Premios | **Depende del gym**: cada logro puede traer un premio o no. Si lo trae, se canjea en recepción (§4). |
| Ranking | **Sí, voluntario**: solo aparece quien acepta participar (§5). |
| Logro nuevo | **Retroactivo**: se otorga de inmediato a quien ya cumple la condición (§6). |

### Heredadas de otras revisiones

| Regla | Origen |
|---|---|
| Con QR fijo, las entradas sin ubicación o fuera del radio **no cuentan** para logros ni para la racha. | `GSM-modulo-attendance.md` §9 |
| Anular una entrada **no quita** logros ya ganados; la racha se recalcula sola. | Attendance §5 |
| Con entradas múltiples se cuentan **días**, no entradas. | Attendance §3.2 |
| Las rutinas creadas por el propio miembro **no cuentan** para logros. | Routines (v1) |
| La asistencia marcada por el miembro ("Estoy en clase") cuenta igual que la del instructor. | Classes (v2) |
| Comunicaciones avisa el desbloqueo (ya existe); push cuando exista Notifications. | Communications |

## 3. Tipos de logro

| Tipo (`condition_type`) | Qué cuenta | Estado |
|---|---|---|
| `visits` | Días con entrada válida | Existe |
| `streak` | Días seguidos cumplidos | Existe |
| `routine_sessions` | Días con ejercicios de rutina registrados | Existe |
| `classes` | Clases asistidas | Existe |
| **`tenure_months`** | **Meses como miembro** | Nuevo |
| **`personal_records`** | **Récords personales batidos** | Nuevo |
| **`lifted_volume`** | **Volumen total levantado, en kg** | Nuevo |
| **`referrals`** | **Amigos referidos que se inscribieron** | Nuevo |
| **`manual`** | **Lo otorga el staff a mano** | Nuevo |

### 3.1 Antigüedad (`tenure_months`)

- Cuenta los **días cubiertos por membresías**, sin las canceladas ni los días congelados, dividido
  entre 30. No cuenta desde la fecha de ingreso: un miembro que se fue un año y volvió no suma ese año.
- Lo lee de Memberships (`IMembershipReader`, método nuevo `GetCoveredDaysAsync`).
- Se evalúa al registrar una entrada y al asignar una membresía.
- Ejemplos: "1 mes con nosotros", "6 meses", "1 año".

### 3.2 Récords personales y volumen (`personal_records`, `lifted_volume`)

- **Récord personal:** un registro de rutina que supera el mejor peso × repeticiones anterior del
  miembro en ese ejercicio. Lo detecta Routines al guardar el registro (con el progreso de R7) y
  publica `PersonalRecordSet`.
- **Volumen:** suma de peso × repeticiones de todas las series registradas, solo en ejercicios de
  repeticiones con peso.
- Como el resto de los logros de rutina, **no cuentan los registros de rutinas creadas por el propio
  miembro**.
- **Limitación aceptada:** los pesos los escribe el miembro. Alguien puede inventar un récord. El
  premio de estos logros conviene que sea simbólico.
- Ejemplos: "Primer récord", "10 récords", "10 toneladas levantadas".

### 3.3 Referidos (`referrals`)

- Cuenta los miembros cuya ficha dice "referido por" este miembro (`Member.ReferredByMemberId`,
  `GSM-modulo-members.md`) y que **ya tienen su primera membresía pagada**. Una cortesía de precio 0
  no cuenta.
- Se evalúa cuando el referido recibe su primera membresía (evento `MembershipCreated` de
  Memberships).
- Ejemplos: "Primer amigo", "5 amigos".

### 3.4 Otorgado por el staff (`manual`)

- El logro no tiene condición automática. Se otorga desde la ficha del miembro: "Otorgar logro", con
  motivo obligatorio ("Ganó la competencia de abril").
- Lo puede otorgar el Owner o cualquier staff. Queda registrado quién y por qué.
- **Es el único tipo que se puede retirar**, con motivo, por si se otorgó por error. Los automáticos
  nunca se retiran.
- Un mismo logro manual se otorga una vez por miembro.

### 3.5 Logros por defecto de un gym nuevo

A los cinco de hoy se suman tres: "1 año con nosotros" (`tenure_months` 12), "Primer récord"
(`personal_records` 1) y "Primer amigo" (`referrals` 1). Ninguno trae premio: el gym lo agrega si
quiere.

## 4. Premios

**Cada gym decide**, logro por logro, si da premio.

- `Achievement` suma `RewardDescription` (texto opcional: "Camiseta del gym", "10 % en tu próxima
  renovación") y `RewardValidDays` (opcional: cuántos días tiene para reclamarlo).
- Sin texto de premio, el logro es solo reconocimiento, como hoy.
- `MemberAchievement` suma `RewardStatus` (`none`, `pending`, `delivered`, `expired`),
  `RewardDeliveredAt`, `RewardDeliveredBy`.
- **El miembro** ve en la app "Premio pendiente: Camiseta del gym · reclámalo en recepción".
- **Recepción** ve los premios pendientes en la tarjeta del miembro (`GSM-modulo-attendance.md` §4) y
  lo marca como **entregado**.
- **Un premio de descuento no se aplica solo.** Recepción lo usa como descuento al cobrar la
  membresía (el precio acordado con su motivo, `GSM-modulo-memberships.md` §4) y marca el premio como
  entregado.
- Si tiene vencimiento, **3 días antes** el miembro recibe "Tu premio *Camiseta del gym* vence el 15
  oct; reclámalo en recepción". Si no se reclama, pasa a `expired` (se calcula; sin tarea programada).
  El aviso lo genera la tarea periódica diaria.
- El Owner ve los premios entregados y pendientes en Logros, para saber cuánto le cuestan.

## 5. Ranking voluntario

- **Ranking mensual de constancia:** días con entrada válida en el mes calendario. Mismo criterio de
  días válidos que la racha (sin entradas fuera del radio, sin entradas anuladas).
- **Solo aparece quien acepta participar**, desde su perfil en la app: "Aparecer en el ranking del
  gym". Elige cómo se le ve: **nombre e inicial del apellido con su foto** ("Ana T."), o **un alias**.
  Puede salirse cuando quiera.
- **Los menores de edad no pueden participar** (`GSM-modulo-members.md` §3.5).
- La app muestra el **top 10** y, a quien participa, **su propia posición** ("Vas 14.º con 12 días").
  Quien no participa ve el top 10 y una invitación a sumarse.
- Empates: va primero quien llegó antes a esa cantidad de días.
- Al cerrar el mes queda visible el **podio del mes anterior**.
- El gym puede apagarlo (flag nuevo `ShowRanking`, encendido por defecto, en Gyms).
- Un logro puede premiar el ranking más adelante; en esta versión el ranking no otorga logros.

## 6. Logros retroactivos

- Al **crear** un logro, o al **bajar** el valor de su condición, el Owner ve antes de confirmar:
  "**23 miembros** ya cumplen este logro y lo recibirán ahora".
- Al confirmar, se otorga de inmediato a todos los miembros activos que cumplen la condición, con la
  fecha de hoy.
- **Cada miembro recibe un solo aviso**, aunque gane varios logros a la vez por este motivo.
- Si el logro trae premio, a ellos también les queda pendiente.
- Subir el valor de la condición **no quita** el logro a quien ya lo tenía.
- No aplica a los logros `manual`.
- Para gyms grandes, el cálculo se hace por consultas en bloque por tipo de condición, no miembro por
  miembro.

## 7. Racha

No cambia la regla (con rutina, contra su calendario; sin rutina, tolerancia de 2 días; congelador
cada 15 días, máximo 2). Cambian las entradas que cuentan (§2, heredadas).

## 8. Vista del miembro: "Mi progreso"

Hoy el miembro tiene `/app/logros` (cuadrícula de logros con su progreso, "Visitas · 7/10") y, en el
inicio, el número de su racha y de sus congeladores. **No hay forma de entender la racha.** La sección
de logros de la app pasa a ser **"Mi progreso"**, con tres pestañas:

1. **Racha**
   - Racha actual y **mejor racha** histórica.
   - **Calendario del mes**: días entrenados, descansos de la rutina, días salvados por un congelador
     y días fallados, cada uno con su color. Se puede ir a meses anteriores.
   - **Qué toca hoy**: "Hoy te toca entrenar para mantener tu racha" o "Hoy es tu día de descanso".
   - **Congeladores**: cuántos tiene, cuándo gana el siguiente y en qué días se usaron.
   - Si alguna entrada no contó (sin ubicación con QR fijo), se explica en ese día del calendario.
2. **Logros**
   - Ganados, con su fecha, y bloqueados, con su progreso. Ordenados por los más cercanos a ganarse.
   - **Premios pendientes** arriba, con "reclámalo en recepción" y su vencimiento.
   - Logros manuales con el motivo ("Ganó la competencia de abril").
3. **Ranking** (si el gym lo tiene encendido): top 10, su posición si participa, el podio del mes
   anterior y el interruptor para participar.

En el inicio de la app se mantiene el resumen (racha y congeladores), y al tocarlo abre "Mi progreso".
El progreso de carga por ejercicio (récords, gráficas) vive en la pantalla de la rutina (Routines R7),
que es solo de entrenamiento: qué toca, próximos entrenamientos y registro de pesos. **Los logros no
aparecen en la rutina** y la rutina no aparece en "Mi progreso" (decidido 2026-10-11).

## 9. Lo que cambia en otros módulos

- **Memberships:** `GetCoveredDaysAsync` para la antigüedad; `MembershipCreated` dispara los
  referidos.
- **Routines:** publica `PersonalRecordSet` y expone el volumen por miembro.
- **Members:** `ReferredByMemberId` para los referidos; la ficha muestra los logros manuales con su
  motivo.
- **Attendance:** la tarjeta de recepción muestra los premios pendientes.
- **Gyms:** flag `ShowRanking`.
- **App del miembro:** perfil con "Aparecer en el ranking"; pantalla de ranking; premios pendientes en
  Logros.

## 10. Propuesta de sub-etapas

| Sub-etapa | Qué |
|---|---|
| **L1** | Reglas heredadas (entradas que cuentan) y logros retroactivos. |
| **L2** | Antigüedad y referidos. |
| **L3** | Récords personales y volumen (depende de R7, progreso en Routines). |
| **L4** | Logros otorgados por el staff. |
| **L5** | Premios: texto del premio, pendiente/entregado, vencimiento, vista en recepción. |
| **L6** | Ranking voluntario. |
| **L7** | "Mi progreso" en la app: calendario de racha, mejor racha, congeladores, logros con premios y ranking. |

## Catálogo sembrado por tipo (T2)

`SeedDefaultAchievementsHandler` siembra `AchievementTemplates.For(tipo)` al crear el gym y es idempotente (solo agrega los nombres que faltan, consultando por gym). Desde AC1 el catálogo son las cadenas de §12; ver `GSM-tipos-de-negocio.md` §4.5.

## 11. Condiciones nuevas (AC1, construido 2026-11-07)

| Tipo (`condition_type`) | Qué cuenta | De dónde sale |
|---|---|---|
| `early_visits` | Días con entrada **antes de las 8:00** (hora del gym) | `IAttendanceReader.GetVisitHabitsAsync` |
| `late_visits` | Días con entrada **desde las 20:00** | ídem |
| `weekends` | **Fines de semana distintos** con entrada (sábado y domingo de la misma semana cuentan 1) | ídem |
| `complete_sessions` | Días en que registró **todas** las líneas vigentes de un día de su rutina (el que tocaba u otro del plan; solo rutinas asignadas) | `IRoutineReader.GetCompleteSessionCountAsync`, `CompleteSessionCalculator` |
| `distinct_classes` | Clases distintas a las que asistió | `IClassReader.GetDistinctAttendedClassCountAsync` |
| `power_lifts` | Cuántos de los tres levantamientos tiene registrados con peso (la meta es 3) | `IRoutineReader.GetPowerLiftCountAsync`; los ejercicios llevan `Exercise.PowerLift` (`squat`, `bench`, `deadlift`) y el catálogo del sistema trae marcados «Sentadilla con barra», «Press de banca plana con barra» y «Peso muerto convencional» |
| `monthly_ranking_top` | Terminar un **mes cerrado** del ranking en el puesto `condition_value` o mejor (50, 10, 3, 1) | `achievements.ranking_month_results` |

- Las horas son **fijas** para todos los gyms (decidido 2026-11-07).
- Las métricas van en `AchievementExtraMetrics` y solo se calculan si hay un logro pendiente que las pide
  (`AchievementMetrics.ComputeExtraAsync`); los retroactivos las calculan miembro por miembro.
- **Cierre de mes del ranking:** `CloseRankingMonthHandler`, paso `ranking_close` de la tarea periódica. Para cada gym
  con el ranking encendido y participantes guarda una vez el puesto final de cada uno en el mes anterior (único por
  gym, mes y miembro) y vuelve a evaluar sus logros. Un mes sin clasificados no deja filas y se reintenta (barato).
- Migración `V2_AC1_AchievementChains` (aplicada en producción el 2026-11-07).
- **Tiempo en sala** se descartó: la app registra la entrada, no la salida.

## 12. Catálogo con medallas (AC1)

Un gym nuevo arranca con cadenas por nivel, cada logro con su medalla (imágenes en
`GSM.UI.Shared/wwwroot/achievements`, ruta `/_content/GSM.UI.Shared/achievements/<slug>.webp`):

| Cadena | Logros (valor) |
|---|---|
| Asistencia (`visits`) | Primeros Pasos 5 · Constante 10 · Veterano 25 · Medio Centenar 50 · Centurión 100 · Leyenda del Club 250 |
| Rachas (`streak`) | Fuego Inicial 3 · Semana Perfecta 7 · Imparable 14 · Hábito de Hierro 21 · Inquebrantable 30 |
| Disciplina (`complete_sessions`) | Enfocado 5 · Disciplinado 15 · Atleta Metódico 30 · Maestro de la Rutina 60 · Perfeccionista 100 |
| Clases | Curioso 3 · Fan de las Clases 10 · Compañero de Fila 25 · Master de Estudio 50 (`classes`) · Polifacético 4 (`distinct_classes`) |
| Fuerza | Rompiendo Barreras I 1 y II 5 (`personal_records`) · Club de la Fuerza 3 (`power_lifts`) · Tonelaje I 10.000 · Tonelaje II 100.000 · Hércules 500.000 kg acumulados (`lifted_volume`) |
| Horarios | Madrugador I 5 y II 20 (`early_visits`) · Noctámbulo I 5 y II 20 (`late_visits`) · Guerrero de Fin de Semana 8 (`weekends`) |
| Ranking (`monthly_ranking_top`) | En el Tablero 50 · Aspirante al Podio 10 · Medallero 3 · Número Uno 1 |
| Otros | 1 año con nosotros (`tenure_months` 12) · Primer amigo (`referrals` 1) |

Por tipo: el **entrenador personal** no recibe asistencia, horarios ni ranking; **estudio y academia** no reciben
disciplina ni fuerza; las **clases** van a box, estudio y academia. Un gym tipo gym recibe 33. El tonelaje es
acumulado (la propuesta original era por sesión: 5.000, 15.000 y 30.000 kg). **Los gyms que ya existían no reciben
el catálogo nuevo** (solo se siembra al crear el gym).

En la app, la medalla ganada se ve a color y la bloqueada **oscurecida en relieve** (se intuye el dibujo; la silueta
pura dejaba un círculo negro igual en todas las medallas redondas), con candado. El ranking muestra la meta como
«Top 10 en un mes» en lugar de un conteo.
