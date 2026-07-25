param(
    [string]$Dotnet = "dotnet",
    [string]$Git = "git",
    [int]$MaximumFileSizeMb = 10,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    Write-Host "[1/7] Checking Git worktree"
    & $Git rev-parse --is-inside-work-tree | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "The current directory is not a Git repository." }
    & $Git diff --check
    if ($LASTEXITCODE -ne 0) { throw "Git diff contains whitespace errors." }

    Write-Host "[2/7] Checking candidate file sizes"
    $candidates = @(
        & $Git -c core.quotepath=false ls-files --cached
        & $Git -c core.quotepath=false ls-files --others --exclude-standard
    ) | Sort-Object -Unique
    $maximumBytes = $MaximumFileSizeMb * 1MB
    $largeFiles = foreach ($relativePath in $candidates) {
        if (Test-Path -LiteralPath $relativePath) {
            $item = Get-Item -LiteralPath $relativePath
            if ($item.Length -gt $maximumBytes) {
                "$relativePath ($([Math]::Round($item.Length / 1MB, 2)) MB)"
            }
        }
    }
    if ($largeFiles) {
        throw "Candidate files exceed ${MaximumFileSizeMb} MB:`n$($largeFiles -join "`n")"
    }

    Write-Host "[3/7] Scanning common secret patterns"
    $textExtensions = @(
        ".cs", ".xaml", ".csproj", ".json", ".md", ".ps1", ".yml", ".yaml",
        ".xml", ".config", ".txt", ".gitignore", ".gitattributes"
    )
    $secretPattern =
        "ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|" +
        "AKIA[0-9A-Z]{16}|BEGIN (RSA |OPENSSH )?PRIVATE KEY"
    $secretMatches = foreach ($relativePath in $candidates) {
        if ((Test-Path -LiteralPath $relativePath) -and
            $textExtensions -contains [IO.Path]::GetExtension($relativePath).ToLowerInvariant()) {
            Select-String -LiteralPath $relativePath -Pattern $secretPattern -ErrorAction Stop
        }
    }
    if ($secretMatches) {
        throw "Potential secrets found; review before pushing:`n$($secretMatches -join "`n")"
    }

    Write-Host "[4/7] Validating GitHub configuration"
    & "$PSScriptRoot\validate-github-config.ps1"
    if ($LASTEXITCODE -ne 0) { throw "GitHub configuration validation failed." }

    Write-Host "[5/7] Running Release tests"
    $testArguments = @(
        "test",
        "tests\IndustrialVisionStudent.Tests\IndustrialVisionStudent.Tests.csproj",
        "-c", "Release",
        "--nologo"
    )
    if ($NoRestore) { $testArguments += "--no-restore" }
    & $Dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { throw "Automated tests failed." }

    Write-Host "[6/7] Publishing self-contained Windows build"
    if ($NoRestore) {
        & "$PSScriptRoot\publish.ps1" -Dotnet $Dotnet -NoRestore
    }
    else {
        & "$PSScriptRoot\publish.ps1" -Dotnet $Dotnet
    }
    if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

    Write-Host "[7/7] Running published application self-test"
    & "$PSScriptRoot\smoke-published.ps1"
    if ($LASTEXITCODE -ne 0) { throw "Published application self-test failed." }

    Write-Host "Pre-push checks passed. Candidate files: $($candidates.Count)"
}
finally {
    Pop-Location
}
