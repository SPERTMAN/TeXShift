[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [ValidateSet('ribbon', 'core')]
    [string]$Driver = 'ribbon',
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

function Invoke-Case {
    param([string]$Name, [string[]]$Arguments, [int]$ExpectedExit = 0)
    $output = Join-Path $OutputDirectory $Name
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $expectedDriver = 'xml'
    if ($Arguments[0] -in @('convert', 'reverse')) {
        $Arguments += @('--driver', $Driver)
        $expectedDriver = $Driver
    }
    $json = & $runner @Arguments --output $output 2> (Join-Path $output 'stderr.txt')
    $code = $LASTEXITCODE
    $result = ($json -join "`n") | ConvertFrom-Json
    if ($result.driver -ne $expectedDriver) { throw "$Name used the wrong conversion driver." }
    if ($code -ne $ExpectedExit -or $result.cleanupStatus -ne 'passed') {
        throw "$Name failed: exit=$code cleanup=$($result.cleanupStatus) $($result.error)"
    }
    if ($ExpectedExit -eq 0) {
        $final = Join-Path $output $result.files.finalXml
        if (-not (Test-Path -LiteralPath $final)) { throw "$Name did not produce final XML." }
        Write-Host "${Name}: PASS"
        $xml = [IO.File]::ReadAllText($final)
        [xml]$page = $xml
        $text = ($page.SelectNodes('//*[local-name()="Outline"]//*[local-name()="T"]') | ForEach-Object InnerText) -join "`n"
        return @{ Path = $final; Xml = $xml; Text = $text }
    }
    if ($Driver -eq 'ribbon' -and -not $result.files.errorDialog) { throw "$Name did not capture the error dialog." }
    Write-Host "${Name}: PASS (expected failure, driver=$Driver)"
}

if ($InputFile) {
    Invoke-Case 'input' @('convert', '--input', (Resolve-Path -LiteralPath $InputFile).Path) | Out-Null
    exit 0
}

$markdown = "# Smoke test`n`nA **bold** word.`n`nSecond paragraph."
foreach ($selection in @('caret', 'outline')) {
    $result = Invoke-Case $selection @('convert', '--markdown', $markdown, "--$selection", 'Second paragraph')
    if ($result.Text.Contains('# Smoke test') -or $result.Text.Contains('**bold**')) {
        throw "$selection left Markdown unconverted."
    }
}
$range = "Before **untouched**.`n`nFirst **selected**.`nSecond *selected*.`n`nAfter **untouched**."
$result = Invoke-Case 'range' @('convert', '--markdown', $range, '--select', 'First', 'Second')
if ($result.Text.Contains('**selected**') -or $result.Text.Contains('*selected*') -or
    -not $result.Text.Contains('Before **untouched**') -or -not $result.Text.Contains('After **untouched**')) {
    throw 'Range conversion changed surrounding content or missed the selection.'
}
$math = Invoke-Case 'math' @('convert', '--input', (Join-Path $repoRoot 'tests/TestData/MarkdownCases/math_inline.md'))
if (-not $math.Xml.Contains('[if mathML]')) { throw 'Math conversion produced no native equation.' }
$reverse = Invoke-Case 'reverse' @('reverse', '--page', $math.Path)
if ($reverse.Text.Contains('[if mathML]') -or -not $reverse.Text.Contains('$')) {
    throw 'Reverse conversion did not restore the formula source.'
}
$mermaid = Invoke-Case 'mermaid' @('convert', '--input', (Join-Path $repoRoot 'tests/TestData/MarkdownCases/mermaid_simple.md'))
if (-not $mermaid.Xml.Contains('<one:Image')) { throw 'Mermaid conversion produced no image.' }

$empty = Join-Path $OutputDirectory 'empty.md'
[IO.File]::WriteAllText($empty, '')
try { Invoke-Case 'empty' @('convert', '--input', $empty) 1 }
finally { Remove-Item -LiteralPath $empty }

Invoke-Case 'write-xml' @('write-xml', '--markdown', 'Original text', '--outline', 'Original text',
    '--changes', (Join-Path $repoRoot 'tests/TestData/OneNoteWrites/replace-text.xml')) | Out-Null
