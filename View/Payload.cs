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

    /// <summary>深度工作块：同分类连续 ≥25 分钟视为一块，是「真正连续推进」的证据。</summary>
    public int deepCount { get; set; }
    public double deepSec { get; set; }

    /// <summary>碎片化三指标，只统计工作类，避免系统弹窗把数字稀释掉。</summary>
    public double medianStay { get; set; }
    public double shortPct { get; set; }
    public double switchPerHour { get; set; }

    public List<RowInfo> rows { get; set; } = new();
    public List<GroupInfo> groups { get; set; } = new();
    public List<SiteInfo> sites { get; set; } = new();
    public List<SegInfo> segs { get; set; } = new();
    public List<UnknownInfo> unknown { get; set; } = new();
    public double[] hourly { get; set; } = new double[24];
    public List<StackInfo> hourlyStack { get; set; } = new();
    public List<InsightInfo> insights { get; set; } = new();
}

internal sealed class FocusInfo
{
    /// <summary>分类口径：同一分类内部换站点/文件不算打断，这才是真实的深度工作时长。</summary>
    public string l { get; set; } = "";
    public string sub { get; set; } = "";
    public double s { get; set; }
    public double d { get; set; }

    /// <summary>站点口径：同一站点内连续，用来回答「在哪儿的」，数值通常明显更短。</summary>
    public string sl { get; set; } = "";
    public double ss { get; set; }
    public double sd { get; set; }
}

/// <summary>一条解读。w 是重要度，前端按它排序后取前三条，不再靠数组下标猜。</summary>
internal sealed class InsightInfo
{
    public string t { get; set; } = "";
    public int w { get; set; }
}

/// <summary>落在「未归类 / 其他」里的页面，列出来是为了让人直接补规则。</summary>
internal sealed class UnknownInfo
{
    public string n { get; set; } = "";
    public string p { get; set; } = "";
    public double sec { get; set; }
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

    /// <summary>深度工作块的下限：25 分钟。番茄钟口径，低于它更像是在处理零碎消息。</summary>
    internal const double DeepBlockSec = 1500;

