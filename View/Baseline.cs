using System.Globalization;

namespace AppUsage;

internal sealed class BaseDay
{
    public string d { get; set; } = "";
    public double sec { get; set; }
}

internal sealed class BaselineInfo
{
    public int window { get; set; }
    public int days { get; set; }
    public double medTotal { get; set; }
    public double avgTotal { get; set; }
    public double p25 { get; set; }
    public double p75 { get; set; }
    public double medCoverage { get; set; }
    public double medSwitches { get; set; }
    public Dictionary<string, double> groupPct { get; set; } = new();
    public List<BaseDay> recent { get; set; } = new();
}

/// <summary>
/// 历史基线：拿最近若干个有记录的工作日算中位数，给「今天算多还是算少」一个参照。
/// 少于 1.5 小时的日子不进统计（装环境、只开一会儿机的半天会把中位数拉偏），
/// 但它们在 recent 里照常出现，趋势图才完整。
/// </summary>
internal static class Baseline
{
    private const int MinSec = 5400;

    private static BaselineInfo? _cache;
    private static DateTime _cacheAt;
    private static DateTime _cacheDay;
    private static readonly object Gate = new();

    internal static BaselineInfo Get(int window, DateTime today)
    {
        lock (Gate)
        {
            if (_cache is not null && _cacheDay == today.Date &&
                (DateTime.Now - _cacheAt).TotalSeconds < 60)
                return _cache;
        }

        var info = Compute(window, today);

        lock (Gate)
        {
            _cache = info;
            _cacheAt = DateTime.Now;
            _cacheDay = today.Date;
        }
        return info;
    }

    private static BaselineInfo Compute(int window, DateTime today)
    {
        var info = new BaselineInfo { window = window };

        var dates = new List<DateTime>();
        foreach (var s in Store.AvailableDates())
        {
            if (!DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var d)) continue;
            if (d.Date >= today.Date) continue;
            dates.Add(d.Date);
            if (dates.Count >= window) break;
        }

        var totals = new List<double>();
        var covers = new List<double>();
        var switchCounts = new List<double>();
        var groupPct = new Dictionary<string, List<double>>(StringComparer.Ordinal);

        // recent 按时间正序，趋势图从左到右就是日子往前走
        foreach (var d in Enumerable.Reverse(dates))
        {
            var work = Payload.Prepare(d, Store.Load(d));
            if (work.Count == 0) continue;

            var total = work.Sum(x => x.Sec);
            info.recent.Add(new BaseDay
            {
                d = d.ToString("MM-dd"),
                sec = Math.Round(total, 0),
            });

            if (total < MinSec) continue;

            totals.Add(total);
            switchCounts.Add(work.Count);

            var first = work.Min(x => x.Start);
            var last = work.Max(x => x.End);
            var span = Math.Max(1, (last - first).TotalSeconds);
            covers.Add(Math.Min(100, total / span * 100));

            foreach (var g in work.GroupBy(x => x.Group))
                Append(groupPct, g.Key, g.Sum(x => x.Sec) / total * 100);
        }

        info.days = totals.Count;
        info.medTotal = Math.Round(Median(totals), 0);
        info.avgTotal = Math.Round(totals.Count > 0 ? totals.Average() : 0, 0);
        info.p25 = Math.Round(Percentile(totals, 0.25), 0);
        info.p75 = Math.Round(Percentile(totals, 0.75), 0);
        info.medCoverage = Math.Round(Median(covers), 1);
        info.medSwitches = Math.Round(Median(switchCounts), 0);

        foreach (var kv in groupPct)
            info.groupPct[kv.Key] = Math.Round(Median(kv.Value), 1);

        return info;
    }

    private static void Append(Dictionary<string, List<double>> map, string key, double v)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<double>();
            map[key] = list;
        }
        list.Add(v);
    }

    internal static double Median(List<double> xs)
    {
        if (xs.Count == 0) return 0;
        var a = xs.OrderBy(x => x).ToList();
        var m = a.Count / 2;
        return a.Count % 2 == 1 ? a[m] : (a[m - 1] + a[m]) / 2;
    }

    private static double Percentile(List<double> xs, double p)
    {
        if (xs.Count == 0) return 0;
        var a = xs.OrderBy(x => x).ToList();
        var i = (int)Math.Round((a.Count - 1) * p);
        return a[Math.Clamp(i, 0, a.Count - 1)];
    }
}
