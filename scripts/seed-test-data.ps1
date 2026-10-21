<#
.SYNOPSIS
    Siembra datos de prueba por la API: 2 gimnasios (ya creados con su owner y 2 staff) con planes, 4 miembros con
    cuenta en la app, membresías, pagos y una rutina distinta por miembro.

.DESCRIPTION
    Antes hay que tener los gimnasios con su owner (la alta con contraseña conocida no existe por la API: el owner
    nace sin contraseña y la elige por correo). Este script inicia sesión como cada owner y hace el resto con las
    reglas de negocio de la API. Es idempotente por documento: si el primer miembro del gym ya existe, lo omite.

    -Api       URL base de la API (sin barra final).
    -Password  Contraseña de los owners y de las cuentas de miembro creadas.
#>
param(
    [Parameter(Mandatory)][string]$Api,
    [Parameter(Mandatory)][string]$Password
)

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
# Gimnasios
# ----------------------------------------------------------------------------------------------
$gyms = @(
    @{
        name = 'Iron Forge Gym'; owner = 'owner@ironforge.test'
        phone = '+576011234567'; email = 'contacto@ironforge.test'; color = '#B50016'
        address = 'Calle 85 # 15-20'; city = 'Bogotá'; legal = 'Iron Forge S.A.S.'
        plans = @(
            @{ key = 'mensual';    name = 'Mensual';    description = 'Acceso ilimitado a sala.';      price = 120000; days = 30 },
            @{ key = 'trimestral'; name = 'Trimestral'; description = 'Tres meses con descuento.';     price = 320000; days = 90 },
            @{ key = 'estudiante'; name = 'Estudiante'; description = 'Mensual con carné vigente.';    price = 90000;  days = 30 }
        )
        members = @(
            @{ first = 'Santiago'; last = 'Cárdenas'; doc = '1020300001'; email = 'santiago.cardenas@correo.test'; plan = 'trimestral'; start = 20; routine = 'ppl'; born = '1994-03-12' },
            @{ first = 'Camila';   last = 'Ortega';   doc = '1020300002'; email = 'camila.ortega@correo.test';     plan = 'mensual';    start = 8;  routine = 'fuerza'; born = '1998-07-25' },
            @{ first = 'Mateo';    last = 'Herrera';  doc = '1020300003'; email = 'mateo.herrera@correo.test';     plan = 'mensual';    start = 27; routine = 'grasa'; born = '1989-11-02' },
            @{ first = 'Isabela';  last = 'Roa';      doc = '1020300004'; email = 'isabela.roa@correo.test';       plan = 'estudiante'; start = 40; routine = 'principiante'; born = '2003-01-30' }
        )
    },
    @{
        name = 'Pulse Fitness Studio'; owner = 'owner@pulsefitness.test'
        phone = '+576017654321'; email = 'contacto@pulsefitness.test'; color = '#0AAFCA'
        address = 'Carrera 43A # 7-50'; city = 'Medellín'; legal = 'Pulse Fitness Studio S.A.S.'
        plans = @(
            @{ key = 'mensual';    name = 'Mensual';    description = 'Sala y clases grupales.';       price = 150000; days = 30 },
            @{ key = 'trimestral'; name = 'Trimestral'; description = 'Tres meses, mejor precio.';    price = 400000; days = 90 },
            @{ key = 'quincenal';  name = 'Quincenal';  description = 'Quince días de acceso.';        price = 80000;  days = 15 }
        )
        members = @(
            @{ first = 'Valeria';    last = 'Montoya'; doc = '1030400001'; email = 'valeria.montoya@correo.test'; plan = 'mensual';    start = 5;  routine = 'gluteo'; born = '1996-05-18' },
            @{ first = 'Juan Pablo'; last = 'Duarte';  doc = '1030400002'; email = 'juanpablo.duarte@correo.test'; plan = 'trimestral'; start = 60; routine = 'torsopierna'; born = '1991-09-09' },
            @{ first = 'Sara';       last = 'Lozano';  doc = '1030400003'; email = 'sara.lozano@correo.test';     plan = 'quincenal';  start = 14; routine = 'hiit'; born = '2000-12-21' },
            @{ first = 'Tomás';      last = 'Beltrán'; doc = '1030400004'; email = 'tomas.beltran@correo.test';   plan = $null;        start = 0;  routine = 'movilidad'; born = '1985-04-04' }
        )
    }
)

$methods = @('Cash', 'Nequi', 'Card', 'BankTransfer')
$summary = @()

