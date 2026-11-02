param([ValidateSet('run','migrate')][string]$Action = 'run')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$previous = @{}
Push-Location $root
try {
    if (-not (Test-Path .env)) { throw 'Execute Initialize-Local.ps1 antes.' }
    foreach ($line in Get-Content .env | Where-Object { $_ -and -not $_.StartsWith('#') }) {
        $parts = $line.Split('=', 2)
        $previous[$parts[0]] = [Environment]::GetEnvironmentVariable($parts[0], 'Process')
        [Environment]::SetEnvironmentVariable($parts[0], $parts[1], 'Process')
    }
    $project = 'src/FiapCloudGames.Catalog.Api/FiapCloudGames.Catalog.Api.csproj'
    if ($Action -eq 'run') { dotnet run --project $project }
    else { dotnet run --project $project -- "--$Action" }
    if ($LASTEXITCODE -ne 0) { throw "Operação $Action falhou." }
} finally {
    foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') }
    Pop-Location
}
