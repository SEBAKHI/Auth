[CmdletBinding()]
param(
    [ValidateRange(0, 100)]
    [double]$ChangedLineThreshold = 90,
    [ValidateRange(0, 100)]
    [double]$BackendLineFloor = 52.3,
    [string]$Base
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$backendResults = Join-Path $repositoryRoot 'TestResults/Coverage'
$frontendRoot = Join-Path $repositoryRoot 'Auth_UI'

dotnet test (Join-Path $repositoryRoot 'Auth/Auth_API.Tests/Auth_API.Tests.csproj') `
    --no-restore `
    --nologo `
    --verbosity minimal `
    --collect:'XPlat Code Coverage' `
    --settings (Join-Path $repositoryRoot 'Auth/coverage.runsettings') `
    --results-directory $backendResults
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Push-Location $frontendRoot
try {
    pnpm test:coverage
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}

$backendCoverage = Get-ChildItem $backendResults -Recurse -Filter 'coverage.json' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $backendCoverage) {
    throw 'The backend coverage collector produced no coverage.json report.'
}

$verifierArgs = @(
    (Join-Path $repositoryRoot 'Tools/verify-changed-coverage.mjs'),
    '--frontend', (Join-Path $frontendRoot 'coverage/coverage-final.json'),
    '--backend', $backendCoverage.FullName,
    '--threshold', $ChangedLineThreshold,
    '--backend-floor', $BackendLineFloor
)
if ($Base) { $verifierArgs += @('--base', $Base) }

node @verifierArgs
exit $LASTEXITCODE
