namespace AppUsage;

internal sealed class DayPayload
{
    public string date { get; set; } = "";
    public string weekday { get; set; } = "";
    public bool empty { get; set; }
    public double spanStartSec { get; set; }
    public double spanEndSec { get; set; }
    public double firstSec { get; set; }
    public double lastSec { get; set; }
    public double totalSec { get; set; }
    public double coverage { get; set; }
    public int switches { get; set; }
    public double avgStay { get; set; }
    public FocusInfo focus { get; set; } = new();
    public List<RowInfo> rows { get; set; } = new();
    public List<GroupInfo> groups { get; set; } = new();
    public List<SiteInfo> sites { get; set; } = new();
    public List<SegInfo> segs { get; set; } = new();
    public double[] hourly { get; set; } = new double[24];
    public List<StackInfo> hourlyStack { get; set; } = new();
    public List<string> insights { get; set; } = new();
}

internal sealed class FocusInfo
{
    public string l { get; set; } = "";
    public double s { get; set; }
    public double d { get; set; }
}

internal sealed class RowInfo
{
    public string l { get; set; } = "";
    public string p { get; set; } = "";
    public double sec { get; set; }
    public double pct { get; set; }
    public int sw { get; set; }
    public string c { get; set; } = "";
}

/// <summary>分类层：编码 / 沟通 / 业务系统 / 资讯社区 …</summary>
internal sealed class GroupInfo
{
    public string n { get; set; } = "";
    public double sec { get; set; }
    public double pct { get; set; }
    public string c { get; set; } = "";
}

/// <summary>页面层：站点下的具体页面或文件。</summary>
internal sealed class PageInfo
{
    public string n { get; set; } = "";
    public double sec { get; set; }
    public double pct { get; set; }
}

/// <summary>站点层：浏览器按站点归并，IDE 按项目归并。</summary>
internal sealed class SiteInfo
{
    public string n { get; set; } = "";
    public string g { get; set; } = "";
    public double sec { get; set; }
    public double pct { get; set; }
    public string c { get; set; } = "";
    public List<PageInfo> pages { get; set; } = new();
}

internal sealed class SegInfo
{
    public string l { get; set; } = "";
    public string c { get; set; } = "";
    public string g { get; set; } = "";
    public double s { get; set; }
    public double d { get; set; }
    public string w { get; set; } = "";
}

internal sealed class StackInfo
{
    public string l { get; set; } = "";
    public string c { get; set; } = "";
    public double[] v { get; set; } = new double[24];
}

internal static class Payload
{
    internal static readonly string[] Palette =
    {
        "#4f9cf9", "#5ac8a8", "#f2a65a", "#d96ba0", "#9b7ede",
        "#e5674e", "#4fc3d9", "#c9b458", "#7b8fd4", "#6fbf73",
        "#e0a0d0", "#8ecae6",
    };

