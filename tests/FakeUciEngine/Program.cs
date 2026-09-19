// Минимальный поддельный UCI-движок для проверки обёртки UciEngine.
// Команды читаются всегда, поиск идёт в отдельном потоке — как у настоящего движка.
//
// Кроме UCI понимает служебные команды тестов:
//   test-scenario <имя>  — что отвечать на следующую команду go
//                          (default, mate, none, multipv, silent)
//   test-crash           — немедленно завершить процесс, как упавший движок
using System;
using System.Threading;
using System.Threading.Tasks;

object outLock = new();
CancellationTokenSource? searchCts = null;
Task? searchTask = null;
var scenario = "default";

void Say(string text)
{
    lock (outLock) Console.WriteLine(text);
}

void Search(bool infinite, CancellationToken token)
{
    switch (scenario)
    {
        case "mate":
            Say("info depth 20 seldepth 26 multipv 1 score mate -3 lowerbound nodes 123456 nps 1000000 " +
                "time 123 hashfull 250 tbhits 7 pv d1h5 e8e7");
            break;

        case "none":
            // Позиция уже закончена: движок не даёт ни анализа, ни хода.
            break;

        case "multipv":
            // Строки приходят вперемешку — обёртка должна разложить их по номерам.
            Say("info depth 12 multipv 3 score cp -15 nodes 300 pv c2c4 e7e5");
            Say("info depth 12 multipv 1 score cp 45 nodes 100 pv e2e4 e7e5");
            Say("info depth 12 multipv 2 score cp 12 nodes 200 pv d2d4 d7d5");
            break;

        case "silent":
            // Только служебные строки без анализа — их обёртка игнорирует.
            Say("info string проверка связи");
            Say("info depth 5 currmove e2e4 currmovenumber 1");
            break;

        default:
            Say("info string начинаем поиск");
            Say("info depth 1 seldepth 1 multipv 1 score cp 24 nodes 20 nps 20000 time 1 pv e2e4 e7e5");
            Say("info depth 2 seldepth 3 multipv 1 score cp 31 upperbound nodes 200 nps 30000 time 7 pv e2e4 e7e5 g1f3");
            Say("info depth 3 seldepth 4 multipv 2 score mate 5 nodes 900 nps 40000 time 22 pv d2d4 d7d5");
            break;
    }

    if (infinite) token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));
    else Thread.Sleep(50);

    Say(scenario switch
    {
        "none" => "bestmove (none)",
        "mate" => "bestmove d1h5",
        "multipv" => "bestmove e2e4",
        _ => "bestmove e2e4 ponder e7e5"
    });
}

string? line;
while ((line = Console.ReadLine()) != null)
{
    if (line == "uci")
    {
        Say("id name FakeFish 1.2");
        Say("id author Tester");
        Say("option name Threads type spin default 1 min 1 max 512");
        Say("option name Hash type spin default 16 min 1 max 4096");
        Say("option name MultiPV type spin default 1 min 1 max 500");
        Say("option name Skill Level type spin default 20 min 0 max 20");
        Say("option name UCI_LimitStrength type check default false");
        Say("option name Style type combo default Normal var Normal var Wild Attack");
        Say("option name Clear Hash type button");
        Say("option name Debug Log File type string default");
        Say("uciok");
    }
    else if (line == "isready")
    {
        Say("readyok");
    }
    else if (line.StartsWith("test-scenario"))
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        scenario = parts.Length > 1 ? parts[1] : "default";
    }
    else if (line == "test-crash")
    {
        Environment.Exit(3);
    }
    else if (line.StartsWith("go"))
    {
        var infinite = line.Contains("infinite");
        searchCts?.Cancel();
        searchTask?.Wait();
        searchCts = new CancellationTokenSource();
        var token = searchCts.Token;
        searchTask = Task.Run(() => Search(infinite, token));
    }
    else if (line == "stop")
    {
        searchCts?.Cancel();
    }
    else if (line == "quit")
    {
        searchCts?.Cancel();
        break;
    }
}
