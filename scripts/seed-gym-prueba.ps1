<#
.SYNOPSIS
    Crea en producción el gym de prueba "con todos los juguetes" y 5 miembros con historial.

.DESCRIPTION
    El recorrido es el de un cliente real, por la API:
      1. El SuperAdmin da de alta el gym (POST /api/gyms) con su owner y 30 días de prueba.
      2. El SuperAdmin le fija la contraseña al owner (PUT /api/admin/gyms/{id}/accounts/{owner}/password),
         porque sin Resend el correo de bienvenida no sale.
      3. El owner configura el gym (todas las opciones encendidas), planes, clases y novedades, y crea
         los 5 miembros: ficha, membresía, pago, rutina e invitación a la app.
      4. Cada miembro activa su cuenta y registra sus ejercicios.
    La API solo registra "hoy". El historial (6 semanas de asistencia y de entrenamientos, con la carga
    subiendo) se rellena por SQL a partir de lo que registró la API. Luego la recepción hace la entrada de hoy
    por la API, y esa entrada evalúa los logros sobre todo el historial.

    Las contraseñas se piden por consola y no se guardan. La conexión a la base sale de $env:GSM_PROD_DB.
#>
param(
    [string]$Api = 'https://gsm-plataforma-gmrss0-0111fa-5-161-96-96.sslip.io',
    [string]$SuperAdminEmail = 'javiergo.2502@gmail.com',
    [string]$OwnerEmail = 'owner@forjagym.test',
    [string]$PgBin = 'C:\Program Files\PostgreSQL\18\bin',
    # El gym ya se creó en una corrida anterior: se salta el alta y la configuración y sigue desde los miembros.
    [switch]$Continuar
)
if (-not $env:GSM_PROD_DB) { throw 'Falta $env:GSM_PROD_DB (cadena de conexión de la base).' }
function Read-Secret([string]$Prompt) {
    $s = Read-Host $Prompt -AsSecureString
    return [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s))
}
$SuperAdminPassword = if ($Continuar) { $null } else { Read-Secret "Contraseña del SuperAdmin ($SuperAdminEmail)" }
$Password = if ($Continuar) { Read-Secret 'Contraseña del owner (la que pusiste en la primera corrida; también será la de los miembros)' }
            else { Read-Secret 'Contraseña a fijar para el owner y los 5 miembros (mín. 8, con mayúscula, número y símbolo)' }

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$Api = $Api.TrimEnd('/')

function Invoke-Api {
    param([string]$Method, [string]$Path, $Body = $null, [string]$Token = $null)
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $params = @{ Method = $Method; Uri = "$Api$Path"; Headers = $headers; ContentType = 'application/json; charset=utf-8' }
    if ($null -ne $Body) {
        # Windows PowerShell 5.1 manda las cadenas en ISO-8859-1: los acentos solo llegan bien en bytes.
        $params['Body'] = [System.Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject $Body -Depth 10 -Compress))
    }
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try { $result = Invoke-RestMethod @params; return $result }
        catch {
            if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 429 -and $attempt -lt 3) {
                Write-Host '    · límite de peticiones: espero 65 s' -ForegroundColor DarkYellow
                Start-Sleep -Seconds 65
                continue
            }
            $err = $_
            break
        }
    }
    if ($err) {
        $detail = if ($err.ErrorDetails) { $err.ErrorDetails.Message } else { '' }
        $status = if ($err.Exception.Response) { [int]$err.Exception.Response.StatusCode } else { '?' }
        throw "API $Method $Path -> $status $detail"
    }
}

function Login([string]$Email) {
    $r = Invoke-Api POST '/api/auth/login' @{ email = $Email; password = $Password }
    if ($r.tokens) { return $r.tokens.accessToken }
    return $r.accessToken
}

function Step([string]$Text) { Write-Host "==> $Text" -ForegroundColor Cyan }

$tz = [TimeZoneInfo]::FindSystemTimeZoneById('SA Pacific Standard Time')
$Today = [TimeZoneInfo]::ConvertTimeFromUtc([DateTime]::UtcNow, $tz).Date
function Day([int]$Offset) { return $Today.AddDays($Offset).ToString('yyyy-MM-dd') }

