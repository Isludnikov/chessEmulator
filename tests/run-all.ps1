# Прогоняет все наборы тестов и возвращает ненулевой код, если хоть один провалился.
# Ключи передаются тестам как есть: --verbose (печатать каждую проверку), --full (глубокие perft).
param([Parameter(ValueFromRemainingArguments = $true)] $TestArgs)

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
$projects = @(
    'tests\ChessEmulator.CoreTests',
    'tests\ChessEmulator.EngineTests',
    'tests\ChessEmulator.UiTests'
)

$failed = @()
foreach ($project in $projects) {
    Write-Host ""
    Write-Host "=== $project ===" -ForegroundColor Cyan
    & dotnet run --project (Join-Path $root $project) -c Release -- @TestArgs
    if ($LASTEXITCODE -ne 0) { $failed += $project }
}

Write-Host ""
if ($failed.Count -eq 0) {
    Write-Host "Все наборы тестов пройдены." -ForegroundColor Green
    exit 0
}

Write-Host ("Провалены наборы: " + ($failed -join ', ')) -ForegroundColor Red
exit 1
