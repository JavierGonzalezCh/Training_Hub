<#
.SYNOPSIS
  Crea un carril de trabajo aislado: worktree + bases de datos propias + .env.lane (docs/V2-BUILD-PLAN.md §2.1).

.DESCRIPTION
  - Worktree en ../gsm-lanes/<carril> desde la rama de integración `v2` (salvo -SkipWorktree).
  - Bases gsm_dev_<carril> y gsm_test_<carril> (se crean si no existen) con las migraciones aplicadas.
  - .env.lane en la carpeta de código con GSM_TEST_DATABASE, la cadena de dev y los puertos
    API = 7300 + 10*n y web = 7301 + 10*n.

.PARAMETER Lane
  Nombre del carril: minúsculas, dígitos y guion bajo (p. ej. members, x0probe).
.PARAMETER Number
  Número del carril n para los puertos (por defecto 0).
.PARAMETER SkipWorktree
  No crea el worktree: usa el repositorio donde vive este script (el integrador ya lo creó con su herramienta).
.PARAMETER BaseBranch
  Rama desde la que sale el worktree (por defecto v2).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)][string]$Lane,
    [int]$Number = 0,
    [switch]$SkipWorktree,
    [string]$BaseBranch = 'v2'
)

$ErrorActionPreference = 'Stop'

if ($Lane -notmatch '^[a-z][a-z0-9_]{0,30}$') {
    throw "Nombre de carril inválido '$Lane': usa minúsculas, dígitos y guion bajo (empieza por letra)."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$devDb = "gsm_dev_$Lane"
$testDb = "gsm_test_$Lane"
$apiPort = 7300 + 10 * $Number
$webPort = 7301 + 10 * $Number

# --- psql ---
$psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
if (-not (Test-Path $psql)) { $psql = 'psql' }
$env:PGPASSWORD = 'gsm'

function Invoke-Psql([string]$Database, [string]$Sql) {
    $out = & $psql -U gsm -h localhost -d $Database -v ON_ERROR_STOP=1 -tAc $Sql
    if ($LASTEXITCODE -ne 0) { throw "psql falló: $Sql" }
    return $out
}

# --- worktree ---
$codeDir = $repoRoot
if (-not $SkipWorktree) {
    $lanesRoot = Join-Path (Split-Path $repoRoot -Parent) 'gsm-lanes'
    $codeDir = Join-Path $lanesRoot $Lane
    if (Test-Path $codeDir) { throw "Ya existe $codeDir." }
    New-Item -ItemType Directory -Force $lanesRoot | Out-Null
    git -C $repoRoot worktree add -b "lane/$Lane" $codeDir $BaseBranch
    if ($LASTEXITCODE -ne 0) { throw 'git worktree add falló.' }
}

# --- bases de datos ---
foreach ($db in @($devDb, $testDb)) {
    $exists = Invoke-Psql 'postgres' "SELECT 1 FROM pg_database WHERE datname = '$db'"
    if (-not $exists) {
        Invoke-Psql 'postgres' "CREATE DATABASE $db" | Out-Null
        Write-Host "Base creada: $db"
    }
    else {
        Write-Host "Base ya existía: $db"
    }
}

# --- migraciones ---
$infra = Join-Path $codeDir 'src\Backend\GSM.Infrastructure'
foreach ($db in @($devDb, $testDb)) {
    $env:GSM_MIGRATIONS_CONNECTION = "Host=localhost;Port=5432;Database=$db;Username=gsm;Password=gsm"
    Write-Host "Aplicando migraciones a $db ..."
    # --context: desde LG1 hay dos DbContext (GsmDbContext y LogsDbContext, este sin migraciones propias).
    dotnet ef database update --project $infra --context GsmDbContext | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet ef database update falló en $db." }
}
Remove-Item Env:GSM_MIGRATIONS_CONNECTION -ErrorAction SilentlyContinue

# --- .env.lane ---
$envFile = Join-Path $codeDir '.env.lane'
@"
# Generado por scripts/new-lane.ps1 (no se commitea).
GSM_LANE=$Lane
GSM_TEST_DATABASE=$testDb
GSM_MIGRATIONS_CONNECTION=Host=localhost;Port=5432;Database=$devDb;Username=gsm;Password=gsm
ConnectionStrings__Postgres=Host=localhost;Port=5432;Database=$devDb;Username=gsm;Password=gsm
GSM_API_PORT=$apiPort
GSM_WEB_PORT=$webPort
"@ | Set-Content -Path $envFile -Encoding utf8

Write-Host ""
Write-Host "Carril '$Lane' listo en $codeDir"
Write-Host "  tests : `$env:GSM_TEST_DATABASE='$testDb'"
Write-Host "  dev   : $devDb   (API $apiPort, web $webPort)"
Write-Host "  config: $envFile"
