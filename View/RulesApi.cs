using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AppUsage;

internal sealed class RuleSiteDto
{
    public string match { get; set; } = "";
    public string site { get; set; } = "";
    public string group { get; set; } = "";
}

internal sealed class RuleAppDto
{
    public string proc { get; set; } = "";
    public string name { get; set; } = "";
    public string group { get; set; } = "";
    public bool byProject { get; set; }
}

internal sealed class GroupDto
{
    public string n { get; set; } = "";
    public string c { get; set; } = "";
    public bool work { get; set; }
}

/// <summary>一条「没被规则覆盖到」的记录。kind=site 表示该写站点规则，kind=app 表示该补应用规则。</summary>
internal sealed class UnknownDto
{
    public string page { get; set; } = "";
    public string app { get; set; } = "";
    public string proc { get; set; } = "";
    public string kind { get; set; } = "site";
    public double sec { get; set; }
    public int n { get; set; }
    public string last { get; set; } = "";
    public string suggest { get; set; } = "";
}

internal sealed class RulesSnapshot
{
    public string path { get; set; } = "";
    public bool exists { get; set; }
    public string? error { get; set; }
    public List<RuleSiteDto> sites { get; set; } = new();
    public List<RuleAppDto> apps { get; set; } = new();
    public List<string> drop { get; set; } = new();
    public List<RuleSiteDto> builtinSites { get; set; } = new();
    public List<RuleAppDto> builtinApps { get; set; } = new();
    public List<GroupDto> groups { get; set; } = new();
    public List<UnknownDto> unknown { get; set; } = new();
    public int days { get; set; }
    public double unknownSec { get; set; }
    public int unknownPages { get; set; }
}

internal sealed class RulesSaveRequest
{
    public List<RuleSiteDto>? sites { get; set; }
    public List<RuleAppDto>? apps { get; set; }
    public List<string>? drop { get; set; }
}

internal sealed class RulesTestRequest
{
    public string? proc { get; set; }
    public string? title { get; set; }
}

internal sealed class RulesSaveResult
{
    public bool ok { get; set; }
    public List<string> errors { get; set; } = new();
    public int sites { get; set; }
    public int apps { get; set; }
    public int drop { get; set; }
    public string? backup { get; set; }
}

/// <summary>
/// 配置页的服务端。读写的就是 %APPDATA%\AppUsage\rules.json，
/// 不存在第二份真相 —— 手工编辑和页面编辑是同一条路。
/// </summary>
internal static class RulesApi
{
    private static readonly object WriteGate = new();

    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // ---------------------------------------------------------------- 读

    internal static RulesSnapshot Get(int days = 7)
    {
        Rules.LoadOverrides();

        var snap = new RulesSnapshot
        {
            path = Rules.FilePath,
            exists = File.Exists(Rules.FilePath),
            days = days,
        };

        var (sites, apps, drops, error) = ReadFile();
        snap.sites = sites;
        snap.apps = apps;
        snap.drop = drops;
        snap.error = error;

        snap.builtinSites = Rules.BuiltinSiteRules
            .Select(r => new RuleSiteDto { match = r.Pattern, site = r.Site, group = r.Group })
            .ToList();

        snap.builtinApps = Rules.BuiltinAppRules.Values
            .OrderBy(a => a.Proc, StringComparer.OrdinalIgnoreCase)
            .Select(a => new RuleAppDto { proc = a.Proc, name = a.Name, group = a.Group, byProject = a.ByProject })
            .ToList();

        snap.groups = Rules.Groups()
            .Select(g => new GroupDto { n = g.Name, c = g.Color, work = g.Work })
            .ToList();

        CollectUnknown(snap, days);
        return snap;
    }

    /// <summary>只读外置文件本身，不合并内置 —— 配置页要编辑的是「用户自己写的那部分」。</summary>
    private static (List<RuleSiteDto>, List<RuleAppDto>, List<string>, string?) ReadFile()
    {
        var sites = new List<RuleSiteDto>();
        var apps = new List<RuleAppDto>();
        var drops = new List<string>();

        var path = Rules.FilePath;
        if (!File.Exists(path)) return (sites, apps, drops, null);

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            if (root.TryGetProperty("sites", out var sa) && sa.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in sa.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    sites.Add(new RuleSiteDto
                    {
                        match = Str(e, "match") ?? "",
                        site = Str(e, "site") ?? "",
                        group = Str(e, "group") ?? "其他",
                    });
                }
            }