foreach ($g in $gyms) {
    Step "$($g.name): sesión como $($g.owner)"
    $owner = Login $g.owner

    $gym = Invoke-Api GET '/api/gym' -Token $owner
    Invoke-Api PUT '/api/gym' @{
        name = $gym.name; nit = $gym.nit; phone = $g.phone; email = $g.email
        logoUrl = $gym.logoUrl; primaryColor = $g.color; secondaryColor = '#0AAFCA'
        capacity = 80; legalName = $g.legal; address = $g.address; city = $g.city
    } -Token $owner | Out-Null
    Invoke-Api PUT '/api/gym/feature-flags' @{
        showRoutines = $true; showClasses = $true; showProgress = $true; showGamification = $true
        showCommunity = $false; checkinQr = $true; communityComments = $false; membersCanCreateRoutines = $true
    } -Token $owner | Out-Null

    # Los pagos en efectivo exigen la caja del turno abierta.
    try { Invoke-Api POST '/api/cash-shifts/open' @{ openingCash = 100000 } -Token $owner | Out-Null } catch { Write-Host '    · caja ya abierta' }

    $script:catalog = @(Invoke-Api GET '/api/exercises' -Token $owner)
    $routineSpecs = Routines

    Step 'Planes'
    $plans = @{}
    $existingPlans = @(Invoke-Api GET '/api/membership-plans' -Token $owner)
    foreach ($spec in $g.plans) {
        $p = $existingPlans | Where-Object { $_.name -eq $spec.name } | Select-Object -First 1
        if (-not $p) { $p = Invoke-Api POST '/api/membership-plans' @{
            name = $spec.name; description = $spec.description; price = $spec.price; durationValue = $spec.days; durationUnit = 'days'
        } -Token $owner }
        $plans[$spec.key] = @{ id = $p.id; name = $spec.name; price = $spec.price }
    }

    Step 'Miembros, membresías, pagos, rutinas y cuenta de la app'
    $i = 0
    $createdAny = $false
    foreach ($m in $g.members) {
        $found = Invoke-Api GET "/api/members?search=$($m.doc)" -Token $owner
        if ($found.total -gt 0) {
            # Ya existe (corrida anterior interrumpida): solo se completa la cuenta de la app, si faltaba.
            Write-Host "    · $($m.first) $($m.last) ya existe: se completa su cuenta" -ForegroundColor Yellow
            try {
                $inv = Invoke-Api POST "/api/members/$($found.items[0].id)/invite" -Token $owner
                Invoke-Api POST '/api/members/register' @{ code = $inv.code; documentNumber = $m.doc; email = $m.email; password = $Password } | Out-Null
            } catch { }
            $i++; continue
        }
        $createdAny = $true
        $created = Invoke-Api POST '/api/members' @{
            firstName = $m.first; lastName = $m.last; documentType = 'cc'; documentNumber = $m.doc
            phone = '3{0}' -f (100000000 + [int]$m.doc.Substring(6) * 7919 + $i * 13579)
            email = $m.email; birthDate = $m.born; photoUrl = $null
            emergencyContactName = 'Contacto de emergencia'; emergencyContactRelationship = 'Familiar'; emergencyContactPhone = '3101234567'
        } -Token $owner

        if ($m.plan) {
            $plan = $plans[$m.plan]
            $ms = Invoke-Api POST "/api/members/$($created.id)/memberships" @{ planId = $plan.id; startDate = (Day (-$m.start)) } -Token $owner
            Invoke-Api POST '/api/payments' @{
                amount = $plan.price; method = $methods[$i % $methods.Count]; concept = "Plan $($plan.name)"
                notes = $null; memberId = $created.id; membershipId = $ms.id
            } -Token $owner | Out-Null
        }

        $routine = Invoke-Api POST "/api/members/$($created.id)/routines" $routineSpecs[$m.routine] -Token $owner

        $invite = Invoke-Api POST "/api/members/$($created.id)/invite" -Token $owner
        Invoke-Api POST '/api/members/register' @{ code = $invite.code; documentNumber = $m.doc; email = $m.email; password = $Password } | Out-Null

        # El primer día de entrenamiento de su rutina ya registrado hoy, para que la app muestre progreso.
        $token = Login $m.email
        $firstDay = @($routine.days) | Where-Object { -not $_.isRest } | Select-Object -First 1
        foreach ($ex in @($firstDay.exercises)) {
            $sets = @($ex.setTargets | ForEach-Object {
                    $reps = $_.repsMax; if ($null -eq $reps) { $reps = $_.repsMin }
                    @{ setType = 'normal'; reps = $reps; weightKg = $_.weightKg; durationSeconds = $_.durationSeconds; distanceM = $null; rir = $null }
                })
            try { Invoke-Api POST "/api/routine-exercises/$($ex.id)/log" @{ notes = $null; sets = $sets } -Token $token | Out-Null } catch { }
        }
        Write-Host "    · $($m.first) $($m.last) — $($routineSpecs[$m.routine].name)"
        $i++
    }

    if (-not $createdAny) { continue }
    Step 'Ventas de mostrador'
    foreach ($sale in @(@('Agua 600 ml', 3000, 'Cash'), @('Proteína whey (sobre)', 8000, 'Cash'), @('Bebida isotónica', 5000, 'Nequi'))) {
        Invoke-Api POST '/api/payments' @{ amount = $sale[1]; method = $sale[2]; concept = $sale[0]; notes = $null; memberId = $null; membershipId = $null } -Token $owner | Out-Null
    }
}

Write-Host 'Listo.' -ForegroundColor Green
