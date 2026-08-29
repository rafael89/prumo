$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$apiSettingsPath = Join-Path $repositoryRoot 'src/Lodira.Api/appsettings.json'
$environment = $env:DOTNET_ENVIRONMENT
if ([string]::IsNullOrWhiteSpace($environment)) { $environment = $env:ASPNETCORE_ENVIRONMENT }
if ([string]::IsNullOrWhiteSpace($environment)) { $environment = 'Development' }

function Stop-Reset([string] $message) {
    Write-Error "ABORT: $message"
    exit 1
}

if ($environment -ne 'Development') { Stop-Reset "o ambiente atual é '$environment'; o reset só pode executar em Development." }
if (-not (Test-Path -LiteralPath $apiSettingsPath)) { Stop-Reset "appsettings.json da API não encontrado." }

$settings = Get-Content -LiteralPath $apiSettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$connectionString = [string]$settings.ConnectionStrings.Postgres
if ([string]::IsNullOrWhiteSpace($connectionString)) { Stop-Reset 'ConnectionStrings:Postgres não configurada.' }

function Get-ConnectionPart([string] $name) {
    $match = [regex]::Match($connectionString, "(?i)(?:^|;)\s*$name\s*=\s*([^;]*)")
    if (-not $match.Success) { Stop-Reset "parte '$name' não encontrada na connection string." }
    return $match.Groups[1].Value.Trim()
}

$hostName = Get-ConnectionPart 'Host'
$port = Get-ConnectionPart 'Port'
$database = Get-ConnectionPart 'Database'
$username = Get-ConnectionPart 'Username'
if ($hostName -notin @('localhost', '127.0.0.1', '::1')) { Stop-Reset "host remoto não permitido: $hostName" }

Write-Host ''
Write-Host 'Lodira - Local Database Reset' -ForegroundColor Cyan
Write-Host ''
Write-Host "Environment : $environment"
Write-Host "Host        : $hostName"
Write-Host "Database    : $database"
Write-Host ''
Write-Host 'WARNING: todos os dados locais do Lodira serão apagados permanentemente.' -ForegroundColor Yellow
$confirmation = Read-Host 'Digite RESET para continuar'
if ($confirmation -cne 'RESET') { Stop-Reset 'confirmação não recebida.' }

$psqlCandidates = @(
    (Get-Command psql -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
    'C:\Program Files\PostgreSQL\18\bin\psql.exe',
    'C:\Program Files\PostgreSQL\17\bin\psql.exe',
    'C:\Program Files\PostgreSQL\16\bin\psql.exe'
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
if (-not $psqlCandidates) { Stop-Reset 'psql não encontrado. Instale o cliente PostgreSQL ou adicione psql ao PATH.' }
$psql = [string]$psqlCandidates

$password = Read-Host "Senha do usuário local '$username' (não será armazenada)" -AsSecureString
$passwordBstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($password)
$plainPassword = $null
try {
    $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordBstr)
    $env:PGPASSWORD = $plainPassword
    $env:ConnectionStrings__Postgres = "Host=$hostName;Port=$port;Database=$database;Username=$username;Password=$plainPassword"

    Push-Location $repositoryRoot
    try {
        Write-Host '[1/4] Resetando o schema local...'
        & $psql --host $hostName --port $port --username $username --dbname $database --set ON_ERROR_STOP=1 --command 'DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public;'
        if ($LASTEXITCODE -ne 0) { Stop-Reset 'falha ao resetar o schema PostgreSQL.' }

        Write-Host '[2/4] Recriando o schema pela baseline EF...'
        & dotnet ef database update --project src/Lodira.Infrastructure --startup-project src/Lodira.Api --context LodiraDbContext --no-build
        if ($LASTEXITCODE -ne 0) { Stop-Reset 'falha ao aplicar as migrations EF.' }

        Write-Host '[3/4] Validando histórico de migrations...'
        $migrationFiles = @(Get-ChildItem -LiteralPath 'src/Lodira.Infrastructure/Persistence/Migrations' -Filter '*.cs' | Where-Object { $_.Name -match '^\d+_.+\.cs$' -and $_.Name -notmatch '\.Designer\.cs$' })
        if ($migrationFiles.Count -eq 0) { Stop-Reset 'nenhuma migration foi encontrada.' }
        $expectedMigrations = @($migrationFiles | ForEach-Object { $_.BaseName })
        $history = (& $psql --host $hostName --port $port --username $username --dbname $database --tuples-only --no-align --command='SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\";')
        if ($LASTEXITCODE -ne 0) { Stop-Reset 'falha ao consultar o histórico de migrations.' }
        $actualMigrations = @($history | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        if (($actualMigrations -join '|') -ne ($expectedMigrations -join '|')) { Stop-Reset "histórico inesperado. Esperado: $($expectedMigrations -join ', '); encontrado: $($actualMigrations -join ', ')." }

        Write-Host '[4/4] Validando banco vazio...'
        $tables = @('Assets', 'Portfolios', 'PortfolioPositions', 'Documents', 'ProcessingJobs', 'DocumentAnalyses', 'RadarItemStates', 'PortfolioAllocationStrategies', 'StrategyAllocations', 'PortfolioImportSessions', 'InvestmentObjectives', 'InvestmentEvents', 'InvestmentEventEvidence', 'PortfolioAssessments')
        foreach ($table in $tables) {
            $sql = 'SELECT count(*) FROM \"' + $table + '\";'
            $count = (& $psql --host $hostName --port $port --username $username --dbname $database --tuples-only --no-align --command=$sql)
            if ($LASTEXITCODE -ne 0) { Stop-Reset "falha ao validar a tabela $table." }
            if ([int64]$count.Trim() -ne 0) { Stop-Reset "a tabela $table não está vazia." }
        }
        Write-Host ''
        Write-Host 'Local Lodira database reset successfully.' -ForegroundColor Green
        Write-Host "Migration history: $($actualMigrations -join ', ')"
        Write-Host 'Domain tables: empty'
    }
    finally {
        Pop-Location
    }
}
finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
    Remove-Item Env:ConnectionStrings__Postgres -ErrorAction SilentlyContinue
    if ($passwordBstr -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordBstr) }
    if ($plainPassword) { $plainPassword = $null }
}
