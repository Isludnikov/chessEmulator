namespace ChessEmulator.Engine;

/// <summary>Поиск исполняемого файла Stockfish в типичных местах.</summary>
public static class EngineLocator
{
    private static readonly string[] Names =
    {
        "stockfish.exe",
        "stockfish-windows-x86-64-avx2.exe",
        "stockfish-windows-x86-64-bmi2.exe",
        "stockfish-windows-x86-64-sse41-popcnt.exe",
        "stockfish-windows-x86-64.exe"
    };

    /// <summary>Возвращает все найденные варианты движка (без повторов).</summary>
    public static List<string> FindAll()
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in CandidateDirectories())
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            DirectoryInfo info;
            try
            {
                info = new DirectoryInfo(dir);
                if (!info.Exists) continue;
            }
            catch
            {
                continue;
            }

            foreach (var name in Names)
            {
                var path = Path.Combine(info.FullName, name);
                if (File.Exists(path) && seen.Add(path)) found.Add(path);
            }

            // Файлы вида stockfish*.exe в каталоге
            try
            {
                foreach (var file in info.EnumerateFiles("stockfish*.exe"))
                {
                    if (seen.Add(file.FullName)) found.Add(file.FullName);
                }
            }
            catch
            {
                // нет прав на каталог — пропускаем
            }
        }

        return found;
    }

    public static string? FindFirst() => FindAll().FirstOrDefault();

    private static IEnumerable<string> CandidateDirectories()
    {
        var appDir = AppContext.BaseDirectory;
        yield return appDir;
        yield return Path.Combine(appDir, "engine");
        yield return Path.Combine(appDir, "engines");
        yield return Path.Combine(appDir, "stockfish");

        // Каталоги рядом с исходниками (удобно при запуске из IDE)
        var dir = new DirectoryInfo(appDir);
        for (var i = 0; i < 5 && dir?.Parent != null; i++)
        {
            dir = dir.Parent;
            yield return Path.Combine(dir.FullName, "engine");
            yield return Path.Combine(dir.FullName, "engines");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(localAppData, "ChessEmulator", "engine");
        yield return Path.Combine(localAppData, "Programs", "stockfish");

        foreach (var special in new[]
                 {
                     Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86,
                     Environment.SpecialFolder.UserProfile
                 })
        {
            var root = Environment.GetFolderPath(special);
            if (string.IsNullOrEmpty(root)) continue;
            yield return Path.Combine(root, "Stockfish");
            yield return Path.Combine(root, "stockfish");
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile))
        {
            yield return Path.Combine(userProfile, "Downloads");
            yield return Path.Combine(userProfile, "Downloads", "stockfish");
        }

        foreach (var pathDir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                 .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            yield return pathDir.Trim('"');
        }
    }
}
