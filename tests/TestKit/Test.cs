using System.Collections;
using System.Diagnostics;
using System.Globalization;

namespace ChessEmulator.TestKit;

/// <summary>
/// Маленький проверочный движок для тестов проекта: разделы, счёт проверок и итог.
/// По умолчанию печатает одну строку на раздел, подробности — только о провалах;
/// с ключом --verbose печатает каждую проверку.
/// </summary>
public static class Test
{
    private sealed class SuiteState
    {
        public string Name = string.Empty;
        public int Checks;
        public readonly List<string> Failures = new();
        public readonly Stopwatch Clock = Stopwatch.StartNew();
    }

    private static SuiteState? _current;
    private static int _totalChecks;
    private static int _totalFailures;
    private static int _suites;

    /// <summary>Печатать каждую проверку, а не только итог раздела (--verbose).</summary>
    public static bool Verbose { get; set; }

    /// <summary>Выполнять долгие проверки — глубокие perft и прочее (--full).</summary>
    public static bool Full { get; set; }

    public static void Init(string[] args)
    {
        Verbose = args.Contains("--verbose") || args.Contains("-v");
        Full = args.Contains("--full");
    }

    // ------------------------------------------------------------- Разделы

    public static void Suite(string name)
    {
        Flush();
        _current = new SuiteState { Name = name };
        _suites++;
        if (Verbose) Console.WriteLine($"\n── {name}");
    }

    /// <summary>Запускает раздел проверок; исключение внутри засчитывается как провал.</summary>
    public static void Suite(string name, Action body)
    {
        Suite(name);
        try
        {
            body();
        }
        catch (Exception ex)
        {
            Record($"раздел прерван исключением: {ex.GetType().Name}: {ex.Message}", false);
        }
    }

    private static void Flush()
    {
        if (_current == null) return;
        var s = _current;
        _current = null;

        var time = s.Clock.ElapsedMilliseconds >= 100
            ? $", {s.Clock.ElapsedMilliseconds} мс"
            : string.Empty;

        if (s.Failures.Count == 0)
        {
            Console.WriteLine($"[ ok ] {s.Name} — {Plural(s.Checks)}{time}");
        }
        else
        {
            Console.WriteLine($"[ПРОВАЛ] {s.Name} — {Plural(s.Checks)}, провалено {s.Failures.Count}{time}");
            foreach (var failure in s.Failures) Console.WriteLine($"        ! {failure}");
        }
    }

    private static string Plural(int n)
    {
        int tens = n % 100, ones = n % 10;
        var word = tens is >= 11 and <= 14 ? "проверок"
            : ones == 1 ? "проверка"
            : ones is >= 2 and <= 4 ? "проверки"
            : "проверок";
        return $"{n} {word}";
    }

    // ------------------------------------------------------------ Проверки

    public static void Check(string name, object? expected, object? actual)
    {
        string e = Format(expected), a = Format(actual);
        var ok = e == a;
        Record(ok ? name : $"{name}: ожидалось «{e}», получено «{a}»", ok);
    }

    public static void True(string name, bool value) => Check(name, true, value);

    public static void False(string name, bool value) => Check(name, false, value);

    public static void Near(string name, double expected, double actual, double tolerance)
    {
        var ok = Math.Abs(expected - actual) <= tolerance;
        Record(ok ? name : $"{name}: ожидалось {Format(expected)} ± {Format(tolerance)}, получено {Format(actual)}", ok);
    }

    public static void Throws<TException>(string name, Action action) where TException : Exception
    {
        try
        {
            action();
            Record($"{name}: ожидалось исключение {typeof(TException).Name}, его не было", false);
        }
        catch (TException)
        {
            Record(name, true);
        }
        catch (Exception ex)
        {
            Record($"{name}: ожидалось {typeof(TException).Name}, получено {ex.GetType().Name}: {ex.Message}", false);
        }
    }

    public static void NoThrow(string name, Action action)
    {
        try
        {
            action();
            Record(name, true);
        }
        catch (Exception ex)
        {
            Record($"{name}: неожиданное исключение {ex.GetType().Name}: {ex.Message}", false);
        }
    }

    /// <summary>Провал без сравнения — например, когда проверяемое условие описывается текстом.</summary>
    public static void Fail(string name) => Record(name, false);

    private static void Record(string message, bool ok)
    {
        var s = _current ??= new SuiteState { Name = "(без раздела)" };
        s.Checks++;
        _totalChecks++;
        if (!ok)
        {
            s.Failures.Add(message);
            _totalFailures++;
        }
        if (Verbose) Console.WriteLine($"   {(ok ? "ok  " : "ПРОВАЛ")} {message}");
    }

    private static string Format(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        string s => s,
        double d => d.ToString("0.####", CultureInfo.InvariantCulture),
        float f => f.ToString("0.####", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        IEnumerable seq => string.Join(" ", seq.Cast<object?>().Select(Format)),
        _ => value.ToString() ?? "null"
    };

    // --------------------------------------------------------------- Итоги

    /// <summary>Печатает итог и возвращает код выхода процесса.</summary>
    public static int Report(string title)
    {
        Flush();
        Console.WriteLine();
        Console.WriteLine(_totalFailures == 0
            ? $"{title}: {Plural(_totalChecks)} в {_suites} разделах — ВСЁ ПРОЙДЕНО"
            : $"{title}: {Plural(_totalChecks)} в {_suites} разделах — ПРОВАЛЕНО {_totalFailures}");
        return _totalFailures == 0 ? 0 : 1;
    }
}
