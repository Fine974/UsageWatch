using System.Text.RegularExpressions;

namespace AppUsage;

/// <summary>一段记录归类后的结果。</summary>
internal readonly record struct Classified(string App, string Site, string Page, string Group, bool Drop);

/// <summary>
/// 三层归类：应用 → 站点/项目 → 页面/文件。
/// 浏览器按站点归并（知乎 / GitHub / 在线文档），IDE 按项目归并（my-project / my-api），
/// 其余应用本身就作为站点。归并在聚合层做，不改原始数据，规则可以随时调。
/// </summary>
internal static class Classify
{
    private static readonly Regex SepRe = new(@"(?:\s+[–—]\s+|\s+-\s+|\s+·\s+|\s*•\s*)", RegexOptions.Compiled);

    private static readonly Dictionary<string, Classified> Cache = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    /// <summary>只有锁屏和空白页会被整段丢弃。</summary>
    internal static bool ShouldDrop(string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized)) return false;
        foreach (var d in Rules.DropTitles)
            if (normalized.Equals(d, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(d, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    internal static string Normalize(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var t = raw;
        foreach (var (re, rep) in Rules.Noise) t = re.Replace(t, rep);
        return Regex.Replace(t, @"\s+", " ").Trim();
    }

    /// <summary>把一个进程名 + 窗口标题归类。</summary>
    internal static Classified Of(string proc, string? rawTitle)
    {
        Rules.LoadOverrides();

        var key = proc + "\u0001" + (rawTitle ?? "");
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
        }

        var r = Compute(proc, rawTitle);
        lock (Gate)
        {
            if (Cache.Count < 20000) Cache[key] = r;
        }
        return r;
    }

    private static Classified Compute(string proc, string? rawTitle)
    {
        var title = Normalize(rawTitle);
        var appName = Rules.Apps.TryGetValue(proc, out var app) ? app.Name : proc;

        if (ShouldDrop(title))
            return new Classified(appName, appName, title, "空白", true);

        // 站点规则优先：命中就把整段归到该站点
        foreach (var rule in Rules.Sites)
        {
            if (!Regex.IsMatch(title, rule.Pattern, RegexOptions.IgnoreCase)) continue;
            return new Classified(appName, rule.Site, PageOf(title, rule.Site), rule.Group, false);
        }

        if (app is null)
            return new Classified(proc, proc, title.Length > 0 ? title : proc, "其他", false);

        // 浏览器未命中规则 → 长尾网页。归「未归类」而不是硬塞进某个分类：
        // 未命中意味着规则有缺口，让它显性暴露出来，才能被发现并补规则。
        if (app.Group == "浏览器")
            return new Classified(appName, "其他网页", title.Length > 0 ? title : "(无标题)", "未归类", false);

        // IDE 类：标题是「项目 – 文件」，第二层按项目归并
        if (app.ByProject)
        {
            var (proj, file) = ProjectOf(title, appName);
            return new Classified(appName, proj, file, app.Group, false);
        }

        return new Classified(appName, appName, title.Length > 0 ? title : appName, app.Group, false);
    }

    /// <summary>从「订单列表 - 管理后台」里剥出「订单列表」。</summary>
    private static string PageOf(string title, string site)
    {
        var esc = Regex.Escape(site);
        var p = Regex.Replace(title, @"\s*[-–—·]\s*" + esc + @"\s*$", "", RegexOptions.IgnoreCase).Trim();
        if (p == title)
            p = Regex.Replace(title, @"\s*[\[\(]" + esc + @"[\]\)]\s*$", "", RegexOptions.IgnoreCase).Trim();
        if (p.Length == 0 || p.Equals(site, StringComparison.OrdinalIgnoreCase)) p = site;
        return p;
    }

    /// <summary>
    /// 从「my-project – order_service.py」里取项目名和文件名。
    /// 分隔符必须是带空格的形式，否则 my-api-gateway 这种连字符项目名会被切坏。
    /// </summary>
    private static (string Project, string Page) ProjectOf(string title, string appName)
    {
        if (title.Length == 0) return (appName, appName);

        var t = title;
        // 去掉结尾的应用名（"- Cursor" / "– IntelliJ IDEA" / "- Sublime Text (UNREGISTERED)"）
        t = Regex.Replace(t, @"\s+[–—-]\s+" + Regex.Escape(appName) + @"(\s*\(.*\))?\s*$", "",
            RegexOptions.IgnoreCase).Trim();
        if (t.Length == 0) t = title;

        var parts = SepRe.Split(t, 2);
        if (parts.Length < 2)
        {
            // 没有分隔符：标题本身就是应用主界面名（如 "Cursor Agents"），
            // 去掉应用名当项目名，剩下的当页面名，避免站点表里出现 "Cursor Agents" 这种怪名字
            if (t.StartsWith(appName, StringComparison.OrdinalIgnoreCase))
            {
                var rest = t[appName.Length..].Trim(' ', '–', '—', '-', '·');
                return (appName, rest.Length > 0 ? rest : appName);
            }
            return (t, t);
        }

        var proj = parts[0].Trim();
        var file = parts[1].Trim();
        if (proj.Length == 0) return (t, t);
        if (file.Length == 0) file = proj;

        // "Cursor Agents – foo.ts" 这种：项目名其实是应用自己的界面，压成应用名
        if (proj.StartsWith(appName, StringComparison.OrdinalIgnoreCase))
        {
            var rest = proj[appName.Length..].Trim(' ', '–', '—', '-', '·');
            return (appName, rest.Length > 0 ? rest : file);
        }

        return (proj, file);
    }
}
