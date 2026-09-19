// Проверки правил игры, нотации, дерева партии, PGN и строителя позиции.
// Ключи: --verbose (печатать каждую проверку), --full (глубокие прогоны perft).
using ChessEmulator.CoreTests;
using ChessEmulator.TestKit;

Test.Init(args);

TypesTests.Run();
PositionTests.Run();
PerftTests.Run();
SanTests.Run();
GameTests.Run();
PgnTests.Run();
PositionBuilderTests.Run();

return Test.Report("Правила игры");
