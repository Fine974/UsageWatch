using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace AppUsage;

internal static class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    private static Mutex? _single;

    [STAThread]
    private static int Main(string[] args)
    {
        var cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "";

        var needsLock = cmd is "" or "tray" or "--silent" or "run" or "serve";
        if (needsLock)
        {
            _single = new Mutex(true, "usagewatch_singleton", out var created);
            if (!created)
            {
                if (cmd is "" or "tray" or "--silent")
                {
                    OpenExisting();
                }
                else
                {
                    AttachConsoleToParent();
                    Console.WriteLine("已有实例在运行，本次不重复启动。");
                }
                return 0;
            }
        }

        if (cmd is "" or "tray" or "--silent")
            return RunTray(args);

        AttachConsoleToParent();

        var opts = new Opts(args.Skip(1).ToArray());
        return cmd switch
        {
            "run" => RunHeadless(opts),
            "serve" => RunServe(opts),
            "report" => Report(opts),
            "status" => Status(),
            "probe" => Probe(),
            "unknown" => Unknown(opts),
            _ => Help(),
        };
    }

    /// <summary>列出没被任何规则命中的页面，按累计时长排序 —— 用来知道该补什么规则。</summary>
    private static int Unknown(Opts opts)
    {
        Store.EnsureRoot();
        var top = Math.Max(5, opts.Int("top", 30));
        var acc = new Dictionary<string, double>(StringComparer.Ordinal);
        var procOf = new Dictionary<string, string>(StringComparer.Ordinal);
        var days = 0;

        foreach (var file in Directory.GetFiles(Store.Root, "????-??-??.jsonl").OrderBy(x => x))
        {
            if (!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                continue;

            var segs = Store.Load(day);
            if (segs.Count == 0) continue;
            days++;

            foreach (var s in segs)
            {
                if (s.Seconds <= 0) continue;
                var c = Classify.Of(s.Proc, s.Title);
                if (c.Drop) continue;
                if (c.Group != "其他" && c.Site != "其他网页" && c.Site != "unknown") continue;

                var key = Classify.Normalize(s.Title);
                if (key.Length == 0) key = "(无标题) " + s.Proc;
                acc[key] = acc.GetValueOrDefault(key) + s.Seconds;
                procOf.TryAdd(key, s.Proc);
            }
        }

        if (acc.Count == 0)
        {
            Console.WriteLine("没有未归类的记录。");
            return 0;
        }

        var total = acc.Values.Sum();
        Console.WriteLine($"扫描 {days} 天，未归类 {acc.Count} 项，合计 {Payload.Dur(total)}\n");
        Console.WriteLine($"{"累计",10}  {"进程",6}  页面");
        Console.WriteLine(new string('-', 78));

        foreach (var (title, sec) in acc.OrderByDescending(x => x.Value).Take(top))
        {
            var name = title.Length > 56 ? title[..56] + "…" : title;
            Console.WriteLine($"{Payload.Dur(sec),10}  {procOf[title],6}  {name}");
        }

        Console.WriteLine();
        Console.WriteLine($"补规则：编辑 {Path.Combine(Store.Root, "rules.json")}，格式见 rules.example.json");
        return 0;
    }

    private static void AttachConsoleToParent()
    {
        try
        {
            if (!AttachConsole(0xFFFFFFFF)) return;
            var so = Console.OpenStandardOutput();
            Console.SetOut(new StreamWriter(so, new UTF8Encoding(false)) { AutoFlush = true });
            var se = Console.OpenStandardError();
            Console.SetError(new StreamWriter(se, new UTF8Encoding(false)) { AutoFlush = true });
        }
        catch
        {
            // 双击启动时没有父控制台，属正常情况
        }
    }

    private static int RunTray(string[] args)
    {
        var opts = new Opts(args.Where(a => a.StartsWith("--")).ToArray());
        var silent = args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase));

        var cts = new CancellationTokenSource();
        var rec = new Recorder(opts.Int("interval", 5), opts.Int("idle", 600), !opts.Has("no-title"));
        rec.Start(cts.Token);

        _ = Task.Run(async () =>
        {
            try { await WebHost.RunAsync(5279, cts.Token); }
            catch (OperationCanceledException) { }
            catch { }
        });

        WebHost.WhenReady.Wait(4000);

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var tray = new TrayApp(rec, cts, WebHost.Port);
        tray.Announce();

        if (!silent)
        {
            _ = Task.Delay(700).ContinueWith(_ =>
            {
                try { Process.Start(new ProcessStartInfo(WebHost.Url) { UseShellExecute = true }); } catch { }
            });
        }

        Application.Run();
        return 0;
    }

    private static void OpenExisting()
    {
        try
        {
            var port = 5279;
            var f = Path.Combine(Store.Root, "port.txt");
            if (File.Exists(f) && int.TryParse(File.ReadAllText(f).Trim(), out var p)) port = p;
            Process.Start(new ProcessStartInfo($"http://127.0.0.1:{port}") { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private static int RunHeadless(Opts opts)
    {
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var interval = Math.Max(1, opts.Int("interval", 5));
        var idle = Math.Max(30, opts.Int("idle", 600));
        var rec = new Recorder(interval, idle, !opts.Has("no-title"));
        rec.Start(cts.Token);

        Console.WriteLine($"采集已启动 | 间隔 {interval}s | 空闲阈值 {idle}s");

        if (!opts.Has("no-web"))
        {
            _ = Task.Run(async () =>
            {
                try { await WebHost.RunAsync(5279, cts.Token); }
                catch (OperationCanceledException) { }
                catch { }
            });
            WebHost.WhenReady.Wait(4000);
            Console.WriteLine($"监控页: {WebHost.Url}");
        }

        Console.WriteLine("按 Ctrl+C 停止");
        cts.Token.WaitHandle.WaitOne();
        Thread.Sleep(300);
        return 0;
    }

    private static int RunServe(Opts opts)
    {
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        _ = Task.Run(async () =>
        {
            try { await WebHost.RunAsync(5279, cts.Token); }
            catch (OperationCanceledException) { }
        });

        WebHost.WhenReady.Wait(4000);
        Console.WriteLine($"服务已启动: {WebHost.Url}");
        Console.WriteLine("按 Ctrl+C 停止");
        cts.Token.WaitHandle.WaitOne();
        return 0;
    }

    private static int Help()
    {
        Console.WriteLine("""
            usagewatch - 前台应用使用采集与监控

            双击运行        托盘模式：后台采集 + 自动打开监控页
            --silent        托盘模式，不自动打开页面（开机自启用）

            命令行:
              usagewatch run              前台运行，内置 Web 服务
              usagewatch run --no-web     只采集不启服务
              usagewatch serve            只启 Web 服务（不采集）
              usagewatch report           终端汇总
              usagewatch report --html    导出静态看板并打开
              usagewatch report --date 2026-11-12
              usagewatch status           查看采集状态
              usagewatch probe            采样诊断
              usagewatch unknown          列出没归类的页面（用来补规则）

            参数:
              --idle 600       空闲超过 600 秒不计时（默认）
              --interval 5     采样间隔秒数（默认 5）
              --no-title       不记录窗口标题

            数据目录: %APPDATA%\AppUsage
            """);
        return 0;
    }

    private static int Status()
    {
        var self = Environment.ProcessId;
        var running = Process.GetProcessesByName("usagewatch").Any(p => p.Id != self);
        Console.WriteLine(running ? "采集状态: 运行中" : "采集状态: 未运行");

        var st = Store.LoadState();
        if (st is not null)
        {
            var idle = (DateTime.Now - st.Value.Beat).TotalMinutes;
            if (!running && idle > 5)
                Console.WriteLine($"上次采集于 {st.Value.Beat:HH:mm:ss} 中断，下次启动会自动补偿这段");
            else
                Console.WriteLine($"当前前台: {Store.FriendlyName(st.Value.Proc)}  自 {st.Value.Start:HH:mm:ss}  心跳 {st.Value.Beat:HH:mm:ss}");
        }

        var segs = Store.Load(DateTime.Today);
        Console.WriteLine($"今日记录: {segs.Count} 段，合计 {segs.Sum(s => s.Seconds) / 3600:F2}h");
        Console.WriteLine($"数据目录: {Store.Root}");

        var portFile = Path.Combine(Store.Root, "port.txt");
        if (File.Exists(portFile))
            Console.WriteLine($"监控页: http://127.0.0.1:{File.ReadAllText(portFile).Trim()}");
        return 0;
    }

    private static int Probe()
    {
        for (var i = 1; i <= 3; i++)
        {
            var s = Collector.Take();
            Console.WriteLine($"[{i}] 进程={s.Proc,-22} 空闲={s.IdleSeconds,6}s  有窗口={s.HasWindow,-5} 锁屏={s.IsLockScreen,-5} 类名={s.ClassName}");
            Console.WriteLine($"    标题={Trunc(s.Title, 70)}");
            if (i < 3) Thread.Sleep(2000);
        }
        return 0;
    }

    private static int Report(Opts opts)
    {
        var day = opts.Get("date") is { } d && DateTime.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : DateTime.Today;

        var segs = Store.Load(day);
        if (segs.Count == 0)
        {
            Console.WriteLine($"{day:yyyy-MM-dd} 没有记录。");
            Console.WriteLine($"数据目录: {Store.Root}");
            return 0;
        }

        var payload = Payload.Build(day, segs);

        if (opts.Has("html"))
        {
            var arg = opts.Get("html");
            var outPath = arg is { Length: > 0 } && arg != "true"
                ? arg
                : Path.Combine(Store.Root, $"report-{day:yyyy-MM-dd}.html");

            var dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(outPath, Dashboard.BuildHtml(day, payload), new UTF8Encoding(false));
            Console.WriteLine($"看板已生成: {outPath}");

            if (!opts.Has("no-open"))
            {
                try { Process.Start(new ProcessStartInfo(outPath) { UseShellExecute = true }); } catch { }
            }
            return 0;
        }

        var first = TimeSpan.FromSeconds(payload.firstSec);
        var last = TimeSpan.FromSeconds(payload.lastSec);

        Console.WriteLine();
        Console.WriteLine($"日期      {payload.date} {payload.weekday}");
        Console.WriteLine($"活跃跨度  {first.Hours:D2}:{first.Minutes:D2} - {last.Hours:D2}:{last.Minutes:D2}");
        Console.WriteLine($"有效记录  {Payload.Dur(payload.totalSec)}  (覆盖 {payload.coverage:F0}%)");
        Console.WriteLine();

        const int wName = 24, wDur = 11, wPct = 8, wSw = 6;
        Console.WriteLine(Pad("应用", wName) + Pad("时长", wDur) + Pad("占比", wPct) + Pad("次数", wSw));
        Console.WriteLine(new string('-', wName + wDur + wPct + wSw));

        foreach (var r in payload.rows.Take(20))
            Console.WriteLine(Pad(r.l, wName) + Pad(Payload.Dur(r.sec), wDur) + Pad($"{r.pct:F1}%", wPct) + Pad(r.sw.ToString(), wSw));

        Console.WriteLine(new string('-', wName + wDur + wPct + wSw));
        Console.WriteLine(Pad("合计", wName) + Pad(Payload.Dur(payload.totalSec), wDur) + Pad("100.0%", wPct) + Pad(payload.switches.ToString(), wSw));
        Console.WriteLine();
        return 0;
    }

    private static int DisplayWidth(string s)
    {
        var w = 0;
        foreach (var c in s) w += c > 0x2E80 ? 2 : 1;
        return w;
    }

    private static string Pad(string s, int width)
        => s + new string(' ', Math.Max(0, width - DisplayWidth(s)));

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private sealed class Opts
    {
        private readonly Dictionary<string, string> _d;

        public Opts(string[] args)
        {
            _d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (!a.StartsWith("--")) continue;
                var key = a[2..];
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) _d[key] = args[++i];
                else _d[key] = "true";
            }
        }

        public string? Get(string k) => _d.TryGetValue(k, out var v) ? v : null;
        public bool Has(string k) => _d.ContainsKey(k);
        public int Int(string k, int def) => _d.TryGetValue(k, out var v) && int.TryParse(v, out var n) ? n : def;
    }
}
