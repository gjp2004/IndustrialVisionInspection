param(
    [string]$Dotnet = "dotnet",
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$arguments = @(
    "test",
    "$projectRoot\tests\IndustrialVisionStudent.Tests\IndustrialVisionStudent.Tests.csproj",
    "-c", "Release",
    "--nologo"
)
if ($NoRestore) { $arguments += "--no-restore" }
& $Dotnet @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
