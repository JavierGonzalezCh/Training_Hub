#!/usr/bin/env pwsh
# Regenera el cliente tipado de la API (GSM.Client/Generated/GsmApiClient.g.cs) desde el
# documento OpenAPI que sirve la API en Development. El código generado se versiona en el repo:
# si el diff cambia, es que cambió un contrato.
#
# Requisitos: la API corriendo en Development (dotnet run en src/Backend/GSM.API).
# Uso: pwsh scripts/generate-client.ps1 [-OpenApiUrl <url>]
#
# Nota: GSM.Client/Generated/ReadResponseAsString.cs vive fuera de GsmApiClient.g.cs a propósito
# y no se toca al regenerar. Corrige un defecto de la plantilla de NSwag (ReadResponseAsString
# queda en false, así que GsmApiException.Response llega vacío en el camino feliz y ApiError no
# puede leer el detail/title de un 409/422/403). Si añades un cliente nuevo al OpenAPI, añade
# también su partial class ahí y regístralo en GsmClientServiceCollectionExtensions.cs.

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

# Defecto de NSwag: para una colección de enum en el CUERPO de una respuesta (no un enum suelto),
# el generador infiere "integer" aunque en runtime viaje como string (JsonStringEnumConverter
# global). Rompe la deserialización con un JsonException que ApiError no puede leer con claridad.
# Hoy solo afecta a ClassListItem/ClassResponse.DaysOfWeek — si el diff de esta regeneración trae
# otra ICollection<int> nueva en una respuesta (no en un request), sospecha lo mismo.
$genFile = Join-Path $clientDir "Generated/GsmApiClient.g.cs"
$content = Get-Content $genFile -Raw
$before = '        [System.Text.Json.Serialization.JsonPropertyName("daysOfWeek")]
        public System.Collections.Generic.ICollection<int> DaysOfWeek { get; set; } = new System.Collections.ObjectModel.Collection<int>();'
$after = '        [System.Text.Json.Serialization.JsonPropertyName("daysOfWeek")]
        public System.Collections.Generic.ICollection<string> DaysOfWeek { get; set; } = new System.Collections.ObjectModel.Collection<string>();'
$occurrences = ([regex]::Matches($content, [regex]::Escape($before))).Count
if ($occurrences -gt 0) {
    Write-Host "Aplicando el parche de DaysOfWeek ($occurrences ocurrencia(s)) ..."
    $content = $content.Replace($before, $after)
    Set-Content -Path $genFile -Value $content -NoNewline
}
else {
    Write-Warning "El parche de DaysOfWeek no encontró nada que reemplazar: revisa a mano si ClassListItem/ClassResponse siguen usando ICollection<int>."
}

Write-Host "Listo. Revisa el diff de src/Frontend/GSM.Client/Generated/."
