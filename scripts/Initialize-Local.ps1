param(
    [string]$InfrastructureEnv = (Join-Path $PSScriptRoot '../../fiap-cloud-games-infrastructure/.env'),
    [string]$UsersEnv = (Join-Path $PSScriptRoot '../../fiap-cloud-games-users/.env')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$target = Join-Path $root '.env'
if (Test-Path -LiteralPath $target) { Write-Output '.env existente preservado.'; exit 0 }
function Read-Settings([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { throw 'Informe os arquivos .env locais de Infraestrutura e Users.' }
    $settings = @{}
    Get-Content -LiteralPath $path | Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object { $parts = $_.Split('=', 2); $settings[$parts[0]] = $parts[1] }
    return $settings
}
$infra = Read-Settings $InfrastructureEnv
$users = Read-Settings $UsersEnv
foreach ($required in @('CATALOG_DB_PASSWORD','POSTGRES_PORT','RABBITMQ_PORT','RABBITMQ_PASSWORD')) { if (-not $infra[$required]) { throw "Configuração obrigatória ausente: $required" } }
foreach ($required in @('Jwt__Key','Jwt__Issuer','Jwt__Audience')) { if (-not $users[$required]) { throw "Configuração obrigatória ausente: $required" } }
$random = [Security.Cryptography.RandomNumberGenerator]::Create()
function New-LocalSecret { $bytes = New-Object byte[] 32; $random.GetBytes($bytes); [BitConverter]::ToString($bytes).Replace('-', '').ToLowerInvariant() }
try {
    $lines = @(
        "ConnectionStrings__Catalog=Host=127.0.0.1;Port=$($infra.POSTGRES_PORT);Database=fcg_catalog;Username=fcg_catalog;Password=$($infra.CATALOG_DB_PASSWORD)",
        "CATALOG_DOCKER_CONNECTION=Host=postgres;Port=5432;Database=fcg_catalog;Username=fcg_catalog;Password=$($infra.CATALOG_DB_PASSWORD)",
        "Jwt__Key=$($users.Jwt__Key)", "Jwt__Issuer=$($users.Jwt__Issuer)", "Jwt__Audience=$($users.Jwt__Audience)",
        'RabbitMq__Host=127.0.0.1', "RabbitMq__Port=$($infra.RABBITMQ_PORT)", 'RabbitMq__VirtualHost=fcg', 'RabbitMq__Username=fcg_local', "RabbitMq__Password=$($infra.RABBITMQ_PASSWORD)",
        "TEST_DB_PASSWORD=$(New-LocalSecret)", "TEST_RABBIT_PASSWORD=$(New-LocalSecret)", 'TEST_DB_PORT=55434', 'TEST_RABBIT_PORT=5674',
        'ASPNETCORE_URLS=http://127.0.0.1:5082'
    )
    [IO.File]::WriteAllLines($target, $lines, (New-Object Text.UTF8Encoding($false)))
} finally { $random.Dispose() }
Write-Output '.env local criado sem imprimir credenciais. Não versionar.'
