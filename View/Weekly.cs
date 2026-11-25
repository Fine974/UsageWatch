using System.Globalization;

namespace AppUsage;

internal sealed class WeekDayInfo
{
    public string d { get; set; } = "";
    public string weekday { get; set; } = "";
    public bool empty { get; set; }
    public double sec { get; set; }
    public double coverage { get; set; }
    public double focusSec { get; set; }
    public double medianStay { get; set; }
    public int deepCount { get; set; }
    public int switches { get; set; }
    public string top { get; set; } = "";
    public double topPct { get; set; }
    public string topc { get; set; } = "";
}

internal sealed class WeekGroupInfo
{
    public string n { get; set; } = "";
    public string c { get; set; } = "";
    public double sec { get; set; }
    public double pct { get; set; }
    public double pPct { get; set; }
    public bool hasPrev { get; set; }
}

internal sealed class WeekSiteInfo
{
    public string n { get; set; } = "";
    public string g { get; set; } = "";
    public string c { get; set; } = "";
    public double sec { get; set; }
    public double pct { get; set; }
}

internal sealed class WeekInfo
{
    public string start { get; set; } = "";
    public string end { get; set; } = "";
    public string label { get; set; } = "";
    public int recorded { get; set; }
    public double totalSec { get; set; }
    public double avgSec { get; set; }
    public double medStay { get; set; }
    public int deepCount { get; set; }
    public double deepSec { get; set; }
    public double prevTotalSec { get; set; }
    public double prevAvgSec { get; set; }
    public int prevRecorded { get; set; }
    public double deltaPct { get; set; }
    public List<WeekDayInfo> days { get; set; } = new();
    public List<WeekGroupInfo> groups { get; set; } = new();
    public List<WeekSiteInfo> sites { get; set; } = new();
    public List<string> notes { get; set; } = new();
}

/// <summary>
/// 周视图。周一为一周起点（国内习惯），任何一天都落到它所在的那一周。
/// 逐日汇总直接复用 Payload.Build，所以单日页和周页的「最长专注」「深度块」口径一定一致 ——
/// 两边各算一套是这类统计最容易出的错。
/// </summary>
internal static class Weekly
{
    /// <summary>
    /// 低于 3 小时的日子参与合计但不参与日均。日均是平均数，最怕半天数据：
    /// 一个下午才开工的 2 小时日子就能把整周日均拉低一小时以上。中位数不需要这个门槛。
    /// </summary>
    private const int MinDaySec = 10800;

