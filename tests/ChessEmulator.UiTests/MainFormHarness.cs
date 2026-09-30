using System.Reflection;
using ChessEmulator.App;
using ChessEmulator.Chess;
using ChessEmulator.UI;

namespace ChessEmulator.UiTests;

/// <summary>
/// Доступ к внутренностям главного окна. Окно не показывается, поэтому движок не запускается;
/// закрытые поля и методы достаём рефлексией — снаружи у формы нет ничего, кроме конструктора.
/// </summary>
internal static class MainFormHarness
{
    private const BindingFlags Private =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>Выполняет действие, направив файл настроек во временную папку.</summary>
    public static void WithSettingsFile(Action body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ChessEmulatorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable(AppSettings.PathOverrideVariable, Path.Combine(dir, "settings.json"));
        try
        {
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppSettings.PathOverrideVariable, null);
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    private static FieldInfo FindField(string name) =>
        typeof(MainForm).GetField(name, Private) ?? throw new MissingFieldException(nameof(MainForm), name);

    public static T Field<T>(MainForm form, string name) => (T)FindField(name).GetValue(form)!;

    public static void SetField(MainForm form, string name, object? value) => FindField(name).SetValue(form, value);

    /// <summary>Вызывает закрытый метод формы; для статического метода форма не нужна.</summary>
    public static object? Call(MainForm? form, string name, params object?[] args)
    {
        var method = typeof(MainForm).GetMethod(name, Private)
                     ?? throw new MissingMethodException(nameof(MainForm), name);
        return method.Invoke(form, args);
    }

    /// <summary>Нажатие клавиши так, как его видит форма. Возвращает true, если клавиша обработана.</summary>
    public static bool Key(MainForm form, Keys key) => (bool)Call(form, "ProcessCmdKey", new Message(), key)!;

    public static Game GameOf(MainForm form) => Field<Game>(form, "_game");

    /// <summary>Делает ходы за пользователя тем же путём, что и доска.</summary>
    public static Game Play(MainForm form, params string[] uciMoves)
    {
        var game = GameOf(form);
        foreach (var uci in uciMoves)
        {
            var move = game.CurrentPosition.LegalMoves.First(m => m.ToUci() == uci);
            Call(form, "ApplyUserMove", move);
        }
        return game;
    }

    /// <summary>Нажимает кнопку формы с указанной надписью.</summary>
    public static void Press(MainForm form, string text) => UiHarness.Press(UiHarness.ByText<Button>(form, text));

    /// <summary>Включает редактор позиции. Без движка переход завершается сразу, ждать нечего.</summary>
    public static void EnterEditor(MainForm form)
    {
        var task = (Task)Call(form, "BeginPositionEditAsync")!;
        if (!task.IsCompleted) throw new InvalidOperationException("Вход в редактор не завершился сразу.");
        task.GetAwaiter().GetResult();
    }
}
