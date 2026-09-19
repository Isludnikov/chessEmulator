// Проверки обёртки UCI, типов движка и настроек приложения.
// Движок для проверки — поддельный FakeUciEngine.exe рядом с тестом; путь можно задать аргументом.
// Ключ --verbose печатает каждую проверку.
using ChessEmulator.EngineTests;
using ChessEmulator.TestKit;

Test.Init(args);

EngineTypesTests.Run();
SettingsTests.Run();

var enginePath = args.FirstOrDefault(a => !a.StartsWith("--"))
                 ?? Path.Combine(AppContext.BaseDirectory, "FakeUciEngine.exe");

if (!File.Exists(enginePath))
{
    Console.WriteLine($"Не найден движок для теста: {enginePath}");
    return 1;
}

await UciEngineTests.RunAsync(enginePath);

return Test.Report("Движок и настройки");