            if (root.TryGetProperty("apps", out var aa) && aa.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in aa.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    var proc = Str(e, "proc") ?? "";
                    apps.Add(new RuleAppDto
                    {
                        proc = proc,
                        name = Str(e, "name") ?? proc,
                        group = Str(e, "group") ?? "其他",
                        byProject = e.TryGetProperty("byProject", out var bp) && bp.ValueKind == JsonValueKind.True,
                    });
                }
            }

            if (root.TryGetProperty("drop", out var da) && da.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in da.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.String) continue;
                    var v = e.GetString();
                    if (!string.IsNullOrWhiteSpace(v)) drops.Add(v!);
                }
            }
        }
        catch (Exception ex)
        {
            return (sites, apps, drops, ex.Message);
        }

        return (sites, apps, drops, null);
    }

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>
    /// 采集「没被覆盖到」的记录。分两类：
    /// 未归类 = 浏览器网页没命中站点规则；其他 = 进程没配应用规则。
    /// 两类要用不同手段解决，混在一起看会写错规则。
    /// </summary>
    private static void CollectUnknown(RulesSnapshot snap, int days)
    {
        var acc = new Dictionary<string, UnknownDto>(StringComparer.Ordinal);

        foreach (var ds in Store.AvailableDates().Take(days))
        {
            if (!DateTime.TryParseExact(ds, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var d)) continue;

            List<Payload.Work> works;
            try { works = Payload.Prepare(d, Store.Load(d)); }
            catch { continue; }
            foreach (var w in works)
            {
                var kind = w.Group == "未归类" ? "site" : w.Group == "其他" ? "app" : null;
                if (kind is null) continue;

                var key = kind + "\u0001" + w.App + "\u0001" + w.Page;
                if (!acc.TryGetValue(key, out var item))
                {
                    item = new UnknownDto
                    {
                        page = w.Page,
                        app = w.App,
                        proc = w.Proc,
                        kind = kind,
                        last = d.ToString("MM-dd"),
                        suggest = kind == "site" ? SuggestMatch(w.Page) : w.Proc,
                    };
                    acc[key] = item;
                }

                item.sec += w.Sec;
                item.n++;
            }
        }

        snap.unknown = acc.Values
            .OrderByDescending(x => x.sec)
            .Take(60)
            .Select(x =>
            {
                x.sec = Math.Round(x.sec, 1);
                return x;
            })
            .ToList();

        snap.unknownSec = Math.Round(acc.Values.Sum(x => x.sec), 1);
        snap.unknownPages = acc.Count;
    }

    /// <summary>
    /// 从一条标题里猜一个「够稳」的正则片段：去掉括号里的补充说明、编号、纯数字，
    /// 再按分隔符切分取最长的部分。带上 #123 这种易变内容，规则过两天就失效了。
    /// </summary>
    private static string SuggestMatch(string page)
    {
        if (string.IsNullOrWhiteSpace(page)) return "";

        var t = Regex.Replace(page, @"[【\[\(（][^】\]\)）]*[】\]\)）]", " ");
        t = Regex.Replace(t, @"#\s*\d+|(?<![\d.])\d{2,}(?![\d.])", " ");

        var best = Regex.Split(t, @"\s*[-–—·|/_]\s*|\s{2,}")
            .Select(x => x.Trim())
            .Where(x => x.Length >= 2)
            .OrderByDescending(x => x.Length)
            .FirstOrDefault();

        var pick = string.IsNullOrWhiteSpace(best) ? page.Trim() : best;
        if (pick.Length > 24) pick = pick[..24];
        return Regex.Escape(pick).Replace("\\ ", " ");
    }

    // ---------------------------------------------------------------- 写

    internal static RulesSaveResult Save(RulesSaveRequest req)
    {
        var res = new RulesSaveResult();

        var sites = NormalizeSites(req.sites, res.errors);
        var apps = NormalizeApps(req.apps, res.errors);
        var drops = (req.drop ?? new List<string>())
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (res.errors.Count > 0)
        {
            res.ok = false;
            return res;
        }

        lock (WriteGate)
        {
            try
            {
                var path = Rules.FilePath;
                Store.EnsureRoot();

                // 保留原文件里的其它字段（_说明、_用法、用户自己加的注释），只替换三类规则
                JsonObject root;
                var isNew = !File.Exists(path);
                if (!isNew)
                {
                    try { root = (JsonNode.Parse(File.ReadAllText(path)) as JsonObject) ?? new JsonObject(); }
                    catch { root = new JsonObject(); }
                }
                else
                {
                    root = new JsonObject
                    {
                        ["_说明"] = "由 UsageWatch 配置页维护（也可以手工编辑）。保存后立即生效，不需要重启。",
                    };
                }

                root["sites"] = SiteArray(sites);
                root["apps"] = AppArray(apps);
                root["drop"] = new JsonArray(drops.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray());

                var json = root.ToJsonString(WriteOpts);

                // 先备份再覆盖：正则写坏了用户还能捞回来
                if (!isNew)
                {
                    var bak = path + ".bak";
                    File.Copy(path, bak, true);
                    res.backup = bak;
                }

                var tmp = path + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                File.Move(tmp, path, true);

                Rules.Reload();

                res.ok = Rules.LastError is null;
                if (Rules.LastError is not null) res.errors.Add(Rules.LastError);
                res.sites = sites.Count;
                res.apps = apps.Count;
                res.drop = drops.Count;
            }
            catch (Exception ex)
            {
                res.ok = false;
                res.errors.Add(ex.Message);
            }
        }

        return res;
    }

    private static List<RuleSiteDto> NormalizeSites(List<RuleSiteDto>? input, List<string> errors)
    {
        var list = new List<RuleSiteDto>();
        var i = 0;
        foreach (var s in input ?? new List<RuleSiteDto>())
        {
            i++;
            var m = (s.match ?? "").Trim();
            var site = (s.site ?? "").Trim();
            var group = (s.group ?? "").Trim();

            if (m.Length == 0 && site.Length == 0) continue;
            if (m.Length == 0) { errors.Add($"站点规则第 {i} 条：匹配表达式不能为空"); continue; }
            if (site.Length == 0) { errors.Add($"站点规则第 {i} 条：站点名不能为空"); continue; }

            try { _ = new Regex(m); }
            catch (Exception ex) { errors.Add($"站点规则第 {i} 条正则无效：{ex.Message}"); continue; }

            list.Add(new RuleSiteDto { match = m, site = site, group = group.Length > 0 ? group : "其他" });
        }
        return list;
    }

    private static List<RuleAppDto> NormalizeApps(List<RuleAppDto>? input, List<string> errors)
    {
        var list = new List<RuleAppDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;

        foreach (var a in input ?? new List<RuleAppDto>())
        {
            i++;
            var proc = (a.proc ?? "").Trim();
            if (proc.Length == 0) continue;

            // 进程名带 .exe 是最常见的笔误：规则不生效又不报错，很难查
            if (proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                proc = proc[..^4];
            }
            if (!seen.Add(proc))
            {
                errors.Add($"应用规则第 {i} 条：进程 {proc} 重复");
                continue;
            }

            var name = (a.name ?? "").Trim();
            var group = (a.group ?? "").Trim();
            list.Add(new RuleAppDto
            {
                proc = proc,
                name = name.Length > 0 ? name : proc,
                group = group.Length > 0 ? group : "其他",
                byProject = a.byProject,
            });
        }
        return list;
    }

    private static JsonArray SiteArray(List<RuleSiteDto> sites)
    {
        var arr = new JsonArray();
        foreach (var s in sites)
        {
            arr.Add(new JsonObject
            {
                ["match"] = s.match,
                ["site"] = s.site,
                ["group"] = s.group,
            });
        }
        return arr;
    }

    private static JsonArray AppArray(List<RuleAppDto> apps)
    {
        var arr = new JsonArray();
        foreach (var a in apps)
        {
            var o = new JsonObject
            {
                ["proc"] = a.proc,
                ["name"] = a.name,
                ["group"] = a.group,
            };
            if (a.byProject) o["byProject"] = true;
            arr.Add(o);
        }
        return arr;
    }

    // ---------------------------------------------------------------- 试跑

    /// <summary>
    /// 拿一条真实标题跑一遍归类，并指出命中的是哪一条规则（外置还是内置）。
    /// 写正则最大的坑是「以为命中了其实没命中」，或者被更靠前的宽泛规则先吃掉。
    /// </summary>
    internal static object Test(string proc, string? rawTitle)
    {
        Rules.LoadOverrides();

        var title = Classify.Normalize(rawTitle);
        var externalCount = Math.Max(0, Rules.Sites.Count - Rules.BuiltinSiteRules.Count);

        RuleSiteDto? hit = null;
        var hitAt = "";
        var hitIndex = -1;

        var all = Rules.Sites;
        for (var i = 0; i < all.Count; i++)
        {
            bool ok;
            try { ok = Regex.IsMatch(title, all[i].Pattern, RegexOptions.IgnoreCase); }
            catch { continue; }
            if (!ok) continue;

            hitIndex = i;
            hit = new RuleSiteDto { match = all[i].Pattern, site = all[i].Site, group = all[i].Group };
            hitAt = i < externalCount ? $"外置规则 #{i + 1}" : $"内置规则 #{i - externalCount + 1}";
            break;
        }

        var cls = Classify.Of(proc, rawTitle);

        return new
        {
            proc,
            raw = rawTitle ?? "",
            title,
            app = cls.App,
            site = cls.Site,
            page = cls.Page,
            group = cls.Group,
            drop = cls.Drop,
            hitAt,
            hitIndex,
            hit,
            externalCount,
            // 命中的是内置规则时提示一句：想改分类得加外置规则压过去，改内置没用
            note = hitAt.StartsWith("内置", StringComparison.Ordinal)
                ? "命中的是内置规则。外置规则优先级更高，想换分类就在上面加一条更早生效的站点规则。"
                : "",
        };
    }
}
