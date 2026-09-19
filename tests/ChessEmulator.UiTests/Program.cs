using ChessEmulator.TestKit;

namespace ChessEmulator.UiTests;

/// <summary>
/// Проверки контролов интерфейса без показа окон: доска, палитра и панель редактора,
/// запись партии, шкала оценки и отрисовка фигур.
/// Ключ --verbose печатает каждую проверку.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Test.Init(args);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        PieceRendererTests.Run();
        BoardControlTests.Run();
        PositionEditorPanelTests.Run();
        MoveListViewTests.Run();
        EvalBarTests.Run();

        return Test.Report("Интерфейс");
    }
}
