using System.Reflection;
using ChessEmulator.Engine;
using ChessEmulator.UI;
using Xunit;

namespace ChessEmulator.UiTests;

/// <summary>Окно «Журнал UCI…» без показа на экране.</summary>
public sealed class UciLogFormTests
{
    private static TextBox Text(Form form) => (TextBox)form.Controls.Find("uciLogText", true)[0];
    private static CheckBox ShowInfo(Form form) => (CheckBox)form.Controls.Find("uciLogShowInfo", true)[0];

    /// <summary>Сколько обработчиков подписано на журнал (у события поле с тем же именем).</summary>
    private static int SubscriberCount(UciLog log)
    {
        var field = typeof(UciLog).GetField("EntryAdded", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return ((Delegate?)field.GetValue(log))?.GetInvocationList().Length ?? 0;
    }

    /// <summary>Записи копятся и переносятся в окно пачкой по таймеру — без окна дёргаем вручную.</summary>
    private static void Flush(Form form) =>
        form.GetType().GetMethod("Flush", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, Array.Empty<object>());

    [Fact(DisplayName = "Журнал UCI: строки анализа скрыты, остальные видны")]
    public void ФильтрInfo()
    {
        var log = new UciLog();
        log.Add("> position startpos");
        log.Add("< info depth 12 pv e2e4");
        log.Add("< bestmove e2e4");

        using var form = new UciLogForm(log);

        var shown = Text(form).Text;
        Assert.Contains("> position startpos", shown);  // команда видна
        Assert.Contains("< bestmove e2e4", shown);  // ответ виден
        Assert.DoesNotContain("info depth 12", shown);  // анализ скрыт по умолчанию

        ShowInfo(form).Checked = true;
        Assert.Contains("info depth 12", Text(form).Text);  // с галочкой виден и анализ
    }

    [Fact(DisplayName = "Журнал UCI: новые записи дописываются")]
    public void ДописываниеЗаписей()
    {
        var log = new UciLog();
        using var form = new UciLogForm(log);

        log.Add("> go movetime 1000");
        log.Add("< info depth 1 pv e2e4");
        log.Add("< bestmove e2e4");
        Flush(form);

        var shown = Text(form).Text;
        Assert.Contains("> go movetime 1000", shown);  // команда попала в окно
        Assert.Contains("< bestmove e2e4", shown);  // и ответ тоже
        Assert.DoesNotContain("info depth 1", shown);  // фильтр работает и на лету

        Assert.True(shown.IndexOf("> go movetime 1000", StringComparison.Ordinal)
                    < shown.IndexOf("< bestmove e2e4", StringComparison.Ordinal),
            "порядок сохранён");

        // При закрытии окно отписывается от журнала: иначе оно копило бы строки вечно
        // и трогало уже освобождённый TextBox.
        Assert.Equal(1, SubscriberCount(log));
        form.Dispose();
        Assert.Equal(0, SubscriberCount(log));
        Assert.Null(Record.Exception(() => log.Add("> stop")));  // журнал живёт дальше без окна
    }
}