    private static readonly string[] WeekNames = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };

    private static WeekInfo? _cache;
    private static DateTime _cacheKey;
    private static DateTime _cacheAt;
    private static readonly object Gate = new();

    internal static DateTime MondayOf(DateTime any) => any.Date.AddDays(-(((int)any.DayOfWeek + 6) % 7));

    internal static WeekInfo Get(DateTime any, Func<DateTime, List<Segment>> loader)
    {
        var monday = MondayOf(any);
        lock (Gate)
        {
            if (_cache is not null && _cacheKey == monday &&
                (DateTime.Now - _cacheAt).TotalSeconds < 30)
                return _cache;
        }

        var info = Build(monday, loader);

        lock (Gate)
        {
            _cache = info;
            _cacheKey = monday;
            _cacheAt = DateTime.Now;
        }
        return info;
    }

    private sealed class Agg
    {
        public List<WeekDayInfo> Days = new();
        public Dictionary<string, double> GroupSec = new(StringComparer.Ordinal);
        public Dictionary<string, double> SiteSec = new(StringComparer.Ordinal);
        public Dictionary<string, string> SiteGroup = new(StringComparer.Ordinal);
        public List<double> Stay = new();
        public double Total;
        public double FullDayTotal;
        public int Recorded;
        public int DeepCount;
        public double DeepSec;
    }

    private static WeekInfo Build(DateTime monday, Func<DateTime, List<Segment>> loader)
    {
        var cur = Aggregate(monday, loader);
        var prev = Aggregate(monday.AddDays(-7), loader);

        var info = new WeekInfo
        {
            start = monday.ToString("yyyy-MM-dd"),
            end = monday.AddDays(6).ToString("yyyy-MM-dd"),
            label = $"{monday:MM/dd}–{monday.AddDays(6):MM/dd}",
            days = cur.Days,
            recorded = cur.Recorded,
            totalSec = Math.Round(cur.Total, 0),
            avgSec = Math.Round(cur.Recorded > 0 ? cur.FullDayTotal / cur.Recorded : 0, 0),
            medStay = Math.Round(Baseline.Median(cur.Stay), 0),
            deepCount = cur.DeepCount,
            deepSec = Math.Round(cur.DeepSec, 0),
            prevTotalSec = Math.Round(prev.Total, 0),
            prevAvgSec = Math.Round(prev.Recorded > 0 ? prev.FullDayTotal / prev.Recorded : 0, 0),
            prevRecorded = prev.Recorded,
            deltaPct = prev.Recorded > 0 && prev.FullDayTotal > 0
                ? Math.Round((cur.FullDayTotal / cur.Recorded - prev.FullDayTotal / prev.Recorded)
                             / (prev.FullDayTotal / prev.Recorded) * 100, 1)
                : 0,
        };

        var curTotal = Math.Max(1, cur.Total);
        var prevTotal = Math.Max(1, prev.Total);

        // 分类：本周 + 上周占比并列，直接能看出结构是在变还是没变
        var names = cur.GroupSec.Keys.Union(prev.GroupSec.Keys)
            .OrderByDescending(n => cur.GroupSec.TryGetValue(n, out var v) ? v : 0)
            .ToList();

        foreach (var n in names)
        {
            var sec = cur.GroupSec.TryGetValue(n, out var a) ? a : 0;
            if (sec < 60 && !prev.GroupSec.ContainsKey(n)) continue;
            info.groups.Add(new WeekGroupInfo
            {
                n = n,
                c = Rules.ColorOf(n),
                sec = Math.Round(sec, 0),
                pct = Math.Round(sec / curTotal * 100, 1),
                pPct = prev.GroupSec.TryGetValue(n, out var b) ? Math.Round(b / prevTotal * 100, 1) : 0,
                hasPrev = prev.Total > 0 && prev.GroupSec.ContainsKey(n),
            });
        }

        // 站点：本周 Top10
        foreach (var kv in cur.SiteSec.OrderByDescending(x => x.Value).Take(10))
        {
            var g = cur.SiteGroup.TryGetValue(kv.Key, out var gg) ? gg : "其他";
            info.sites.Add(new WeekSiteInfo
            {
                n = kv.Key,
                g = g,
                c = Rules.ColorOf(g),
                sec = Math.Round(kv.Value, 0),
                pct = Math.Round(kv.Value / curTotal * 100, 1),
            });
        }

        info.notes = BuildNotes(info, cur, prev);
        return info;
    }

    private static Agg Aggregate(DateTime monday, Func<DateTime, List<Segment>> loader)
    {
        var agg = new Agg();

        for (var i = 0; i < 7; i++)
        {
            var day = monday.AddDays(i);
            if (day.Date > DateTime.Today)
            {
                // 还没到的日子也占一格，周图才能对齐到星期几，不会因为今天是周二就只剩两根柱
                agg.Days.Add(new WeekDayInfo
                {
                    d = day.ToString("yyyy-MM-dd"),
                    weekday = WeekNames[i],
                    empty = true,
                });
                continue;
            }

            var p = Payload.Build(day, loader(day));
            var d = new WeekDayInfo
            {
                d = day.ToString("yyyy-MM-dd"),
                weekday = WeekNames[i],
                empty = p.empty,
                sec = p.totalSec,
                coverage = p.coverage,
                focusSec = p.empty ? 0 : p.focus.d,
                medianStay = p.medianStay,
                deepCount = p.deepCount,
                switches = p.switches,
            };

            if (!p.empty && p.groups.Count > 0)
            {
                d.top = p.groups[0].n;
                d.topPct = p.groups[0].pct;
                d.topc = p.groups[0].c;
            }
            agg.Days.Add(d);

            if (p.empty || p.totalSec <= 0) continue;

            agg.Total += p.totalSec;
            if (p.totalSec >= MinDaySec)
            {
                agg.Recorded++;
                agg.FullDayTotal += p.totalSec;
            }

            if (p.medianStay > 0) agg.Stay.Add(p.medianStay);
            agg.DeepCount += p.deepCount;
            agg.DeepSec += p.deepSec;

            foreach (var g in p.groups)
                agg.GroupSec[g.n] = agg.GroupSec.TryGetValue(g.n, out var v) ? v + g.sec : g.sec;

            foreach (var s in p.sites)
            {
                agg.SiteSec[s.n] = agg.SiteSec.TryGetValue(s.n, out var v) ? v + s.sec : s.sec;
                if (!agg.SiteGroup.ContainsKey(s.n)) agg.SiteGroup[s.n] = s.g;
            }
        }

        return agg;
    }

    private static List<string> BuildNotes(WeekInfo w, Agg cur, Agg prev)
    {
        var notes = new List<string>();

        var full = w.days.Where(x => !x.empty && x.sec >= MinDaySec).ToList();
        if (full.Count > 0)
        {
            var hi = full.MaxBy(x => x.sec)!;
            var lo = full.MinBy(x => x.sec)!;
            notes.Add(hi.d == lo.d
                ? $"只有 {hi.weekday} 是完整工作日：{hi.weekday} {Dur(hi.sec)}。"
                : $"最长的一天是 {hi.weekday} {Dur(hi.sec)}，最短的是 {lo.weekday} {Dur(lo.sec)}，相差 {Dur(hi.sec - lo.sec)}。");
        }

        if (w.groups.Count > 0)
        {
            var g = w.groups[0];
            var cmp = g.hasPrev ? $"，上周 {g.pPct:F0}%" : "";
            notes.Add($"占比最高的是 {g.n} {g.pct:F0}%（{Dur(g.sec)}）{cmp}。");
        }

        if (w.deepCount > 0)
            notes.Add($"本周共 {w.deepCount} 段 25 分钟以上的深度块，合计 {Dur(w.deepSec)}"
                    + $"（{(w.recorded > 0 ? Dur(w.deepSec / w.recorded) : "-")}/工作日）。");

        if (w.prevRecorded > 0 && Math.Abs(w.deltaPct) >= 5)
            notes.Add($"日均 {Dur(w.avgSec)}，比上周的 {Dur(w.prevAvgSec)} {(w.deltaPct > 0 ? "多" : "少")} {Math.Abs(w.deltaPct):F0}%。");

        var un = w.groups.FirstOrDefault(x => x.n == "未归类");
        if (un is not null && un.sec >= 600)
            notes.Add($"{Dur(un.sec)} 落在未归类页面，占 {un.pct:F0}%，规则还有缺口。");

        return notes;
    }

    private static string Dur(double sec)
    {
        var totalMin = (int)Math.Round(sec / 60.0);
        if (totalMin < 1) return $"{Math.Max(1, (int)Math.Round(sec))}s";
        return totalMin >= 60 ? $"{totalMin / 60}h {totalMin % 60:D2}m" : $"{totalMin}m";
    }
}