$script:catalog = @()
function Item([string]$Name, [int]$Sets, [string]$Reps, $Kg, [int]$Rest) {
    $e = $script:catalog | Where-Object { $_.name -eq $Name } | Select-Object -First 1
    if (-not $e) { throw "Ejercicio '$Name' no está en el catálogo" }
    $tt = [string]$e.trackingType

    # "8-10" -> rango, "5" -> fijo, "45 s" -> segundos. Lo que no corresponde al tipo de medición del ejercicio se descarta.
    $secs = $null; $min = $null; $max = $null
    if ($Reps -match '^(\d+)\s*s$') { $secs = [int]$Matches[1] }
    elseif ($Reps -match '^(\d+)-(\d+)$') { $min = [int]$Matches[1]; $max = [int]$Matches[2] }
    elseif ($Reps -match '^\d+$') { $min = [int]$Reps; $max = [int]$Reps }

    $timeOnly = ($tt -match 'Time') -and ($tt -notmatch 'Reps')
    if ($timeOnly) { if (-not $secs) { $secs = 40 }; $min = $null; $max = $null }
    else { if ($null -eq $min) { $min = 12; $max = 12 }; $secs = $null }
    $weight = $null
    if ($tt -match 'Weight') { $weight = $Kg }

    $targets = @()
    for ($n = 0; $n -lt $Sets; $n++) {
        $targets += @{ setType = 'normal'; repsMin = $min; repsMax = $max; weightKg = $weight; durationSeconds = $secs; distanceM = $null; rir = $null }
    }
    return @{ exerciseId = $e.id; setTargets = $targets; restSeconds = $Rest; notes = $null }
}
function Dia([string]$Name, $Items) { return @{ name = $Name; isRest = $false; exercises = @($Items) } }
function Descanso() { return @{ name = 'Descanso'; isRest = $true; exercises = @() } }

