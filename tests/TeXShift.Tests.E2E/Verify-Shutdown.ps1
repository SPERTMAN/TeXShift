[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$runner = Join-Path $PSScriptRoot 'bin/x64/Debug/net48/TeXShift.Tests.E2E.exe'
$output = Join-Path $repoRoot 'TestResults/shutdown-regression'
$addInDll = Join-Path $repoRoot 'src/TeXShift.AddIn/bin/x64/Debug/net48/TeXShift.AddIn.dll'
$coreDll = Join-Path (Split-Path $addInDll) 'TeXShift.Core.dll'

# Resolve identities from registration so the check covers both installed flavors.
$classIds = @('TeXShift.AddIn.Debug.Connect', 'TeXShift.AddIn.Connect') | ForEach-Object {
    $key = "Registry::HKEY_CLASSES_ROOT\$_\CLSID"
    if (Test-Path $key) { (Get-Item $key).GetValue('') }
}
if (-not $classIds) { throw 'Register the Debug add-in before running shutdown regression.' }

function Get-RemainingProcesses {
    Get-CimInstance Win32_Process | Where-Object {
        if ($_.Name -eq 'ONENOTE.EXE') { return $true }
        if ($_.Name -ne 'dllhost.exe') { return $false }
        foreach ($classId in $classIds) {
            if ($_.CommandLine -like "*$classId*") { return $true }
        }
        return $false
    }
}

function Assert-Exited {
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $remaining = @(Get-RemainingProcesses)
        if ($remaining.Count -eq 0) { break }
        Start-Sleep -Milliseconds 300
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($remaining.Count) { throw "Processes remain: $($remaining.ProcessId -join ', ')" }
    foreach ($path in @($addInDll, $coreDll)) {
        $stream = [IO.File]::Open($path, 'Open', 'ReadWrite', 'None')
        $stream.Dispose()
    }
}

Assert-Exited
New-Item -ItemType Directory -Path $output -Force | Out-Null
& $runner dump --hierarchy --output (Join-Path $output 'hierarchy.xml')
if ($LASTEXITCODE) { throw 'Read-only lifecycle failed.' }
Assert-Exited
Write-Output 'Read-only lifecycle: PASS'
Remove-Item -LiteralPath (Join-Path $output 'hierarchy.xml')

foreach ($name in @('math_inline', 'mermaid_simple')) {
    & $runner convert --input (Join-Path $repoRoot "tests/TestData/MarkdownCases/$name.md") --output (Join-Path $output $name)
    if ($LASTEXITCODE) { throw "$name conversion failed." }
    Assert-Exited
    Write-Output "$name lifecycle: PASS"
}
