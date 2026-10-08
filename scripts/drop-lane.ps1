<#
.SYNOPSIS
  Limpia un carril creado con new-lane.ps1: borra gsm_dev_<carril>, gsm_test_<carril> y su worktree.

.DESCRIPTION
  Solo toca bases cuyo nombre sea exactamente gsm_dev_<carril> o gsm_test_<carril>; rechaza cualquier
  otro nombre (nunca gsm ni gsm_test). El worktree se borra solo si existe en ../gsm-lanes/<carril>.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)][string]$Lane
)

$ErrorActionPreference = 'Stop'

if ($Lane -notmatch '^[a-z][a-z0-9_]{0,30}$') {
    throw "Nombre de carril inválido '$Lane'."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
if (-not (Test-Path $psql)) { $psql = 'psql' }
$env:PGPASSWORD = 'gsm'

foreach ($db in @("gsm_dev_$Lane", "gsm_test_$Lane")) {
    if ($db -notmatch '^gsm_(dev|test)_[a-z][a-z0-9_]*$') {
        throw "Rechazado: '$db' no es una base de carril."
    }
    & $psql -U gsm -h localhost -d postgres -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS $db WITH (FORCE)"
    if ($LASTEXITCODE -ne 0) { throw "No se pudo borrar $db." }
}

$worktree = Join-Path (Join-Path (Split-Path $repoRoot -Parent) 'gsm-lanes') $Lane
if (Test-Path $worktree) {
    git -C $repoRoot worktree remove --force $worktree
    if ($LASTEXITCODE -ne 0) { throw 'git worktree remove falló.' }
    git -C $repoRoot branch -D "lane/$Lane" 2>$null | Out-Null
    Write-Host "Worktree borrado: $worktree"
}

Write-Host "Carril '$Lane' limpiado."
