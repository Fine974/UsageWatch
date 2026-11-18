using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AppUsage;

internal sealed class Segment
{
    public string Proc { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public double Seconds => Math.Max(0, (End - Start).TotalSeconds);
}

internal sealed record UsageRow(string Proc, string Label, double Seconds, int Switches);

internal static class Store
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    internal static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AppUsage");

    internal static string FileFor(DateTime d) => Path.Combine(Root, $"{d:yyyy-MM-dd}.jsonl");

    internal static string StateFile => Path.Combine(Root, "state.json");

    internal static void EnsureRoot() => Directory.CreateDirectory(Root);

    internal static void AppendSegment(Segment s)
    {
        var sec = Math.Round(s.Seconds, 1);
        if (sec < 1) return;

        EnsureRoot();
        var line = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["t"] = s.Start.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            ["e"] = s.End.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            ["p"] = s.Proc,
            ["w"] = Truncate(s.Title, 120),
            ["s"] = sec,
        });
        File.AppendAllText(FileFor(s.Start), line + "\n", Utf8NoBom);
    }

    internal static List<Segment> Load(DateTime day)
    {
        var path = FileFor(day);
        var list = new List<Segment>();
        if (!File.Exists(path)) return list;

        foreach (var line in File.ReadLines(path, Utf8NoBom))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var r = doc.RootElement;
                var proc = r.GetProperty("p").GetString() ?? "";
                var title = r.GetProperty("w").GetString() ?? "";

                // 历史数据里可能残留锁屏段（早期版本漏判 LockApp），读取时统一剔除
                if (Collector.IsLockScreenProc(proc, title, "")) continue;

                list.Add(new Segment
                {
                    Start = DateTime.Parse(r.GetProperty("t").GetString()!, CultureInfo.InvariantCulture),
                    End = DateTime.Parse(r.GetProperty("e").GetString()!, CultureInfo.InvariantCulture),
                    Proc = proc,
                    Title = title,
                });
            }
            catch
            {
                // 跳过损坏行，采集中断时可能留下半行
            }
        }
        return list;
    }

    internal static List<string> AvailableDates()
    {
        try
        {
            if (!Directory.Exists(Root)) return new List<string>();
            return Directory.GetFiles(Root, "*.jsonl")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .Where(n => n.Length == 10)
                .OrderByDescending(x => x)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    internal static List<UsageRow> Aggregate(IEnumerable<Segment> segs)
    {
        var rows = new List<UsageRow>();
        foreach (var g in segs.Where(x => x.Proc.Length > 0).GroupBy(x => x.Proc))
        {
            var total = g.Sum(x => x.Seconds);
            var switches = g.Count();
            rows.Add(new UsageRow(g.Key, FriendlyName(g.Key), total, switches));
        }
        return rows.OrderByDescending(x => x.Seconds).ToList();
    }

    internal static void SaveState(string proc, DateTime start, DateTime beat)
    {
        EnsureRoot();
        try
        {
            var json = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["p"] = proc,
                ["t"] = start.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                ["b"] = beat.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            });
            var tmp = StateFile + ".tmp";
            File.WriteAllText(tmp, json, Utf8NoBom);
            File.Move(tmp, StateFile, true);
        }
        catch
        {
            // 心跳失败不影响主流程
        }
    }

    internal static void ClearState()
    {
        try
        {
            if (File.Exists(StateFile)) File.Delete(StateFile);
        }
        catch
        {
        }
    }

    internal static (string Proc, DateTime Start, DateTime Beat)? LoadState()
    {
        try
        {
            if (!File.Exists(StateFile)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(StateFile, Utf8NoBom));
            var r = doc.RootElement;
            var p = r.GetProperty("p").GetString() ?? "";
            if (p.Length == 0) return null;
            return (p,
                DateTime.Parse(r.GetProperty("t").GetString()!, CultureInfo.InvariantCulture),
                DateTime.Parse(r.GetProperty("b").GetString()!, CultureInfo.InvariantCulture));
        }
        catch
        {
            return null;
        }
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["idea64"] = "IntelliJ IDEA",
        ["devenv"] = "Visual Studio",
        ["Code"] = "VS Code",
        ["chrome"] = "Chrome",
        ["msedge"] = "Edge",
        ["firefox"] = "Firefox",
        ["WindowsTerminal"] = "Windows Terminal",
        ["conhost"] = "Console Host",
        ["powershell"] = "PowerShell",
        ["pwsh"] = "PowerShell 7",
        ["cmd"] = "CMD",
        ["explorer"] = "资源管理器",
        ["WeChat"] = "微信",
        ["Weixin"] = "微信",
        ["DingTalk"] = "钉钉",
        ["WXWork"] = "企业微信",
        ["wps"] = "WPS Office",
        ["et"] = "WPS 表格",
        ["wpp"] = "WPS 演示",
        ["WINWORD"] = "Word",
        ["EXCEL"] = "Excel",
        ["POWERPNT"] = "PowerPoint",
        ["OUTLOOK"] = "Outlook",
        ["navicat"] = "Navicat",
        ["DataGrip64"] = "DataGrip",
        ["java"] = "Java 进程",
        ["javaw"] = "Java 进程",
        ["mstsc"] = "远程桌面",
        ["Taskmgr"] = "任务管理器",
        ["SnippingTool"] = "截图工具",
        ["Notepads"] = "记事本",
        ["notepad++"] = "Notepad++",
        ["typora"] = "Typora",
        ["Obsidian"] = "Obsidian",
    };

    internal static string FriendlyName(string proc)
        => Known.TryGetValue(proc, out var v) ? v : proc;
}
