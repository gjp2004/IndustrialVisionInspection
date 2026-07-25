param(
    [string]$Dotnet = "dotnet",
    [int]$DurationSeconds = 1800
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$report = Join-Path $projectRoot "artifacts\stability-report.json"
& $Dotnet run --project "$projectRoot\tools\IndustrialVisionStudent.StabilityRunner\IndustrialVisionStudent.StabilityRunner.csproj" `
    -c Release -- $DurationSeconds $report
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
