$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$githubRoot = Join-Path $projectRoot ".github"
$requiredFiles = @(
    "workflows\ci.yml",
    "workflows\release.yml",
    "dependabot.yml",
    "ISSUE_TEMPLATE\bug_report.yml",
    "ISSUE_TEMPLATE\feature_request.yml",
    "ISSUE_TEMPLATE\config.yml"
)

foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $githubRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing GitHub configuration file: $relativePath"
    }
}

$yamlFiles = Get-ChildItem -LiteralPath $githubRoot -Recurse -File |
    Where-Object { $_.Extension -in ".yml", ".yaml" }
foreach ($file in $yamlFiles) {
    $lines = @(Get-Content -LiteralPath $file.FullName -Encoding UTF8)
    for ($index = 0; $index -lt $lines.Count; $index++) {
        $line = $lines[$index]
        if ($line.Contains("`t")) {
            throw "$($file.FullName):$($index + 1) contains a tab."
        }
        if ($line -match "\s+$") {
            throw "$($file.FullName):$($index + 1) has trailing whitespace."
        }
        if ($line -match "^( +)") {
            $spaces = $Matches[1].Length
            if ($spaces % 2 -ne 0) {
                throw "$($file.FullName):$($index + 1) uses odd indentation."
            }
        }
    }
}

$ci = Get-Content -LiteralPath (Join-Path $githubRoot "workflows\ci.yml") -Raw -Encoding UTF8
$release = Get-Content -LiteralPath (Join-Path $githubRoot "workflows\release.yml") -Raw -Encoding UTF8
if ($ci -notmatch "dotnet test" -or $ci -notmatch "smoke-published\.ps1") {
    throw "CI workflow must run tests and the published smoke test."
}
if ($release -notmatch "tags:" -or
    $release -notmatch "gh release create" -or
    $release -notmatch "smoke-published\.ps1") {
    throw "Release workflow must be tag-triggered, smoke-tested, and create a GitHub Release."
}
if ($release -match "dotnet publish[^\r\n]*-r win-x64[^\r\n]*--no-restore" -and
    $release -notmatch "dotnet restore IndustrialVisionStudent\.csproj -r win-x64") {
    throw "Release workflow must restore the application for win-x64 before publishing with --no-restore."
}

Write-Host "GitHub configuration validation passed. YAML files: $($yamlFiles.Count)"
