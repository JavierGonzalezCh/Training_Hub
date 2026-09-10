#!/usr/bin/env pwsh
# Regenera el cliente tipado de la API (GSM.Client/Generated/GsmApiClient.g.cs) desde el
# documento OpenAPI que sirve la API en Development. El código generado se versiona en el repo:
# si el diff cambia, es que cambió un contrato.
#
# Requisitos: la API corriendo en Development (dotnet run en src/Backend/GSM.API).
# Uso: pwsh scripts/generate-client.ps1 [-OpenApiUrl <url>]

param(
    [string]$OpenApiUrl = "https://localhost:7219/openapi/v1.json"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$clientDir = Join-Path $repoRoot "src/Frontend/GSM.Client"
$specPath = Join-Path $clientDir "openapi-v1.json"

Write-Host "Descargando OpenAPI de $OpenApiUrl ..."
# -SkipCertificateCheck: el certificado de desarrollo es autofirmado.
Invoke-WebRequest -Uri $OpenApiUrl -OutFile $specPath -SkipCertificateCheck

Write-Host "Generando el cliente con NSwag ..."
Push-Location $clientDir
try {
    dotnet tool restore | Out-Null
    dotnet nswag run nswag.json
}
finally {
    Pop-Location
}

Write-Host "Listo. Revisa el diff de src/Frontend/GSM.Client/Generated/."
