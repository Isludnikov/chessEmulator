using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

// Контролы WinForms держат общее состояние процесса, а часть проверок зависит
// от времени (анимация шкалы оценки), поэтому классы тестов не идут параллельно.
[assembly: Parallelization(Mode = ParallelMode.None)]

namespace ChessEmulator.UiTests;

internal static class UiTestSetup
{
    /// <summary>
    /// Общие для процесса настройки Windows Forms. Должны выполниться до создания
    /// первого контрола: SetCompatibleTextRenderingDefault бросает исключение,
    /// если окно уже создано. Инициализатор модуля срабатывает раньше любого теста —
    /// раньше, чем это делал прежний [STAThread] Main.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
    }
}
