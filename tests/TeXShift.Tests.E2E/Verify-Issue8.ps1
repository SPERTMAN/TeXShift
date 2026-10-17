[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$runner = Join-Path $PSScriptRoot 'bin/x64/Debug/net48/TeXShift.Tests.E2E.exe'
$fixture = Join-Path $repoRoot 'tests/TestData/OneNote/issue8-mixed-native-and-latex.xml'
$outputRoot = Join-Path $repoRoot 'TestResults/issue8-regression'

function Invoke-Issue8Case {
    param([string]$Name, [string[]]$Selection)

    $output = Join-Path $outputRoot $Name
    $arguments = @('convert', '--page', $fixture, '--output', $output)
    if ($Selection.Count -gt 0) { $arguments += @('--select') + $Selection }
    & $runner @arguments
    if ($LASTEXITCODE) { throw "Issue #8 case failed: $Name" }

    $final = Get-ChildItem -LiteralPath $output -Filter 'F04_Final_Page_XML_Basic_*.xml' |
        Sort-Object LastWriteTime | Select-Object -Last 1
    if (-not $final) { throw "Issue #8 case produced no final XML: $Name" }
    $xml = Get-Content -LiteralPath $final.FullName -Raw -Encoding UTF8
    if ($xml -notmatch '<mi>f</mi>' -or $xml -notmatch '<mi>L</mi><mrow><mi>d</mi><mi>a</mi><mi>t</mi><mi>a</mi></mrow>') {
        throw "Issue #8 case lost the original native formula: $Name"
    }
    if (($xml -split '<!--\[if mathML\]>').Count -lt 4) {
        throw "Issue #8 case lost the converted formula: $Name"
    }
    if (($xml -split '<one:List').Count -lt 3) {
        throw "Issue #8 case lost the nested list structure: $Name"
    }
    Write-Output "${Name}: PASS"
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
Invoke-Issue8Case 'whole-outline' @('Context')
Invoke-Issue8Case 'nested-paragraph' @('\mathcal{J}')