# ----------------------------------------------------------------------------------------------
# Rutinas (una por miembro, todas distintas)
# ----------------------------------------------------------------------------------------------
function Routines() {
    return @{
        ppl = @{
            name = 'Hipertrofia PPL · 6 días'; notes = 'Empuje, tirón y pierna dos veces por semana. Subir carga cuando completes todas las series.'
            days = @(
                (Dia 'Empuje A' @((Item 'Press de banca plana con barra' 4 '8-10' 60 120), (Item 'Press militar con barra' 3 '8-10' 35 120), (Item 'Press de banca inclinado con mancuernas' 3 '10' 20 90), (Item 'Elevaciones laterales' 3 '15' 8 60), (Item 'Extensión de tríceps en polea' 3 '12' 25 60))),
                (Dia 'Tirón A' @((Item 'Dominadas' 4 '6-8' $null 120), (Item 'Remo con barra' 4 '8-10' 50 120), (Item 'Jalón al pecho en polea' 3 '10' 45 90), (Item 'Curl de bíceps con barra' 3 '10' 25 60), (Item 'Face pull en polea' 3 '15' 20 60))),
                (Dia 'Pierna A' @((Item 'Sentadilla con barra' 4 '6-8' 80 150), (Item 'Peso muerto rumano' 3 '10' 60 120), (Item 'Prensa de piernas' 3 '12' 140 90), (Item 'Curl femoral tumbado en máquina' 3 '12' 35 60), (Item 'Elevación de talones de pie' 4 '15' 50 45))),
                (Dia 'Empuje B' @((Item 'Press de banca inclinado con mancuernas' 4 '8-10' 24 120), (Item 'Press de hombros con mancuernas' 3 '10' 18 90), (Item 'Aperturas con mancuernas' 3 '12' 12 60), (Item 'Fondos en paralelas' 3 '10' $null 90), (Item 'Press francés con barra' 3 '12' 25 60))),
                (Dia 'Tirón B' @((Item 'Remo con mancuerna a una mano' 4 '10' 28 90), (Item 'Remo en máquina' 3 '12' 50 90), (Item 'Curl martillo' 3 '12' 14 60), (Item 'Elevaciones posteriores (pájaros)' 3 '15' 6 60))),
                (Dia 'Pierna B' @((Item 'Peso muerto convencional' 4 '5' 90 180), (Item 'Sentadilla búlgara' 3 '10' 16 90), (Item 'Hip thrust con barra' 4 '10' 70 90), (Item 'Plancha' 3 '60 s' $null 45))),
                (Descanso)
            )
        }
        fuerza = @{
            name = 'Fuerza 5x5 · Full body 3 días'; notes = 'Tres días no consecutivos. Añadir 2,5 kg cuando completes 5x5.'
            days = @(
                (Dia 'Full body A' @((Item 'Sentadilla con barra' 5 '5' 70 180), (Item 'Press de banca plana con barra' 5 '5' 55 180), (Item 'Remo con barra' 5 '5' 50 180))),
                (Descanso),
                (Dia 'Full body B' @((Item 'Sentadilla con barra' 5 '5' 72.5 180), (Item 'Press militar con barra' 5 '5' 35 180), (Item 'Peso muerto convencional' 1 '5' 90 240))),
                (Descanso),
                (Dia 'Full body A' @((Item 'Sentadilla con barra' 5 '5' 75 180), (Item 'Press de banca plana con barra' 5 '5' 57.5 180), (Item 'Remo con barra' 5 '5' 52.5 180))),
                (Descanso), (Descanso)
            )
        }
        grasa = @{
            name = 'Pérdida de grasa · Circuito 4 días'; notes = 'Circuitos de 3 vueltas con descansos cortos. Mantener el ritmo, no la carga máxima.'
            days = @(
                (Dia 'Circuito A' @((Item 'Burpees' 3 '12' $null 30), (Item 'Zancadas con mancuernas' 3 '12' 10 30), (Item 'Flexiones de pecho' 3 '12' $null 30), (Item 'Escaladores (mountain climbers)' 3 '40 s' $null 30))),
                (Dia 'Circuito B' @((Item 'Swing con kettlebell' 4 '15' 16 30), (Item 'Remo con mancuerna a una mano' 3 '12' 14 30), (Item 'Sentadilla con barra' 3 '15' 30 45), (Item 'Plancha' 3 '45 s' $null 30))),
                (Descanso),
                (Dia 'Circuito C' @((Item 'Salto a la cuerda' 4 '60 s' $null 30), (Item 'Peso muerto con kettlebell' 3 '15' 20 45), (Item 'Fondos entre bancos' 3 '12' $null 30), (Item 'Crunch abdominal' 3 '20' $null 30))),
                (Dia 'Circuito D' @((Item 'Burpees' 4 '10' $null 30), (Item 'Prensa de piernas' 3 '15' 80 45), (Item 'Jalón al pecho en polea' 3 '12' 35 45), (Item 'Elevación de piernas colgado' 3 '10' $null 45))),
                (Descanso), (Descanso)
            )
        }
        principiante = @{
            name = 'Principiante · Full body 3 días'; notes = 'Aprende la técnica con cargas ligeras. Pide corrección al entrenador.'
            days = @(
                (Dia 'Día 1' @((Item 'Prensa de piernas' 3 '12' 60 90), (Item 'Press de banca inclinado con mancuernas' 3 '12' 8 90), (Item 'Jalón al pecho en polea' 3 '12' 30 90), (Item 'Plancha' 3 '30 s' $null 45))),
                (Descanso),
                (Dia 'Día 2' @((Item 'Puente de glúteos' 3 '15' $null 60), (Item 'Press de hombros con mancuernas' 3 '12' 6 90), (Item 'Remo en máquina' 3 '12' 30 90), (Item 'Crunch abdominal' 3 '15' $null 45))),
                (Descanso),
                (Dia 'Día 3' @((Item 'Sentadilla con barra' 3 '10' 20 90), (Item 'Flexiones de pecho' 3 '8' $null 90), (Item 'Remo con mancuerna a una mano' 3 '12' 10 90))),
                (Descanso), (Descanso)
            )
        }
        gluteo = @{
            name = 'Glúteo y pierna · 4 días'; notes = 'Énfasis en glúteo con progresión de carga semanal.'
            days = @(
                (Dia 'Glúteo A' @((Item 'Hip thrust con barra' 4 '10' 60 90), (Item 'Sentadilla búlgara' 3 '10' 12 90), (Item 'Patada de glúteo en polea' 3 '15' 15 60), (Item 'Puente de glúteos' 3 '20' $null 45))),
                (Dia 'Torso ligero' @((Item 'Jalón al pecho en polea' 3 '12' 35 75), (Item 'Press de hombros con mancuernas' 3 '12' 8 75), (Item 'Remo en máquina' 3 '12' 35 75))),
                (Descanso),
                (Dia 'Pierna B' @((Item 'Peso muerto rumano' 4 '10' 45 120), (Item 'Prensa de piernas' 3 '12' 100 90), (Item 'Extensión de cuádriceps en máquina' 3 '15' 30 60), (Item 'Curl femoral tumbado en máquina' 3 '12' 25 60))),
                (Dia 'Glúteo B' @((Item 'Hip thrust con barra' 4 '8' 70 120), (Item 'Zancadas con mancuernas' 3 '12' 10 90), (Item 'Patada de glúteo en polea' 3 '15' 15 60), (Item 'Plancha' 3 '45 s' $null 45))),
                (Descanso), (Descanso)
            )
        }
        torsopierna = @{
            name = 'Torso / Pierna · 4 días'; notes = 'Subir carga cuando completes todas las series con buena técnica.'
            days = @(
                (Dia 'Torso A' @((Item 'Press de banca plana con barra' 4 '8-10' 50 120), (Item 'Remo con barra' 4 '8-10' 45 120), (Item 'Press militar con barra' 3 '10' 30 90), (Item 'Curl de bíceps con barra' 3 '12' 20 60), (Item 'Extensión de tríceps en polea' 3 '12' 20 60))),
                (Dia 'Pierna A' @((Item 'Sentadilla con barra' 4 '6-8' 70 150), (Item 'Peso muerto rumano' 3 '10' 50 120), (Item 'Prensa de piernas' 3 '12' 120 90), (Item 'Elevación de talones de pie' 4 '15' 40 45))),
                (Descanso),
                (Dia 'Torso B' @((Item 'Press de banca inclinado con mancuernas' 4 '10' 18 90), (Item 'Jalón al pecho en polea' 4 '10' 45 90), (Item 'Elevaciones laterales' 3 '15' 8 60), (Item 'Curl martillo' 3 '12' 12 60))),
                (Dia 'Pierna B' @((Item 'Hip thrust con barra' 4 '10' 60 90), (Item 'Sentadilla búlgara' 3 '10' 14 90), (Item 'Curl femoral tumbado en máquina' 3 '12' 30 60), (Item 'Plancha' 3 '45 s' $null 45))),
                (Descanso), (Descanso)
            )
        }
        hiit = @{
            name = 'HIIT funcional · 3 días'; notes = '40 s de trabajo por 20 s de pausa. Ritmo alto, técnica limpia.'
            days = @(
                (Dia 'HIIT A' @((Item 'Burpees' 4 '40 s' $null 20), (Item 'Swing con kettlebell' 4 '40 s' 12 20), (Item 'Escaladores (mountain climbers)' 4 '40 s' $null 20), (Item 'Zancadas con mancuernas' 4 '40 s' 8 20))),
                (Descanso),
                (Dia 'HIIT B' @((Item 'Salto a la cuerda' 5 '45 s' $null 20), (Item 'Remo en TRX' 4 '40 s' $null 20), (Item 'Flexiones de pecho' 4 '40 s' $null 20), (Item 'Subida al cajón' 4 '40 s' $null 20))),
                (Descanso),
                (Dia 'HIIT C' @((Item 'Peso muerto con kettlebell' 4 '40 s' 16 20), (Item 'Lanzamiento de balón medicinal' 4 '40 s' 4 20), (Item 'Plancha' 4 '40 s' $null 20), (Item 'Rueda abdominal' 3 '12' $null 30))),
                (Descanso), (Descanso)
            )
        }
        movilidad = @{
            name = 'Movilidad y core · 3 días'; notes = 'Sesiones suaves para ganar rango de movimiento y estabilidad.'
            days = @(
                (Dia 'Core y espalda' @((Item 'Plancha' 3 '45 s' $null 45), (Item 'Hiperextensiones lumbares' 3 '12' $null 45), (Item 'Crunch abdominal' 3 '15' $null 45), (Item 'Face pull en polea' 3 '15' 15 45))),
                (Descanso),
                (Dia 'Cadera y pierna' @((Item 'Puente de glúteos' 3 '15' $null 45), (Item 'Zancadas con mancuernas' 3 '10' 6 60), (Item 'Buenos días con barra' 3 '12' 15 60))),
                (Descanso),
                (Dia 'Hombro y postura' @((Item 'Elevaciones posteriores (pájaros)' 3 '15' 4 45), (Item 'Remo en TRX' 3 '12' $null 60), (Item 'Aperturas con banda elástica' 3 '15' $null 45))),
                (Descanso), (Descanso)
            )
        }
    }
}