    /// <summary>专注块判定：同类（或同站点）之间间隔不超过 3 分钟，算同一段。</summary>
    internal const double FocusGapSec = 180;

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
                // 用去噪后的 Page，不要原始 Title：原始标题带着「和另外 28 个页面 - 个人 - Microsoft Edge」，
                // hover 时根本看不清是哪个页面
                w = x.Page.Length > 0 ? x.Page : x.Title,
            });
        }

        // ---------- 未归类明细：列出来才能补规则 ----------
        result.unknown = clipped
            .Where(x => x.Group is "未归类" or "其他")
            .GroupBy(x => x.Page)
            .Select(g => new UnknownInfo
            {
                n = g.Key.Length > 0 ? g.Key : "(空标题)",
                p = g.First().App,
                sec = Math.Round(g.Sum(x => x.Sec), 1),
            })
            .OrderByDescending(x => x.sec)
            .Take(12)
            .ToList();

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

        // ---------- 专注：两套口径并列 ----------
        // 分类口径回答「真正连续投入了多久」，站点口径回答「在哪儿」。
        // 只显示站点口径会严重低估：在 Cursor 里切文件、开分支对比，专注就被切断了。
        var groupBlocks = GroupBlocks(clipped, FocusGapSec);
        var siteBlocks = StrictSiteBlocks(clipped, FocusGapSec);
        var bestGroup = BestBlock(groupBlocks);
        var bestSite = BestBlock(siteBlocks);

        result.focus = new FocusInfo
        {
            l = bestGroup.Key,
            sub = bestGroup.Sub,
            s = Sec(bestGroup, day0),
            d = Math.Round(bestGroup.Dur, 1),
            sl = bestSite.Key,
            ss = Sec(bestSite, day0),
            sd = Math.Round(bestSite.Dur, 1),
        };

        var deep = groupBlocks.Where(b => b.Dur >= DeepBlockSec).ToList();
        result.deepCount = deep.Count;
        result.deepSec = Math.Round(deep.Sum(b => b.Dur), 1);

        // ---------- 碎片化：只统计工作类 ----------
        // 全量平均停留会被系统弹窗、未归类稀释，看起来没那么碎，但那不是真实感受
        var workSegs = clipped.Where(x => Rules.IsWork(x.Group)).ToList();
        if (workSegs.Count > 0)
        {
            var workSec = workSegs.Sum(x => x.Sec);
            result.medianStay = Math.Round(Baseline.Median(workSegs.Select(x => x.Sec).ToList()), 1);
            result.shortPct = Math.Round(workSegs.Count(x => x.Sec < 60) * 100.0 / workSegs.Count, 1);
            result.switchPerHour = workSec > 0
                ? Math.Round(workSegs.Count / (workSec / 3600), 1)
                : 0;
        }

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

    /// <summary>
    /// 生成解读。每条带重要度 w，前端直接排序取前三条 —— 不靠数组下标，
    /// 因为这里随时会增删条目，下标一错就会把「分类占比」这种复述顶到最前面。
    /// </summary>
    private static List<InsightInfo> BuildInsights(DayPayload p, List<Work> clipped, double totalSec)
    {
        var list = new List<InsightInfo>();
        void Add(int w, string t) => list.Add(new InsightInfo { w = w, t = t });

        if (p.groups.Count > 1)
        {
            var head = $"{p.groups[0].n} {p.groups[0].pct:F0}%（{Dur(p.groups[0].sec)}）";
            var rest = p.groups.Skip(1).Take(2).Select(g => $"{g.n} {g.pct:F0}%").ToList();
            Add(1, $"时间主要落在 {head}，其次是 {string.Join("、", rest)}。");
        }

        if (p.sites.Count > 1)
            Add(1, $"待得最久的去处是 {p.sites[0].n}（{Dur(p.sites[0].sec)}，{p.sites[0].pct:F0}%），"
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
            Add(1, $"浏览器共 {Dur(bSec)}（占全天 {bSec / totalSec * 100:F0}%），"
                 + $"主要用在 {string.Join("、", parts)}；单站点最多是 {topSite.S} {Dur(topSite.Sec)}。");
        }

        // 专注：分类口径为主，站点口径作为对照写进同一句，避免两个数字互相打脸
        if (p.focus.d > 0)
        {
            var fs = TimeSpan.FromSeconds(p.focus.s);
            var fe = TimeSpan.FromSeconds(p.focus.s + p.focus.d);
            var where = p.focus.sub.Length > 0 && p.focus.sub != p.focus.l ? $" · {p.focus.sub}" : "";
            Add(6, $"最长专注 {Dur(p.focus.d)}，出现在 {fs.Hours:D2}:{fs.Minutes:D2}–{fe.Hours:D2}:{fe.Minutes:D2}"
                 + $"（{p.focus.l}{where}）。按站点口径算是 {Dur(p.focus.sd)}（{p.focus.sl}）。");
        }

        if (p.deepCount > 0)
            Add(7, $"有 {p.deepCount} 段超过 25 分钟的深度工作块，合计 {Dur(p.deepSec)}，占全天 {p.deepSec / totalSec * 100:F0}%。");
        else if (totalSec >= 5400)
            Add(7, "全天没有超过 25 分钟的深度工作块，时间都被切成了短段。");

        // 碎片化：中位停留比平均停留更能反映真实手感，少数长段会把平均值拉高
        if (p.medianStay > 0)
        {
            var verdict = p.shortPct >= 70 ? "大部分操作是被打断的"
                : p.shortPct >= 45 ? "接近一半的操作不超过 1 分钟"
                : "段落长度还算健康";
            Add(5, $"工作中位停留 {p.medianStay:F0} 秒，短于 1 分钟的段落占 {p.shortPct:F0}%（{verdict}），"
                 + $"平均每小时切换 {p.switchPerHour:F0} 次。");
        }

        if (p.unknown.Count > 0)
        {
            var unSec = p.unknown.Sum(x => x.sec);
            if (unSec >= 300)
                Add(9, $"{Dur(unSec)} 落在未归类的 {p.unknown.Count} 个页面上，规则还缺条目。");
        }

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
            Add(4, $"离开电脑 {gaps.Count} 次（每次 ≥5 分钟），最长一次 {(longest.B - longest.A):h\\h\\ mm\\m}（{longest.A:HH:mm}–{longest.B:HH:mm}）。");        }
        else
        {
            Add(2, "全天没有超过 5 分钟的离开，数据连续性很好。");
        }

        return list;
    }

    /// <summary>一段连续专注：同 key 且间隔不超过 gap 的记录合成一块。</summary>
    internal readonly record struct Block(string Key, DateTime Start, DateTime End, string Sub)
    {
        public double Dur => Start == DateTime.MinValue ? 0 : (End - Start).TotalSeconds;
    }

    private static string Dominant(Dictionary<string, double> subSec)
        => subSec.Count > 0 ? subSec.MaxBy(kv => kv.Value).Key : "";

    /// <summary>
    /// 分类口径的专注块：**只在该分类自己的段之间**判断间隔。
    ///
    /// 和「按时间轴逐段推进」的写法有本质区别。逐段推进时，中间插进来一条 10 秒的微信
    /// 就会把前后的编码段切开 —— 实测下来 66 分钟的深度编码会被报成 20 分钟。
    /// 这里改成：同类两段之间间隔 ≤3 分钟就合并，中间那点小打断算「顺手瞄了一眼」，
    /// 不算专注结束。这正是番茄钟允许短暂中断的口径。
    /// </summary>
    internal static List<Block> GroupBlocks(List<Work> ordered, double gapSec)
    {
        var res = new List<Block>();

        foreach (var grp in ordered.Where(x => Rules.IsWork(x.Group)).GroupBy(x => x.Group))
        {
            var seq = grp.OrderBy(x => x.Start).ToList();
            var start = seq[0].Start;
            var end = seq[0].End;
            var subSec = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            void Accum(Work w) => subSec[w.Site] = subSec.TryGetValue(w.Site, out var v) ? v + w.Sec : w.Sec;
            Accum(seq[0]);

            for (var i = 1; i < seq.Count; i++)
            {
                var s = seq[i];
                if ((s.Start - end).TotalSeconds <= gapSec)
                {
                    if (s.End > end) end = s.End;
                    Accum(s);
                    continue;
                }
                res.Add(new Block(grp.Key, start, end, Dominant(subSec)));
                start = s.Start;
                end = s.End;
                subSec.Clear();
                Accum(s);
            }
            res.Add(new Block(grp.Key, start, end, Dominant(subSec)));
        }

        return res;
    }

    /// <summary>
    /// 站点口径的专注块：沿时间轴逐段推进，换到任何别的地方就算中断。
    /// 数值明显更小，但它回答的是另一个问题 ——「有多久完全没离开过这个界面」。
    /// </summary>
    internal static List<Block> StrictSiteBlocks(List<Work> ordered, double gapSec)
    {
        var res = new List<Block>();
        var open = false;
        var site = "";
        var start = DateTime.MinValue;
        var end = DateTime.MinValue;

        void Settle()
        {
            if (open) res.Add(new Block(site, start, end, site));
        }

        foreach (var s in ordered)
        {
            if (!Rules.IsWork(s.Group))
            {
                Settle();
                open = false;
                continue;
            }

            if (open && site == s.Site && (s.Start - end).TotalSeconds <= gapSec)
            {
                if (s.End > end) end = s.End;
            }
            else
            {
                Settle();
                open = true;
                site = s.Site;
                start = s.Start;
                end = s.End;
            }
        }
        Settle();
        return res;
    }

    private static Block BestBlock(List<Block> bs)
        => bs.Count == 0 ? default : bs.MaxBy(b => b.Dur);

    private static double Sec(Block b, DateTime day0)
        => b.Dur <= 0 ? 0 : Math.Round((b.Start - day0).TotalSeconds, 1);

    internal static string Dur(double sec)
    {
        // 与前端 KPI 的四舍五入保持一致，避免同一页面出现 15m / 16m 两种写法
        var totalMin = (int)Math.Round(sec / 60.0);
        if (totalMin < 1) return $"{Math.Max(1, (int)Math.Round(sec))}s";
        return totalMin >= 60 ? $"{totalMin / 60}h {totalMin % 60:D2}m" : $"{totalMin}m";
    }
}
