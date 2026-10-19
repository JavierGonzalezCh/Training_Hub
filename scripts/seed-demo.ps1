<#
.SYNOPSIS
    Siembra datos de demostración para revisar el sistema completo en local.

.DESCRIPTION
    Dos fases:

    1. Por la API (https://localhost:7219; por HTTP redirige y la redirección pierde el token): todo lo que tiene reglas de negocio —miembros, planes,
       membresías, pagos, clases, reservas, rutinas, logros, staff, gyms del SaaS— pasa por los
       casos de uso reales, así que las invariantes se respetan igual que en producción.
    2. Por SQL: la API solo registra "hoy" (check-ins, logs de rutina, fecha de pago). Para que
       el dashboard, las horas pico, la racha y los ingresos del mes anterior tengan algo que
       mostrar, el historial se retrocede o se rellena directamente en la base.

    Siembra sobre el gym `demo` (owner@demo.local) y crea cuatro gyms más para el panel
    SuperAdmin. No es idempotente por partes: si encuentra el miembro marcador (documento
    1010000001) se detiene, salvo con -Force.

    Requiere la API arrancada en Development (DevSeeder ya ejecutado) y psql de Postgres 18.

    Cuentas de prueba que crea (todas en dominios .local / .test, sin validez fuera de local):
      - <miembro>@correo.test  tres miembros con cuenta en la app   clave: Miembro_123456
      - owner@<slug>.local     owners de los cuatro gyms nuevos      clave: ChangeMe_123456
      - recepcion@ y entrenador@demo.local: staff sin contraseña (así nace el staff; el código
        de restablecimiento sale en el log de la API porque Resend no está configurado).

.EXAMPLE
    ./scripts/seed-demo.ps1
#>
param(
    [string]$Api = 'https://localhost:7219',
    [string]$PgBin = 'C:\Program Files\PostgreSQL\18\bin',
    [string]$PgConnection = 'host=localhost port=5432 dbname=gsm user=gsm password=gsm',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$SeedPassword = 'ChangeMe_123456'
$MemberPassword = 'Miembro_123456'
$Random = New-Object System.Random 20261004

# ----------------------------------------------------------------------------------------------
# Utilidades
# ----------------------------------------------------------------------------------------------

function Invoke-Api {
    param(
        [Parameter(Mandatory)][string]$Method,
        [Parameter(Mandatory)][string]$Path,
        $Body = $null,
        [string]$Token = $null
    )

    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }

    $params = @{
        Method      = $Method
        Uri         = "$Api$Path"
        Headers     = $headers
        ContentType = 'application/json; charset=utf-8'
    }
    if ($null -ne $Body) {
        # Windows PowerShell 5.1 manda las cadenas en ISO-8859-1: los acentos solo llegan bien en bytes.
        $json = ConvertTo-Json -InputObject $Body -Depth 10 -Compress
        $params['Body'] = [System.Text.Encoding]::UTF8.GetBytes($json)
    }

    try {
        # En 5.1 Invoke-RestMethod emite un array JSON como un solo objeto: pasarlo por una
        # variable lo desenrolla, y sin eso un Where-Object sobre la lista ve el array entero.
        $result = Invoke-RestMethod @params
        return $result
    }
    catch {
        $detail = $null
        if ($_.ErrorDetails) { $detail = $_.ErrorDetails.Message }
        $status = $null
        if ($_.Exception.Response) {
            $status = [int]$_.Exception.Response.StatusCode
            # En 5.1 ErrorDetails a veces llega vacío: el ProblemDetails hay que leerlo del stream.
            if (-not $detail) {
                try {
                    $stream = $_.Exception.Response.GetResponseStream()
                    $stream.Position = 0
                    $detail = (New-Object System.IO.StreamReader $stream).ReadToEnd()
                }
                catch { }
            }
        }
        throw "API $Method $Path -> $status $detail"
    }
}

# Igual que Invoke-Api pero devuelve $null en vez de lanzar: para pasos opcionales.
function Try-Api {
    param([string]$Method, [string]$Path, $Body = $null, [string]$Token = $null)
    try { return Invoke-Api -Method $Method -Path $Path -Body $Body -Token $Token }
    catch { Write-Host "    · omitido: $($_.Exception.Message)" -ForegroundColor DarkYellow; return $null }
}

function Login([string]$Email, [string]$Password) {
    $r = Invoke-Api POST '/api/auth/login' @{ email = $Email; password = $Password }
    if ($r.tokens) { return $r.tokens.accessToken }
    return $r.accessToken
}

function Invoke-Sql([string]$Sql) {
    $psql = Join-Path $PgBin 'psql.exe'
    $file = [System.IO.Path]::GetTempFileName()
    try {
        [System.IO.File]::WriteAllText($file, $Sql, (New-Object System.Text.UTF8Encoding $false))
        & $psql $PgConnection -v ON_ERROR_STOP=1 -q -X -f $file
        if ($LASTEXITCODE -ne 0) { throw "psql terminó con código $LASTEXITCODE" }
    }
    finally {
        Remove-Item $file -ErrorAction SilentlyContinue
    }
}

function Pick($Items) { return $Items[$Random.Next($Items.Count)] }
function Step([string]$Text) { Write-Host "==> $Text" -ForegroundColor Cyan }

# Días de negocio en la zona de Colombia, nunca desde UTC (ver CLAUDE.md, convención de IClock).
$tz = [TimeZoneInfo]::FindSystemTimeZoneById('SA Pacific Standard Time')
$Today = [TimeZoneInfo]::ConvertTimeFromUtc([DateTime]::UtcNow, $tz).Date
function Day([int]$Offset) { return $Today.AddDays($Offset).ToString('yyyy-MM-dd') }

# ----------------------------------------------------------------------------------------------
# 0. Sesión y comprobación
# ----------------------------------------------------------------------------------------------

Step 'Iniciando sesión como owner@demo.local'
$owner = Login 'owner@demo.local' $SeedPassword
$gym = Invoke-Api GET '/api/gym' -Token $owner
$gymId = $gym.id

$marker = Invoke-Api GET '/api/members?search=1010000001' -Token $owner
if ($marker.total -gt 0 -and -not $Force) {
    Write-Host 'El gym demo ya tiene los datos sembrados (miembro 1010000001). Usa -Force para sembrar de todas formas.' -ForegroundColor Yellow
    return
}

# ----------------------------------------------------------------------------------------------
# 1. Gym: marca, módulos y staff
# ----------------------------------------------------------------------------------------------

Step 'Configurando el gym demo (marca v4.0 y módulos activos)'
Invoke-Api PUT '/api/gym' @{
    name           = 'Gimnasio Demo'
    nit            = $gym.nit
    phone          = '6013214567'
    email          = 'contacto@demo.local'
    logoUrl        = $gym.logoUrl
    primaryColor   = '#B50016'
    secondaryColor = '#0AAFCA'
} -Token $owner | Out-Null

Invoke-Api PUT '/api/gym/feature-flags' @{
    showRoutines             = $true
    showClasses              = $true
    showProgress             = $true
    showGamification         = $true
    showCommunity            = $false
    checkinQr                = $true
    communityComments        = $false
    membersCanCreateRoutines = $true
} -Token $owner | Out-Null

Step 'Staff adicional'
Try-Api POST '/api/gym/staff' @{ email = 'recepcion@demo.local'; fullName = 'Laura Méndez' } -Token $owner | Out-Null
Try-Api POST '/api/gym/staff' @{ email = 'entrenador@demo.local'; fullName = 'Andrés Gómez' } -Token $owner | Out-Null

# ----------------------------------------------------------------------------------------------
# 2. Planes
# ----------------------------------------------------------------------------------------------

Step 'Planes de membresía'
$planSpecs = @(
    @{ key = 'mensual';    name = 'Mensual';     description = 'Acceso ilimitado a sala y clases.';          price = 120000;  days = 30 },
    @{ key = 'trimestral'; name = 'Trimestral';  description = 'Tres meses con descuento.';                  price = 320000;  days = 90 },
    @{ key = 'anual';      name = 'Anual';       description = 'El mejor precio por mes.';                   price = 1100000; days = 365 },
    @{ key = 'estudiante'; name = 'Estudiante';  description = 'Mensual con carné estudiantil vigente.';     price = 90000;   days = 30 },
    @{ key = 'dia';        name = 'Pase de día'; description = 'Una entrada.';                               price = 15000;   days = 1 }
)
$existingPlans = @(Invoke-Api GET '/api/membership-plans' -Token $owner)
$plans = @{}
foreach ($spec in $planSpecs) {
    $found = $existingPlans | Where-Object { $_.name -eq $spec.name } | Select-Object -First 1
    if (-not $found) {
        $found = Invoke-Api POST '/api/membership-plans' @{
            name = $spec.name; description = $spec.description; price = $spec.price; durationDays = $spec.days
        } -Token $owner
    }
    $plans[$spec.key] = @{ id = $found.id; name = $spec.name; price = $spec.price; days = $spec.days }
}

# ----------------------------------------------------------------------------------------------
# 3. Miembros, membresías y pagos
# ----------------------------------------------------------------------------------------------

# Escenario por miembro: plan y hace cuántos días empezó. Con eso salen activos, por vencer y vencidos.
#   start = días atrás; $null = sin membresía.
$people = @(
    @('Valentina', 'Restrepo',  'mensual', 3),
    @('Santiago',  'Gómez',     'trimestral', 20),
    @('Camila',    'Rodríguez', 'mensual', 8),
    @('Mateo',     'Hernández', 'anual', 110),
    @('Isabella',  'Martínez',  'mensual', 12),
    @('Sebastián', 'López',     'mensual', 15),
    @('Mariana',   'García',    'trimestral', 45),
    @('Nicolás',   'Pérez',     'mensual', 1),
    @('Daniela',   'Sánchez',   'estudiante', 10),
    @('Alejandro', 'Ramírez',   'mensual', 18),
    @('Sofía',     'Torres',    'mensual', 22),
    @('Juan José', 'Díaz',      'mensual', 5),
    @('Gabriela',  'Vargas',    'trimestral', 70),
    @('Samuel',    'Castro',    'mensual', 9),
    @('Luciana',   'Moreno',    'estudiante', 19),
    @('David',     'Rojas',     'mensual', 14),
    @('Antonia',   'Jiménez',   'mensual', 0),
    @('Tomás',     'Muñoz',     'anual', 200),
    @('Paula',     'Ortiz',     'mensual', 26),
    @('Emiliano',  'Suárez',    'mensual', 27),
    @('Manuela',   'Romero',    'mensual', 28),
    @('Martín',    'Álvarez',   'estudiante', 25),
    @('Sara',      'Ruiz',      'mensual', 40),
    @('Felipe',    'Mejía',     'mensual', 55),
    @('Laura',     'Cárdenas',  'mensual', 75),
    @('Andrés',    'Quintero',  'trimestral', 120),
    @('Juliana',   'Ospina',    'mensual', 6),
    @('Carlos',    'Arango',    'dia', 0),
    @('María José','Zapata',    $null, $null),
    @('Esteban',   'Giraldo',   $null, $null)
)

Step "Creando $($people.Count) miembros con membresías y pagos"
$methods = @('Cash', 'Transfer', 'Card', 'Transfer', 'Cash')
$members = @()
for ($i = 0; $i -lt $people.Count; $i++) {
    $p = $people[$i]
    $doc = '10100000{0:00}' -f ($i + 1)
    $email = ('{0}.{1}@correo.test' -f $p[0], $p[1]).ToLowerInvariant() `
        -replace ' ', '' -replace 'á', 'a' -replace 'é', 'e' -replace 'í', 'i' -replace 'ó', 'o' -replace 'ú', 'u' -replace 'ñ', 'n'
    $birth = (Get-Date -Year (1975 + $Random.Next(30)) -Month (1 + $Random.Next(12)) -Day (1 + $Random.Next(28))).ToString('yyyy-MM-dd')

    $created = Invoke-Api POST '/api/members' @{
        firstName        = $p[0]
        lastName         = $p[1]
        documentNumber   = $doc
        phone            = '3{0}' -f (100000000 + $Random.Next(899999999))
        email            = $email
        birthDate        = $birth
        emergencyContact = 'Familiar · 31{0}' -f (10000000 + $Random.Next(89999999))
        photoUrl         = $null
    } -Token $owner

    $member = [pscustomobject]@{ id = $created.id; doc = $doc; email = $email; first = $p[0]; last = $p[1]; plan = $p[2]; start = $p[3]; membershipId = $null }

    if ($p[2]) {
        $plan = $plans[$p[2]]
        $membership = Invoke-Api POST "/api/members/$($created.id)/memberships" @{ planId = $plan.id; startDate = (Day (-$p[3])) } -Token $owner
        $member.membershipId = $membership.id
        Invoke-Api POST '/api/payments' @{
            amount = $plan.price; method = (Pick $methods); concept = "Plan $($plan.name)"
            notes = $null; memberId = $created.id; membershipId = $membership.id
        } -Token $owner | Out-Null
    }
    $members += $member
}

# Renovaciones: tres miembros que ya pagaron el mes anterior (más ingresos del mes pasado y más historia).
foreach ($idx in 0, 2, 4) {
    $m = $members[$idx]
    Invoke-Api POST '/api/payments' @{
        amount = 120000; method = 'Transfer'; concept = 'Plan Mensual (renovación anterior)'
        notes = 'Mes anterior'; memberId = $m.id; membershipId = $null
    } -Token $owner | Out-Null
}

# Ventas de mostrador de hoy, sin miembro: alimentan el cierre de caja del día.
Step 'Ventas de mostrador'
foreach ($sale in @(@('Agua 600 ml', 3000, 'Cash'), @('Proteína whey (sobre)', 8000, 'Cash'), @('Toalla', 25000, 'Card'), @('Bebida isotónica', 5000, 'Transfer'))) {
    Invoke-Api POST '/api/payments' @{ amount = $sale[1]; method = $sale[2]; concept = $sale[0]; notes = $null; memberId = $null; membershipId = $null } -Token $owner | Out-Null
}

# Un miembro dado de baja (inactivo): para el filtro de estado.
Invoke-Api DELETE "/api/members/$($members[29].id)" -Token $owner | Out-Null

# ----------------------------------------------------------------------------------------------
# 4. Logros
# ----------------------------------------------------------------------------------------------

Step 'Logros'
$existingAchievements = @(Invoke-Api GET '/api/achievements' -Token $owner)
foreach ($a in @(
        @('Primera visita', 'Tu primer día en el gym.', 'Visits', 1),
        @('Constante', '10 visitas al gym.', 'Visits', 10),
        @('Veterano', '25 visitas al gym.', 'Visits', 25),
        @('Semana perfecta', '7 días seguidos de racha.', 'Streak', 7),
        @('Disciplina', '5 sesiones de rutina registradas.', 'RoutineSessions', 5),
        @('Fan de las clases', 'Asististe a 3 clases.', 'Classes', 3))) {
    if (-not ($existingAchievements | Where-Object { $_.name -eq $a[0] })) {
        Invoke-Api POST '/api/achievements' @{ name = $a[0]; description = $a[1]; iconUrl = $null; conditionType = $a[2]; conditionValue = $a[3] } -Token $owner | Out-Null
    }
}

# ----------------------------------------------------------------------------------------------
# 5. Historial por SQL: fechas de alta, pagos y asistencia pasada
# ----------------------------------------------------------------------------------------------

Step 'Retrocediendo fechas de alta y de pago, y rellenando la asistencia de las últimas 6 semanas'
$docs = ($members | ForEach-Object { "'$($_.doc)'" }) -join ','
$constantes = ($members[0..2] | ForEach-Object { "'$($_.id)'" }) -join ','

Invoke-Sql @"
BEGIN;

-- Fecha de alta: el inicio de su primera membresía, o hace 90 días si no tiene.
UPDATE members.members m
SET joined_at  = COALESCE((SELECT min(ms.start_date) FROM memberships.memberships ms WHERE ms.member_id = m.id), current_date - 90)::timestamp AT TIME ZONE 'America/Bogota' + interval '9 hours',
    created_at = COALESCE((SELECT min(ms.start_date) FROM memberships.memberships ms WHERE ms.member_id = m.id), current_date - 90)::timestamp AT TIME ZONE 'America/Bogota' + interval '9 hours'
WHERE m.gym_id = '$gymId' AND m.document_number IN ($docs);

-- Cada pago de plan, el día que empezó su membresía, a una hora de recepción.
UPDATE payments.payments p
SET paid_at = (ms.start_date::timestamp + make_interval(hours => 7 + (abs(hashtext(p.id::text)) % 12), mins => abs(hashtext(p.id::text)) % 60)) AT TIME ZONE 'America/Bogota'
FROM memberships.memberships ms
WHERE p.membership_id = ms.id AND p.gym_id = '$gymId';

-- Las renovaciones "del mes anterior" caen 30 días antes del inicio de su membresía actual.
UPDATE payments.payments p
SET paid_at = ((SELECT min(ms.start_date) FROM memberships.memberships ms WHERE ms.member_id = p.member_id) - 30)::timestamp AT TIME ZONE 'America/Bogota' + interval '18 hours'
WHERE p.gym_id = '$gymId' AND p.notes = 'Mes anterior';

-- Asistencia: los días cubiertos por una membresía, con probabilidad ~45 % y horas pico
-- reales (6-8 h y 17-20 h). Hoy no: los check-ins de hoy pasan por la API, que evalúa logros.
INSERT INTO attendance.attendances (id, gym_id, member_id, checked_in_at, attendance_date, method, created_at, updated_at)
SELECT gen_random_uuid(), m.gym_id, m.id, ts, d::date, (CASE WHEN random() < 0.7 THEN 'qr' ELSE 'manual' END)::attendance.attendance_method, ts, ts
FROM members.members m
JOIN memberships.memberships ms ON ms.member_id = m.id
CROSS JOIN LATERAL generate_series(greatest(ms.start_date, current_date - 42), least(ms.end_date, current_date - 1), interval '1 day') AS d
CROSS JOIN LATERAL (
    SELECT (d::date + (CASE WHEN random() < 0.45 THEN time '05:30' + random() * interval '3 hours'
                            WHEN random() < 0.8  THEN time '16:45' + random() * interval '3.5 hours'
                            ELSE time '09:00' + random() * interval '7 hours' END))::timestamp AT TIME ZONE 'America/Bogota' AS ts
) t
WHERE m.gym_id = '$gymId' AND m.document_number IN ($docs)
  AND m.id NOT IN ($constantes)
  AND random() < 0.45
ON CONFLICT (member_id, attendance_date) DO NOTHING;

-- Tres miembros constantes: todos los días de las últimas 3 semanas (racha y logros de racha).
INSERT INTO attendance.attendances (id, gym_id, member_id, checked_in_at, attendance_date, method, created_at, updated_at)
SELECT gen_random_uuid(), m.gym_id, m.id, ts, d::date, 'qr', ts, ts
FROM members.members m
CROSS JOIN LATERAL generate_series(current_date - 21, current_date - 1, interval '1 day') AS d
CROSS JOIN LATERAL (SELECT (d::date + time '06:00' + random() * interval '90 minutes')::timestamp AT TIME ZONE 'America/Bogota' AS ts) t
WHERE m.id IN ($constantes)
ON CONFLICT (member_id, attendance_date) DO NOTHING;

COMMIT;
"@

# ----------------------------------------------------------------------------------------------
# 6. Check-ins de hoy por la API (evalúan logros con el historial ya cargado)
# ----------------------------------------------------------------------------------------------

Step 'Check-ins de hoy'
foreach ($idx in 0, 1, 2, 3, 5, 7, 9, 11, 13, 16, 22) {
    # El 22 tiene la membresía vencida: la recepción lo deja entrar pero la API lo marca.
    $r = Try-Api POST '/api/attendance/checkin' @{ memberId = $members[$idx].id } -Token $owner
    if ($r -and $r.unlockedAchievements.Count -gt 0) {
        Write-Host ("    · {0} desbloqueó: {1}" -f $r.memberName, (($r.unlockedAchievements | ForEach-Object { $_.name }) -join ', '))
    }
}

# ----------------------------------------------------------------------------------------------
# 7. Clases: catálogo, asistencia pasada, una cancelación
# ----------------------------------------------------------------------------------------------

Step 'Clases'
$existingClasses = @(Invoke-Api GET '/api/classes' -Token $owner)
$classSpecs = @(
    @{ name = 'Yoga';                    description = 'Flexibilidad y respiración.';         instructorName = 'Valentina Ríos'; capacity = 15; startTime = '07:00:00'; durationMinutes = 60; isRecurring = $true; daysOfWeek = @('Tuesday', 'Thursday'); scheduledDate = $null },
    @{ name = 'CrossFit';                description = 'WOD del día, todos los niveles.';     instructorName = 'Andrés Gómez';   capacity = 12; startTime = '18:00:00'; durationMinutes = 60; isRecurring = $true; daysOfWeek = @('Monday', 'Wednesday', 'Friday'); scheduledDate = $null },
    @{ name = 'Funcional';               description = 'Circuito de fuerza y cardio.';        instructorName = 'Camilo Pardo';   capacity = 20; startTime = '19:15:00'; durationMinutes = 45; isRecurring = $true; daysOfWeek = @('Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'); scheduledDate = $null },
    @{ name = 'Pilates';                 description = 'Core y postura.';                     instructorName = 'Mariana López';  capacity = 10; startTime = '09:00:00'; durationMinutes = 50; isRecurring = $true; daysOfWeek = @('Saturday'); scheduledDate = $null },
    @{ name = 'Masterclass de movilidad'; description = 'Sesión especial de fin de mes.';     instructorName = 'Valentina Ríos'; capacity = 25; startTime = '10:00:00'; durationMinutes = 90; isRecurring = $false; daysOfWeek = $null; scheduledDate = (Day 5) }
)
foreach ($spec in $classSpecs) {
    if (-not ($existingClasses | Where-Object { $_.name -eq $spec.name })) {
        Invoke-Api POST '/api/classes' $spec -Token $owner | Out-Null
    }
}

# Asistencia de las dos últimas semanas, marcada por el staff. Una clase recién creada puede no
# proyectar ocurrencias anteriores a su alta: esas fechas se omiten sin más.
$past = Invoke-Api GET "/api/classes/schedule?from=$(Day -14)&to=$(Day -1)" -Token $owner
$attendedClasses = 0
foreach ($entry in @($past.entries)) {
    $pool = $members[0..21] | Sort-Object { $Random.Next() } | Select-Object -First (3 + $Random.Next(6))
    try {
        Invoke-Api POST "/api/classes/$($entry.classId)/sessions/$($entry.sessionDate)/attendance" @{
            attended = @($pool | ForEach-Object { $_.id }); noShow = @()
        } -Token $owner | Out-Null
        $attendedClasses++
    }
    catch { }
}
Write-Host "    · $attendedClasses sesiones pasadas con asistencia"

# Una sesión futura cancelada, para ver el estado en el horario.
$upcoming = Invoke-Api GET "/api/classes/schedule?from=$(Day 1)&to=$(Day 7)" -Token $owner
$yoga = @($upcoming.entries) | Where-Object { $_.className -eq 'Yoga' } | Select-Object -First 1
if ($yoga) {
    Try-Api POST "/api/classes/$($yoga.classId)/sessions/$($yoga.sessionDate)/cancel" @{ reason = 'Instructora incapacitada' } -Token $owner | Out-Null
}

# ----------------------------------------------------------------------------------------------
# 8. Cuentas de miembro: invitación, registro, reservas y rutina
# ----------------------------------------------------------------------------------------------

Step 'Rutinas (asignadas por el staff)'
$catalog = @(Invoke-Api GET '/api/exercises' -Token $owner)
function Ex([string]$Name) {
    $e = $catalog | Where-Object { $_.name -eq $Name } | Select-Object -First 1
    if (-not $e) { throw "Ejercicio '$Name' no está en el catálogo" }
    return $e.id
}
function Item([string]$Name, [int]$Sets, [string]$Reps, $Kg, [int]$Rest) {
    return @{ exerciseId = (Ex $Name); targetSets = $Sets; targetReps = $Reps; targetWeightKg = $Kg; restSeconds = $Rest; notes = $null }
}

$routineBody = @{
    name  = 'Torso / Pierna · 4 días'
    notes = 'Subir carga cuando completes todas las series con buena técnica.'
    days  = @(
        @{ name = 'Torso A'; isRest = $false; exercises = @(
                (Item 'Press de banca plana con barra' 4 '8-10' 50 120),
                (Item 'Remo con barra' 4 '8-10' 45 120),
                (Item 'Press militar con barra' 3 '10' 30 90),
                (Item 'Curl de bíceps con barra' 3 '12' 20 60),
                (Item 'Extensión de tríceps en polea' 3 '12' 20 60)) },
        @{ name = 'Pierna A'; isRest = $false; exercises = @(
                (Item 'Sentadilla con barra' 4 '6-8' 70 150),
                (Item 'Peso muerto rumano' 3 '10' 50 120),
                (Item 'Prensa de piernas' 3 '12' 120 90),
                (Item 'Elevación de talones de pie' 4 '15' 40 45)) },
        @{ name = 'Descanso'; isRest = $true; exercises = @() },
        @{ name = 'Torso B'; isRest = $false; exercises = @(
                (Item 'Press de banca inclinado con mancuernas' 4 '10' 18 90),
                (Item 'Jalón al pecho en polea' 4 '10' 45 90),
                (Item 'Elevaciones laterales' 3 '15' 8 60),
                (Item 'Curl martillo' 3 '12' 12 60)) },
        @{ name = 'Pierna B'; isRest = $false; exercises = @(
                (Item 'Hip thrust con barra' 4 '10' 60 90),
                (Item 'Sentadilla búlgara' 3 '10' 14 90),
                (Item 'Curl femoral tumbado en máquina' 3 '12' 30 60),
                (Item 'Plancha' 3 '45 s' $null 45)) },
        @{ name = 'Descanso'; isRest = $true; exercises = @() },
        @{ name = 'Descanso'; isRest = $true; exercises = @() }
    )
}

$withAccount = $members[0..2]
$routines = @{}
foreach ($m in $withAccount + $members[3..5]) {
    $routines[$m.id] = Invoke-Api POST "/api/members/$($m.id)/routines" $routineBody -Token $owner
}

Step 'Cuentas de la app del socio (invitación + registro)'
foreach ($m in $withAccount) {
    $invite = Invoke-Api POST "/api/members/$($m.id)/invite" -Token $owner
    Invoke-Api POST '/api/members/register' @{ code = $invite.code; documentNumber = $m.doc; email = $m.email; password = $MemberPassword } | Out-Null
    Write-Host "    · $($m.email)"

    $token = Login $m.email $MemberPassword

    # Reservas de la próxima semana. Las clases sin cupo son de entrada libre y no admiten reserva.
    $booked = 0
    foreach ($entry in (@($upcoming.entries) | Where-Object { $_.status -ne 'cancelled' -and $null -ne $_.capacity } | Sort-Object { $Random.Next() } | Select-Object -First 4)) {
        $r = Try-Api POST "/api/classes/$($entry.classId)/sessions/$($entry.sessionDate)/book" -Token $token
        if ($r) { $booked++ }
    }
    Write-Host "      $booked reservas"

    # Registro de hoy: el primer día de entrenamiento de su rutina.
    $routine = $routines[$m.id]
    $firstDay = @($routine.days) | Where-Object { -not $_.isRest } | Select-Object -First 1
    foreach ($ex in @($firstDay.exercises)) {
        Try-Api POST "/api/routine-exercises/$($ex.id)/log" @{ setsDone = $ex.targetSets; repsDone = $ex.targetReps; weightKg = $ex.targetWeightKg; notes = $null } -Token $token | Out-Null
    }
}

# Historial de rutina de las últimas 3 semanas para los miembros con cuenta: siguiendo el ciclo
# de días (los de descanso no se registran), con la carga subiendo poco a poco.
Step 'Historial de rutina'
$accountIds = ($withAccount | ForEach-Object { "'$($_.id)'" }) -join ','
Invoke-Sql @"
INSERT INTO routines.routine_exercise_logs (id, gym_id, member_id, routine_exercise_id, performed_on, completed_at, sets_done, reps_done, weight_kg, notes, created_at, updated_at)
SELECT gen_random_uuid(), r.gym_id, r.member_id, re.id, d::date,
       (d::date + time '06:30' + re."order" * interval '6 minutes')::timestamp AT TIME ZONE 'America/Bogota',
       re.target_sets, re.target_reps,
       CASE WHEN re.target_weight_kg IS NULL THEN NULL
            ELSE round((re.target_weight_kg * (0.85 + 0.15 * (21 - (current_date - d::date)) / 21.0))::numeric / 2.5) * 2.5 END,
       NULL,
       (d::date + time '07:30')::timestamp AT TIME ZONE 'America/Bogota',
       (d::date + time '07:30')::timestamp AT TIME ZONE 'America/Bogota'
FROM routines.routines r
JOIN routines.routine_days rd ON rd.routine_id = r.id AND NOT rd.is_rest
JOIN routines.routine_exercises re ON re.routine_day_id = rd.id
CROSS JOIN LATERAL generate_series(current_date - 21, current_date - 1, interval '1 day') AS d
WHERE r.member_id IN ($accountIds)
  -- Día del ciclo que tocaba esa fecha, contando desde el inicio de la rutina (puede ser negativo).
  AND (((d::date - r.starts_on) % 7) + 7) % 7 = rd.position - (SELECT min(position) FROM routines.routine_days x WHERE x.routine_id = r.id)
ON CONFLICT DO NOTHING;
"@

# ----------------------------------------------------------------------------------------------
# 9. SuperAdmin: cartera de gyms con estados distintos
# ----------------------------------------------------------------------------------------------

Step 'Cartera SuperAdmin'
$admin = Login 'admin@gsm.local' $SeedPassword

Try-Api PUT "/api/admin/gyms/$gymId/subscription" @{ plan = 'Pro'; monthlyPrice = 350000 } -Token $admin | Out-Null
Try-Api POST "/api/admin/gyms/$gymId/payments" @{ amount = 350000; method = 'Transfer'; coversFrom = $null; coversThrough = $null; reference = 'TRF-10021'; notes = $null } -Token $admin | Out-Null

$saasGyms = @(
    @{ name = 'Titán Gym Medellín';  slug = 'titan';   plan = 'Enterprise'; price = 590000; state = 'active' },
    @{ name = 'Forma Cali';          slug = 'forma';   plan = 'Basic';      price = 190000; state = 'trial' },
    @{ name = 'Box 57 CrossFit';     slug = 'box57';   plan = 'Pro';        price = 350000; state = 'overdue' },
    @{ name = 'Studio Zen Bogotá';   slug = 'zen';     plan = 'Basic';      price = 190000; state = 'suspended' }
)
foreach ($g in $saasGyms) {
    $created = Try-Api POST '/api/gyms' @{
        name = $g.name; slug = $g.slug; nit = $null; phone = $null; email = "contacto@$($g.slug).local"; plan = $g.plan
        owner = @{ email = "owner@$($g.slug).local"; fullName = "Owner $($g.name)"; password = $SeedPassword }
    } -Token $admin
    if (-not $created) { continue }
    $id = $created.gymId
    Write-Host "    · $($g.name) ($($g.state))"

    Try-Api PUT "/api/admin/gyms/$id/subscription" @{ plan = $g.plan; monthlyPrice = $g.price } -Token $admin | Out-Null
    switch ($g.state) {
        'active' {
            Try-Api POST "/api/admin/gyms/$id/payments" @{ amount = $g.price; method = 'Card'; coversFrom = (Day -20); coversThrough = (Day 10); reference = 'TC-88412'; notes = $null } -Token $admin | Out-Null
        }
        'overdue' {
            Try-Api POST "/api/admin/gyms/$id/payments" @{ amount = $g.price; method = 'Transfer'; coversFrom = (Day -65); coversThrough = (Day -6); reference = 'TRF-09877'; notes = 'Dos meses' } -Token $admin | Out-Null
        }
        'suspended' {
            Try-Api POST "/api/admin/gyms/$id/suspend" @{ reason = 'Impago reiterado; pendiente de acuerdo de pago.' } -Token $admin | Out-Null
        }
    }
}

# Una impersonación (de solo lectura) para que la auditoría tenga una fila.
$imp = Try-Api POST "/api/admin/impersonate/$gymId" @{ reason = 'Revisión de soporte: configuración de clases.' } -Token $admin
if ($imp) { Try-Api POST '/api/admin/impersonate/end' -Token $admin | Out-Null }

Write-Host ''
Write-Host 'Listo. Cuentas útiles:' -ForegroundColor Green
Write-Host "  owner@demo.local / staff@demo.local / admin@gsm.local  (clave del README)"
Write-Host "  App del socio (/app): $(($withAccount | ForEach-Object { $_.email }) -join ', ')  clave: $MemberPassword"
