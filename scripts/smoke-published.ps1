param(
    [string]$Executable
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Executable)) {
    $projectFile = Join-Path $projectRoot "IndustrialVisionStudent.csproj"
    [xml]$projectXml = Get-Content -LiteralPath $projectFile -Raw -Encoding UTF8
    $version = [string]$projectXml.Project.PropertyGroup.Version
    $Executable = Join-Path $projectRoot `
        "release\IndustrialVisionStudent-v$version-win-x64\IndustrialVisionStudent.exe"
}

$resolvedExecutable = (Get-Item -LiteralPath $Executable).FullName
Write-Host "Running published self-test: $resolvedExecutable"
$process = Start-Process -FilePath $resolvedExecutable `
    -ArgumentList "--self-test" -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0) {
    throw "Published self-test failed with exit code $($process.ExitCode)."
}
Write-Host "Published self-test passed."