# ----------------------------------------------------------------------------------------------

function Invoke-Sql([string]$Sql) {
    $file = [IO.Path]::GetTempFileName()
    [IO.File]::WriteAllText($file, "\set ON_ERROR_STOP on`n" + $Sql, (New-Object Text.UTF8Encoding $false))
    & (Join-Path $PgBin 'psql.exe') $env:GSM_PROD_DB -q -f $file
    $code = $LASTEXITCODE; Remove-Item $file
    if ($code -ne 0) { throw "psql terminó con código $code" }
}
function Try-Api {
    param([string]$Method, [string]$Path, $Body = $null, [string]$Token = $null)
    try { return Invoke-Api $Method $Path $Body $Token } catch { Write-Host "    · $_" -ForegroundColor DarkYellow; return $null }
}

if ($Continuar) {
    Step 'Continuando con el gym existente'
    $owner = Login $OwnerEmail
    $gymId = (Invoke-Api GET '/api/gym' -Token $owner).id
    $plans = @{}
    foreach ($p in @(Invoke-Api GET '/api/membership-plans' -Token $owner)) {
        $plans[$p.name.ToLowerInvariant()] = @{ id = $p.id; name = $p.name; price = $p.price }
    }
    if ($plans.Count -eq 0) { throw 'El gym no tiene planes: córrelo sin -Continuar sobre un gym nuevo.' }
} else {
# ----------------------------------------------------------------------------------------------
# 1. SuperAdmin: alta del gym
# ----------------------------------------------------------------------------------------------
Step 'SuperAdmin: alta del gym'
$r = Invoke-Api POST '/api/auth/login' @{ email = $SuperAdminEmail; password = $SuperAdminPassword }
$admin = if ($r.tokens) { $r.tokens.accessToken } else { $r.accessToken }

$created = Invoke-Api POST '/api/gyms' @{
    name = 'Forja Training Club'; slug = 'forja-training'; nit = '901555123-4'; phone = '+576013001234'; email = 'contacto@forjagym.test'
    saasPlanId = $null; owner = @{ email = $OwnerEmail; fullName = 'Andrés Castaño' }
    trialDays = 30; monthlyPrice = 0; type = 'Gym'
} -Token $admin
$gymId = $created.gymId
Write-Host "    · gym $gymId, owner $OwnerEmail"

Step 'SuperAdmin: contraseña del owner'
Invoke-Api PUT "/api/admin/gyms/$gymId/accounts/$($created.ownerId)/password" @{ newPassword = $Password } -Token $admin | Out-Null

# ----------------------------------------------------------------------------------------------
# 2. Owner: configuración
# ----------------------------------------------------------------------------------------------
Step 'Owner: datos del gym y todas las opciones encendidas'
$owner = Login $OwnerEmail
$gym = Invoke-Api GET '/api/gym' -Token $owner
Invoke-Api PUT '/api/gym' @{
    name = $gym.name; nit = $gym.nit; phone = $gym.phone; email = $gym.email; logoUrl = $null
    primaryColor = '#B50016'; secondaryColor = '#0AAFCA'; capacity = 90
    legalName = 'Forja Training Club S.A.S.'; address = 'Calle 93 # 13-45'; city = 'Bogotá'
} -Token $owner | Out-Null
Invoke-Api PUT '/api/gym/feature-flags' @{
    showRoutines = $true; showClasses = $true; showProgress = $true; showGamification = $true; showCommunity = $true
    checkinQr = $true; membersCanCreateRoutines = $true; membersCanReportEquipment = $true; showRanking = $true; showFrontDesk = $true
} -Token $owner | Out-Null
Try-Api POST "/api/admin/gyms/$gymId/onboarding/skip" -Token $admin | Out-Null
Try-Api POST '/api/cash-shifts/open' @{ openingCash = 150000 } -Token $owner | Out-Null

Step 'Planes'
$plans = @{}
foreach ($spec in @(
        @{ key = 'mensual';    name = 'Mensual';    description = 'Sala, clases y app.';          price = 140000; days = 30 },
        @{ key = 'trimestral'; name = 'Trimestral'; description = 'Tres meses con descuento.';    price = 380000; days = 90 },
        @{ key = 'semestral';  name = 'Semestral';  description = 'Seis meses, el mejor precio.'; price = 690000; days = 180 })) {
    $p = Invoke-Api POST '/api/membership-plans' @{ name = $spec.name; description = $spec.description; price = $spec.price; durationValue = $spec.days; durationUnit = 'days' } -Token $owner
    $plans[$spec.key] = @{ id = $p.id; name = $spec.name; price = $spec.price }
}

Step 'Clases'
foreach ($c in @(
        @{ name = 'Funcional'; description = 'Circuito de fuerza y cardio.'; instructorName = 'Camilo Pardo'; capacity = 20; startTime = '19:00:00'; durationMinutes = 50; isRecurring = $true; daysOfWeek = @('Monday', 'Wednesday', 'Friday'); scheduledDate = $null },
        @{ name = 'Yoga'; description = 'Flexibilidad y respiración.'; instructorName = 'Valentina Ríos'; capacity = 15; startTime = '07:00:00'; durationMinutes = 60; isRecurring = $true; daysOfWeek = @('Tuesday', 'Thursday'); scheduledDate = $null },
        @{ name = 'Spinning'; description = 'Cardio en bicicleta con música.'; instructorName = 'Laura Mejía'; capacity = 18; startTime = '06:00:00'; durationMinutes = 45; isRecurring = $true; daysOfWeek = @('Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'); scheduledDate = $null },
        @{ name = 'Masterclass de técnica en sentadilla'; description = 'Sesión especial con el head coach.'; instructorName = 'Andrés Castaño'; capacity = 25; startTime = '10:00:00'; durationMinutes = 90; isRecurring = $false; daysOfWeek = $null; scheduledDate = (Day 6) })) {
    Try-Api POST '/api/classes' $c -Token $owner | Out-Null
}

Step 'Novedades'
Try-Api POST '/api/announcements' @{ title = '¡Bienvenidos a Forja!'; body = 'Estrenamos app: registra tus entrenamientos, sigue tu racha y desbloquea logros.'; pinned = $true; expiresOn = $null; sendEmail = $false; category = 'GymNews'; important = $false } -Token $owner | Out-Null
Try-Api POST '/api/announcements' @{ title = 'Reto de noviembre'; body = 'Quien complete 12 entrenamientos este mes entra al sorteo de una camiseta Forja.'; pinned = $false; expiresOn = (Day 22); sendEmail = $false; category = 'GymNews'; important = $true } -Token $owner | Out-Null

}

