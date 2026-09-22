# Прогоняет все наборы тестов через xUnit. Ненулевой код возврата — что-то упало.
# Ключ -Full добавляет глубокие прогоны perft (переменная среды CHESS_TESTS_FULL).
param([switch]$Full, [Parameter(ValueFromRemainingArguments = $true)] $TestArgs)

$root = Split-Path -Parent $PSScriptRoot
if ($Full) { $env:CHESS_TESTS_FULL = '1' }

try {
    & dotnet test (Join-Path $root 'ChessEmulator.sln') -c Release @TestArgs
    exit $LASTEXITCODE
}
finally {
    if ($Full) { Remove-Item Env:\CHESS_TESTS_FULL -ErrorAction SilentlyContinue }
}
