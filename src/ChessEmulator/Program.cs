using ChessEmulator.UI;

namespace ChessEmulator;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            MessageBox.Show("Непредвиденная ошибка: " + e.Exception.Message, "Chess Emulator",
                MessageBoxButtons.OK, MessageBoxIcon.Error);

        // Первый аргумент — путь к PGN-файлу (чтобы открывать партии двойным щелчком).
        var pgnPath = args.FirstOrDefault(a => a.EndsWith(".pgn", StringComparison.OrdinalIgnoreCase) && File.Exists(a));

        try
        {
            Application.Run(new MainForm(pgnPath));
        }
        catch (Exception ex)
        {
            ReportFatal(ex);
        }
    }

    /// <summary>Пишет подробности сбоя в файл рядом с настройками и показывает сообщение.</summary>
    private static void ReportFatal(Exception ex)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ChessEmulator", "crash.log");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTime.Now:u}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Записать журнал не удалось — покажем хотя бы сообщение.
        }

        MessageBox.Show($"Программа завершилась с ошибкой:{Environment.NewLine}{ex.Message}" +
                        $"{Environment.NewLine}{Environment.NewLine}Подробности: {path}",
            "Chess Emulator", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