# ----------------------------------------------------------------------------------------------
# 3. Miembros
# ----------------------------------------------------------------------------------------------
$script:catalog = @(Invoke-Api GET '/api/exercises' -Token $owner)
$routineSpecs = Routines
# prob = constancia (probabilidad de ir un día que le toca entrenar). Con 1 la racha queda intacta.
$members = @(
    @{ first = 'Santiago'; last = 'Cárdenas'; doc = '1020300101'; email = 'santiago.cardenas@forjagym.test'; phone = '3104550101'; plan = 'trimestral'; start = 45; routine = 'ppl';    born = '1994-03-12'; prob = 1.0 },
    @{ first = 'Camila';   last = 'Ortega';   doc = '1020300102'; email = 'camila.ortega@forjagym.test';     phone = '3104550102'; plan = 'semestral';  start = 50; routine = 'fuerza'; born = '1998-07-25'; prob = 1.0 },
    @{ first = 'Mateo';    last = 'Herrera';  doc = '1020300103'; email = 'mateo.herrera@forjagym.test';     phone = '3104550103'; plan = 'mensual';    start = 20; routine = 'grasa';  born = '1989-11-02'; prob = 0.75 },
    @{ first = 'Valeria';  last = 'Montoya';  doc = '1020300104'; email = 'valeria.montoya@forjagym.test';   phone = '3104550104'; plan = 'trimestral'; start = 44; routine = 'gluteo'; born = '1996-05-18'; prob = 0.85 },
    @{ first = 'Sara';     last = 'Lozano';   doc = '1020300105'; email = 'sara.lozano@forjagym.test';       phone = '3104550105'; plan = 'mensual';    start = 12; routine = 'hiit';   born = '2000-12-21'; prob = 0.6 }
)
$methods = @('Cash', 'Nequi', 'Card', 'BankTransfer', 'Cash')

