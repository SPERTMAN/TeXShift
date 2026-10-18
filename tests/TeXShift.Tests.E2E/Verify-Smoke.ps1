[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [ValidateSet('com', 'ui')]
    [string[]]$Mode = @('com', 'ui'),
    [string]$InputFile,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$runner = Join-Path $PSScriptRoot "bin/x64/$Configuration/net48/TeXShift.Tests.E2E.exe"
if (-not (Test-Path -LiteralPath $runner)) { throw "Build $Configuration before running smoke tests." }
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot ('TestResults/smoke-' + [Guid]::NewGuid().ToString('N'))
}

$failed = $false
foreach ($runMode in $Mode) {
    $runOutput = Join-Path $OutputDirectory $runMode
    $arguments = @('convert', '--mode', $runMode, '--output', $runOutput)
    if ($InputFile) {
        $arguments += @('--input', (Resolve-Path -LiteralPath $InputFile).Path)
    } else {
        $arguments += @('--markdown', "# Smoke test`n`nA **bold** word and an ordinary paragraph.`n`nSecond paragraph.")
        $arguments += @('--caret', 'Second paragraph')
    }
    & $runner @arguments
    if ($LASTEXITCODE -ne 0) { $failed = $true }
}
if ($failed) { exit 1 }
exit 0