    private static readonly string[] WeekNames = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };

    /// <summary>一段被裁剪、归类后的记录。</summary>
    internal readonly record struct Work(
        string Proc, string Title, string App, string Site, string Page, string Group,
        DateTime Start, DateTime End)
    {
        public double Sec => (End - Start).TotalSeconds;
    }

    /// <summary>
    /// 把原始段落归类、裁剪成干净的时间轴。基线统计也走这里，保证和单日页口径一致。
    /// </summary>
    internal static List<Work> Prepare(DateTime day, List<Segment> segs)
    {
        var day0 = day.Date;
        var day1 = day0.AddDays(1);

        var raw = segs
            .Select(s =>
            {
                var st = s.Start < day0 ? day0 : s.Start;
                var en = s.End > day1 ? day1 : s.End;
                if (en <= st) return (Ok: false, W: default(Work));

                // 归类在这里做：锁屏、空白页直接丢弃，不占任何时长
                var c = Classify.Of(s.Proc, s.Title);
                if (c.Drop) return (Ok: false, W: default(Work));

                return (Ok: true, W: new Work(
                    s.Proc, s.Title, c.App, c.Site, c.Page, c.Group, st, en));
            })
            .Where(x => x.Ok)
            .Select(x => x.W)
            .OrderBy(x => x.Start)
            .ToList();

        // 中断补偿可能写出与前段重叠的记录，沿时间轴裁剪，避免重复计时
        var clipped = new List<Work>();
        var cursor = DateTime.MinValue;
        foreach (var x in raw)
        {
            var st = x.Start < cursor ? cursor : x.Start;
            if (x.End <= st) continue;
            clipped.Add(x with { Start = st });
            cursor = x.End;
        }
        return clipped;
    }

    internal static DayPayload Build(DateTime day, List<Segment> segs)
    {
        var day0 = day.Date;
        var day1 = day0.AddDays(1);

        var result = new DayPayload
        {
            date = day.ToString("yyyy-MM-dd"),
            weekday = WeekNames[(int)day.DayOfWeek],
        };

        var clipped = Prepare(day, segs);

        if (clipped.Count == 0)
        {
            result.empty = true;
            return result;
        }

        var totalSec = clipped.Sum(x => x.Sec);
        var spanStart = clipped.Min(x => x.Start);
        var spanEnd = clipped.Max(x => x.End);

        var t0 = spanStart.Date.AddHours(spanStart.Hour);
        var t1 = spanEnd.Date.AddHours(spanEnd.Hour + 1);

        // ---------- 应用层 ----------
        var colorOfApp = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var byApp = clipped
            .GroupBy(x => x.App)
            .Select(g => new { App = g.Key, Sec = g.Sum(x => x.Sec), Sw = g.Count() })
            .OrderByDescending(x => x.Sec)
            .ToList();

        for (var i = 0; i < byApp.Count; i++)
            colorOfApp[byApp[i].App] = Palette[i % Palette.Length];

        foreach (var g in byApp)
        {
            result.rows.Add(new RowInfo
            {
                l = g.App,
                p = g.App,
                sec = Math.Round(g.Sec, 1),
                pct = totalSec > 0 ? Math.Round(g.Sec / totalSec * 100, 1) : 0,
                sw = g.Sw,
                c = colorOfApp[g.App],
            });
        }

        // ---------- 分类层 ----------
        var byGroup = clipped
            .GroupBy(x => x.Group)
            .Select(g => new { Group = g.Key, Sec = g.Sum(x => x.Sec) })
            .OrderByDescending(x => x.Sec)
            .ToList();

        foreach (var g in byGroup)
        {
            result.groups.Add(new GroupInfo
            {
                n = g.Group,
                sec = Math.Round(g.Sec, 1),
                pct = totalSec > 0 ? Math.Round(g.Sec / totalSec * 100, 1) : 0,
                c = Rules.ColorOf(g.Group),
            });
        }

        // ---------- 站点层 + 页面层 ----------
        var bySite = clipped
            .GroupBy(x => x.Site)
            .Select(g => new
            {
                Site = g.Key,
                Group = g.First().Group,
                Sec = g.Sum(x => x.Sec),
                Pages = g.GroupBy(x => x.Page)
                    .Select(p => new { Page = p.Key, Sec = p.Sum(y => y.Sec) })
                    .OrderByDescending(p => p.Sec)
                    .ToList(),
            })
            .OrderByDescending(x => x.Sec)
            .ToList();

        foreach (var s in bySite)
        {
            var siteSec = s.Sec;
            result.sites.Add(new SiteInfo
            {
                n = s.Site,
                g = s.Group,
                sec = Math.Round(s.Sec, 1),
                pct = totalSec > 0 ? Math.Round(s.Sec / totalSec * 100, 1) : 0,
                c = Rules.ColorOf(s.Group),
                pages = s.Pages.Take(12).Select(p => new PageInfo
                {
                    n = p.Page,
                    sec = Math.Round(p.Sec, 1),
                    pct = siteSec > 0 ? Math.Round(p.Sec / siteSec * 100, 1) : 0,
                }).ToList(),
            });
        }

        // ---------- 甘特图：按分类着色，标签用站点名 ----------
        foreach (var x in clipped)
        {
            result.segs.Add(new SegInfo
            {
                l = x.Site,
                c = Rules.ColorOf(x.Group),
                g = x.Group,
                s = Math.Round((x.Start - day0).TotalSeconds, 1),
                d = Math.Round(x.Sec, 1),
                w = x.Title,
            });
        }

        // ---------- 每小时：按分类堆叠，能看出这个小时在做什么类型的事 ----------
        var hourly = new double[24];
        var hourByGroup = new Dictionary<string, double[]>(StringComparer.Ordinal);
        foreach (var c in clipped)
        {
            if (!hourByGroup.TryGetValue(c.Group, out var arr))
            {
                arr = new double[24];
                hourByGroup[c.Group] = arr;
            }

            var t = c.Start;
            while (t < c.End)
            {
                var next = t.Date.AddHours(t.Hour + 1);
                var stop = c.End < next ? c.End : next;
                var span = (stop - t).TotalSeconds;
                hourly[t.Hour] += span;
                arr[t.Hour] += span;
                t = stop;
            }
        }
        result.hourly = hourly.Select(x => Math.Round(x, 1)).ToArray();

        foreach (var g in result.groups.Take(8))
        {
            result.hourlyStack.Add(new StackInfo
            {
                l = g.n,
                c = g.c,
                v = hourByGroup.TryGetValue(g.n, out var a)
                    ? a.Select(x => Math.Round(x, 1)).ToArray()
                    : new double[24],
            });
        }

        var (focusLabel, focusStart, focusDur) = LongestFocus(clipped);
        result.focus = new FocusInfo
        {
            l = focusLabel,
            s = Math.Round((focusStart - day0).TotalSeconds, 1),
            d = Math.Round(focusDur, 1),
        };

        result.spanStartSec = Math.Round((t0 - day0).TotalSeconds, 1);
        result.spanEndSec = Math.Round((t1 - day0).TotalSeconds, 1);
        result.firstSec = Math.Round((spanStart - day0).TotalSeconds, 1);
        result.lastSec = Math.Round((spanEnd - day0).TotalSeconds, 1);
        result.totalSec = Math.Round(totalSec, 1);
        result.coverage = Math.Min(100, Math.Round(totalSec / Math.Max(1, (spanEnd - spanStart).TotalSeconds) * 100, 1));
        result.switches = Math.Max(0, clipped.Count - 1);
        result.avgStay = Math.Round(totalSec / Math.Max(1, clipped.Count), 1);

        result.insights = BuildInsights(result, clipped, totalSec);

        return result;
    }

    private static List<string> BuildInsights(DayPayload p, List<Work> clipped, double totalSec)
    {
        var insights = new List<string>();

        // 最值得看的一条：时间去哪儿了（按分类）。分类数量不定，不能写死下标
        if (p.groups.Count > 1)
        {
            var head = $"{p.groups[0].n} {p.groups[0].pct:F0}%（{Dur(p.groups[0].sec)}）";
            var rest = p.groups.Skip(1).Take(2).Select(g => $"{g.n} {g.pct:F0}%").ToList();
            insights.Add($"时间主要落在 {head}，其次是 {string.Join("、", rest)}。");
        }

        if (p.sites.Count > 1)
            insights.Add($"待得最久的去处是 {p.sites[0].n}（{Dur(p.sites[0].sec)}，{p.sites[0].pct:F0}%），"
                       + $"第二是 {p.sites[1].n} {Dur(p.sites[1].sec)}。");

        // 浏览器专项：把「Edge 占了一整天」拆开，直接回答「浏览器里到底在干什么」
        var browserProcs = new[] { "msedge", "chrome", "firefox" };
        var browser = clipped.Where(x => browserProcs.Contains(x.Proc, StringComparer.OrdinalIgnoreCase)).ToList();
        if (browser.Count > 0 && totalSec > 0)
        {
            var bSec = browser.Sum(x => x.Sec);
            var parts = browser
                .GroupBy(x => x.Group)
                .Select(g => new { G = g.Key, Sec = g.Sum(y => y.Sec) })
                .OrderByDescending(x => x.Sec)
                .Take(3)
                .Select(x => $"{x.G} {Dur(x.Sec)}")
                .ToList();
            var topSite = browser.GroupBy(x => x.Site)
                .Select(g => new { S = g.Key, Sec = g.Sum(y => y.Sec) })
                .OrderByDescending(x => x.Sec)
                .First();
            insights.Add($"浏览器共 {Dur(bSec)}（占全天 {bSec / totalSec * 100:F0}%），"
                       + $"主要用在 {string.Join("、", parts)}；单站点最多是 {topSite.S} {Dur(topSite.Sec)}。");
        }

        if (p.focus.d > 0)
        {
            var fs = TimeSpan.FromSeconds(p.focus.s);
            var fe = TimeSpan.FromSeconds(p.focus.s + p.focus.d);
            insights.Add($"最长连续专注 {Dur(p.focus.d)}，出现在 {fs.Hours:D2}:{fs.Minutes:D2}–{fe.Hours:D2}:{fe.Minutes:D2}（{p.focus.l}）。");
        }

        var avg = totalSec / Math.Max(1, clipped.Count);
        var avgText = avg >= 120 ? $"{avg / 60:F0} 分钟" : $"{avg:F0} 秒";
        var verdict = avg >= 1800 ? "节奏稳定，大块时间在推进主线"
            : avg >= 600 ? "有一定碎片化，注意力大约每 10–30 分钟被打断一次"
            : "切换非常频繁，时间被切得很碎";
        insights.Add($"平均每次停留 {avgText}，全天切换 {p.switches} 次，{verdict}。");

        // 离开空档：覆盖率低到底是「阈值把工作切碎了」还是「本来就离开了」，必须写出来
        var gaps = new List<(DateTime A, DateTime B)>();
        for (var i = 1; i < clipped.Count; i++)
        {
            if ((clipped[i].Start - clipped[i - 1].End).TotalSeconds >= 300)
                gaps.Add((clipped[i - 1].End, clipped[i].Start));
        }

        if (gaps.Count > 0)
        {
            var longest = gaps.OrderByDescending(g => (g.B - g.A).TotalSeconds).First();
            insights.Add($"离开电脑 {gaps.Count} 次（每次 ≥5 分钟），最长一次 {(longest.B - longest.A):h\\h\\ mm\\m}（{longest.A:HH:mm}–{longest.B:HH:mm}）。");
        }
        else
        {
            insights.Add("全天没有超过 5 分钟的离开，数据连续性很好。");
        }

        return insights;
    }

    /// <summary>
    /// 最长专注按站点判定：同站点内 3 分钟以内的切换视为同一段专注。
    /// 只统计工作类站点 —— 系统弹窗、驱动面板、娱乐页面连续停留再久也不算专注。
    /// </summary>
    internal static (string Label, DateTime Start, double Dur) LongestFocus(List<Work> segs)
    {
        var bestDur = 0.0;
        var bestLabel = "";
        var bestStart = DateTime.MinValue;

        var curSite = "";
        var curStart = DateTime.MinValue;
        var curEnd = DateTime.MinValue;

        void Settle()
        {
            if (curSite.Length == 0) return;
            var d = (curEnd - curStart).TotalSeconds;
            if (d > bestDur)
            {
                bestDur = d;
                bestLabel = curSite;
                bestStart = curStart;
            }
        }

        foreach (var s in segs.OrderBy(x => x.Start))
        {
            // 非工作段直接切断当前专注，不能让它把前后两段工作「接」起来
            if (!Rules.IsWork(s.Group))
            {
                Settle();
                curSite = "";
                continue;
            }

            if (curSite.Equals(s.Site, StringComparison.OrdinalIgnoreCase) &&
                (s.Start - curEnd).TotalSeconds <= 180)
            {
                curEnd = s.End;
            }
            else
            {
                Settle();
                curSite = s.Site;
                curStart = s.Start;
                curEnd = s.End;
            }
        }
        Settle();

        return bestDur <= 0 ? ("", DateTime.MinValue, 0) : (bestLabel, bestStart, bestDur);
    }

    internal static string Dur(double sec)
    {
        // 与前端 KPI 的四舍五入保持一致，避免同一页面出现 15m / 16m 两种写法
        var totalMin = (int)Math.Round(sec / 60.0);
        if (totalMin < 1) return $"{Math.Max(1, (int)Math.Round(sec))}s";
        return totalMin >= 60 ? $"{totalMin / 60}h {totalMin % 60:D2}m" : $"{totalMin}m";
    }
}