Step 'Miembros: ficha, membresía, pago, rutina, cuenta de la app y entrenamientos'
$i = 0
foreach ($m in $members) {
    $c = Invoke-Api POST '/api/members' @{
        firstName = $m.first; lastName = $m.last; documentType = 'cc'; documentNumber = $m.doc; phone = $m.phone
        email = $m.email; birthDate = $m.born; photoUrl = $null
        emergencyContactName = 'Contacto de emergencia'; emergencyContactRelationship = 'Familiar'; emergencyContactPhone = '3101234567'
    } -Token $owner
    $m.id = $c.id
    $plan = $plans[$m.plan]
    $ms = Invoke-Api POST "/api/members/$($c.id)/memberships" @{ planId = $plan.id; startDate = (Day (-$m.start)) } -Token $owner
    Invoke-Api POST '/api/payments' @{ amount = $plan.price; method = $methods[$i]; concept = "Plan $($plan.name)"; notes = $null; memberId = $c.id; membershipId = $ms.id } -Token $owner | Out-Null
    $routine = Invoke-Api POST "/api/members/$($c.id)/routines" $routineSpecs[$m.routine] -Token $owner

    $inv = Invoke-Api POST "/api/members/$($c.id)/invite" -Token $owner
    Invoke-Api POST '/api/members/register' @{ code = $inv.code; documentNumber = $m.doc; email = $m.email; password = $Password } | Out-Null
    $token = Login $m.email
    $m.token = $token

    # Un registro por ejercicio de cada día de la rutina: sirve de molde para el historial por SQL.
    foreach ($day in (@($routine.days) | Where-Object { -not $_.isRest })) {
        foreach ($ex in @($day.exercises)) {
            $sets = @($ex.setTargets | ForEach-Object {
                    $reps = $_.repsMax; if ($null -eq $reps) { $reps = $_.repsMin }
                    @{ setType = 'normal'; reps = $reps; weightKg = $_.weightKg; durationSeconds = $_.durationSeconds; distanceM = $null; rir = 2 } })
            Try-Api POST "/api/routine-exercises/$($ex.id)/log" @{ notes = $null; sets = $sets } -Token $token | Out-Null
        }
    }
    Write-Host "    · $($m.first) $($m.last) — $($routineSpecs[$m.routine].name)"
    $i++
}

