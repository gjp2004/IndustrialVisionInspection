param(
    [string]$Dotnet = "dotnet",
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot "IndustrialVisionStudent.csproj"
[xml]$projectXml = Get-Content -LiteralPath $projectFile -Raw -Encoding UTF8
$version = [string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw "The project Version is missing." }
$output = Join-Path $projectRoot "release\IndustrialVisionStudent-v$version-win-x64"

$arguments = @(
    "publish", $projectFile,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=false",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-o", $output,
    "--nologo"
)
if ($NoRestore) { $arguments += "--no-restore" }
& $Dotnet @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Publish completed: $output"