Step 'Ventas de mostrador'
foreach ($sale in @(@('Agua 600 ml', 3000, 'Cash'), @('Proteína whey (sobre)', 9000, 'Nequi'), @('Bebida isotónica', 5500, 'Cash'))) {
    Try-Api POST '/api/payments' @{ amount = $sale[1]; method = $sale[2]; concept = $sale[0]; notes = $null; memberId = $null; membershipId = $null } -Token $owner | Out-Null
}

# ----------------------------------------------------------------------------------------------
# 4. Historial por SQL (6 semanas)
# ----------------------------------------------------------------------------------------------
Step 'Historial: 6 semanas de asistencia y entrenamientos con progresión de carga'
$probs = ($members | ForEach-Object { "('$($_.id)'::uuid, $($_.prob.ToString([Globalization.CultureInfo]::InvariantCulture)))" }) -join ','
Invoke-Sql @"
BEGIN;
CREATE TEMP TABLE p(member_id uuid, prob numeric) ON COMMIT DROP;
INSERT INTO p VALUES $probs;
CREATE TEMP TABLE today_d ON COMMIT DROP AS SELECT (now() AT TIME ZONE 'America/Bogota')::date AS t;

-- Alta, pago y rutina en su fecha real.
UPDATE members.members m SET joined_at = (ms.start_date::timestamp + interval '9 hours') AT TIME ZONE 'America/Bogota',
                             created_at = (ms.start_date::timestamp + interval '9 hours') AT TIME ZONE 'America/Bogota'
FROM memberships.memberships ms WHERE ms.member_id = m.id AND m.gym_id = '$gymId';
UPDATE payments.payments x SET paid_at = (ms.start_date::timestamp + interval '10 hours') AT TIME ZONE 'America/Bogota'
FROM memberships.memberships ms WHERE x.membership_id = ms.id AND x.gym_id = '$gymId';
UPDATE routines.routines SET starts_on = (SELECT t FROM today_d) - 42, created_at = now() - interval '42 days' WHERE gym_id = '$gymId';
UPDATE routines.routine_days SET added_on = (SELECT t FROM today_d) - 42 WHERE gym_id = '$gymId' AND added_on IS NOT NULL;

-- Día de la rutina que toca cada fecha (ciclo rotativo desde starts_on, igual que RoutineCalendar).
CREATE TEMP TABLE cyc ON COMMIT DROP AS
SELECT r.id routine_id, r.member_id, r.starts_on, rd.id day_id, rd.is_rest,
       row_number() OVER (PARTITION BY r.id ORDER BY rd.position) - 1 AS idx, count(*) OVER (PARTITION BY r.id) AS n
FROM routines.routines r JOIN routines.routine_days rd ON rd.routine_id = r.id AND rd.retired_on IS NULL
WHERE r.gym_id = '$gymId' AND r.is_active;

CREATE TEMP TABLE plan ON COMMIT DROP AS
SELECT c.member_id, c.day_id, d::date AS on_date, ((SELECT t FROM today_d) - d::date) AS ago
FROM cyc c CROSS JOIN LATERAL generate_series((SELECT t FROM today_d) - 42, (SELECT t FROM today_d) - 1, interval '1 day') d
WHERE NOT c.is_rest AND ((d::date - c.starts_on) % c.n) = c.idx;

CREATE TEMP TABLE done ON COMMIT DROP AS
SELECT pl.* FROM plan pl JOIN p ON p.member_id = pl.member_id WHERE p.prob >= 1 OR random() < p.prob;

INSERT INTO attendance.attendances (id, gym_id, member_id, checked_in_at, attendance_date, method, created_at, updated_at)
SELECT gen_random_uuid(), '$gymId', d.member_id, x.ts, d.on_date, 'qr', x.ts, x.ts
FROM done d CROSS JOIN LATERAL (SELECT ((d.on_date + CASE WHEN abs(hashtext(d.member_id::text)) % 3 = 0 THEN time '05:45' ELSE time '18:10' END
      + (abs(hashtext(d.member_id::text || d.on_date::text)) % 80) * interval '1 minute')::timestamp AT TIME ZONE 'America/Bogota') AS ts) x
ON CONFLICT DO NOTHING;

-- Registros: copia del molde de hoy, con la carga del 80 % hace 6 semanas al 100 % ayer (múltiplos de 2,5 kg).
INSERT INTO routines.routine_exercise_logs (id, gym_id, member_id, routine_exercise_id, exercise_id, performed_on, completed_at, notes, sets, created_at, updated_at)
SELECT gen_random_uuid(), l.gym_id, l.member_id, l.routine_exercise_id, l.exercise_id, d.on_date, x.ts, NULL,
       (SELECT jsonb_agg((SELECT jsonb_object_agg(kv.k, CASE WHEN lower(kv.k) = 'weightkg' AND jsonb_typeof(kv.v) = 'number'
                 THEN to_jsonb(round(((kv.v::text)::numeric * (1 - 0.2 * d.ago / 42.0)) / 2.5) * 2.5) ELSE kv.v END)
                 FROM jsonb_each(e) AS kv(k, v)))
        FROM jsonb_array_elements(l.sets) e),
       x.ts, x.ts
FROM done d
JOIN routines.routine_exercises re ON re.routine_day_id = d.day_id AND re.retired_on IS NULL
JOIN routines.routine_exercise_logs l ON l.routine_exercise_id = re.id AND l.performed_on = (SELECT t FROM today_d)
CROSS JOIN LATERAL (SELECT ((d.on_date + time '19:00' + re."order" * interval '7 minutes')::timestamp AT TIME ZONE 'America/Bogota') AS ts) x;

-- Hoy solo queda registrado el día que le toca hoy.
DELETE FROM routines.routine_exercise_logs l USING routines.routine_exercises re, cyc c
WHERE l.routine_exercise_id = re.id AND re.routine_day_id = c.day_id
  AND l.gym_id = '$gymId' AND l.performed_on = (SELECT t FROM today_d)
  AND (((SELECT t FROM today_d) - c.starts_on) % c.n) <> c.idx;
COMMIT;
"@

# ----------------------------------------------------------------------------------------------
# 5. Entrada de hoy por la API (evalúa los logros con todo el historial) y reservas de clases
# ----------------------------------------------------------------------------------------------
Step 'Entrada de hoy en recepción (evalúa logros)'
foreach ($m in $members) {
    $r = Try-Api POST '/api/attendance/checkin' @{ memberId = $m.id } -Token $owner
    if ($r -and $r.unlockedAchievements.Count -gt 0) {
        Write-Host ("    · {0}: {1}" -f $m.first, (($r.unlockedAchievements | ForEach-Object { $_.name }) -join ', '))
    }
}

Step 'Reservas de clases de la próxima semana'
$upcoming = Try-Api GET "/api/classes/schedule?from=$(Day 1)&to=$(Day 7)" -Token $owner
foreach ($m in $members) {
    $pool = @($upcoming.entries) | Where-Object { $null -ne $_.capacity }
    if ($pool.Count -eq 0) { break }
    foreach ($e in ($pool | Get-Random -Count ([Math]::Min(3, $pool.Count)))) {
        Try-Api POST "/api/classes/$($e.classId)/sessions/$($e.sessionDate)/book" -Token $m.token | Out-Null
    }
}

Write-Host ''
Write-Host 'Listo.' -ForegroundColor Green
Write-Host "  Owner:    $OwnerEmail"
Write-Host "  Miembros: $(($members | ForEach-Object { $_.email }) -join ', ')"
Write-Host '  Todos con la contraseña que escribiste al inicio.'
