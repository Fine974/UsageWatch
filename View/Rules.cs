using System.Text.Json;
using System.Text.RegularExpressions;

namespace AppUsage;

internal sealed record SiteRule(string Pattern, string Site, string Group);

internal sealed record AppRule(string Proc, string Name, string Group, bool ByProject = false);

/// <summary>
/// 分类规则表。内置一份默认规则，允许用 %APPDATA%\AppUsage\rules.json 覆盖/追加。
/// 外部规则的优先级高于内置，方便不重新编译就调整归类。
/// </summary>
internal static class Rules
{
    private static readonly Dictionary<string, string> GroupColorMap = new(StringComparer.Ordinal)
    {
        ["编码"] = "#4f9cf9",
        ["沟通"] = "#5ac8a8",
        ["业务系统"] = "#f2a65a",
        ["资讯社区"] = "#d96ba0",
        ["内部工具"] = "#9b7ede",
        ["CI/CD"] = "#e5674e",
        ["API文档"] = "#4fc3d9",
        ["项目管理"] = "#c9b458",
        ["办公"] = "#7b8fd4",
        ["系统"] = "#6f7d94",
        ["工具"] = "#6fbf73",
        ["娱乐"] = "#d4726a",
        ["AI工具"] = "#8b7bf0",
        ["代码托管"] = "#5d9ca6",
        ["设计"] = "#e0a0d0",
        // 未归类要显眼一点：它是「规则还没覆盖到」的信号，看到就该去补规则
        ["未归类"] = "#b07aa8",
        ["其他"] = "#8892a6",
    };

    /// <summary>这些分类不算工作，不参与「最长专注」评选，否则驱动/设置弹窗会抢走专注指标。</summary>
    private static readonly HashSet<string> NotWork = new(StringComparer.Ordinal)
    {
        "系统", "娱乐", "其他", "未归类", "空白",
    };

    internal static bool IsWork(string group) => !NotWork.Contains(group);

    internal static string ColorOf(string group) =>
        GroupColorMap.TryGetValue(group, out var c) ? c : "#8892a6";

    /// <summary>
    /// 标题去噪：浏览器会在标题里塞标签页数量、私信未读数、应用名。
    /// 不剥掉这些，同一个页面会被拆成几十条（实测知乎被拆成 34 条）。
    /// </summary>
    internal static readonly (Regex Re, string Replacement)[] Noise =
    {
        (new Regex(@"\s*(?:和另外\s*\d+\s*个页面|and \d+\s*more\s*pages?)\s*-\s*(?:个人|工作|Personal|Work)?\s*-?\s*Microsoft.{0,3}Edge\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled), ""),
        (new Regex(@"\s*-\s*Microsoft.{0,3}Edge\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled), ""),
        (new Regex(@"\s*-\s*Google Chrome\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled), ""),
        (new Regex(@"\s*-\s*(?:个人|工作|Personal|Work)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled), ""),
        (new Regex(@"^\(\s*\d+\s*封私信\s*/\s*\d+\s*条消息\s*\)\s*", RegexOptions.Compiled), ""),
        (new Regex(@"^[\(\[]\s*\d+\s*[\)\]]\s*", RegexOptions.Compiled), ""),
        (new Regex(@"\s*•\s*\((?:文档|Document)\)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled), ""),
    };

    /// <summary>整段丢弃的标题：锁屏、空白页之类，不应计入任何工作。</summary>
    private static readonly List<string> DropList = new()
    {
        "Windows 默认锁屏界面", "锁屏界面", "新建标签页", "新标签页", "无标题", "通知中心",
    };

    internal static IReadOnlyList<string> DropTitles => DropList;

    /// <summary>
    /// 站点规则：匹配窗口标题，按顺序匹配、先命中先用，所以更具体的规则必须排在通用规则前面。
    /// 宽泛的模式（比如单独的「文档」「后台」）要放在最后，否则会把具体站点提前吃掉。
    ///
    /// 这里只放通用软件与公开网站。公司内部系统、内网地址、具体业务系统这类个性化规则，
    /// 请写进 %APPDATA%\AppUsage\rules.json —— 外置规则优先级更高，可以覆盖这里，
    /// 也不会把内部信息带进代码仓库。格式见 rules.example.json。
    /// </summary>
    internal static readonly List<SiteRule> Sites = new()
    {
        // 项目管理与协作
        new("禅道",                            "禅道",            "项目管理"),
        new("Jira|Confluence",                 "Jira",            "项目管理"),
        new("TAPD",                            "TAPD",            "项目管理"),
        new("飞书|Feishu|\\bLark\\b",           "飞书",            "沟通"),
        new("Notion",                          "Notion",          "办公"),
        new("语雀",                             "语雀",            "办公"),
        new("金山文档|腾讯文档|石墨文档",          "在线文档",         "办公"),

        // 代码托管与流水线
        new("\\[Jenkins\\]|^Jenkins$",         "Jenkins",         "CI/CD"),
        new("GitLab",                          "GitLab",          "代码托管"),
        new("github\\.com|·\\s*GitHub",         "GitHub",          "代码托管"),
        new("gitee\\.com|·\\s*Gitee",           "Gitee",           "代码托管"),
        new("code-server",                     "code-server",     "编码"),

        // 资讯与社区
        new("知乎\\s*$",                        "知乎",            "资讯社区"),
        new("Stack Overflow",                  "Stack Overflow",  "资讯社区"),
        new("掘金|CSDN|博客园|SegmentFault",      "技术社区",         "资讯社区"),
        new("哔哩哔哩|bilibili",                 "哔哩哔哩",         "娱乐"),
        new("YouTube",                         "YouTube",         "娱乐"),

        // AI 助手
        new("DeepSeek|ChatGPT|Kimi|通义|文心一言|Claude|Gemini|豆包|Copilot", "AI 助手", "AI工具"),

        // 设计与原型
        new("Figma",                           "Figma",           "设计"),

        // 基础设施与运维
        new("Kubernetes Dashboard|\\bk8s\\b",   "K8s Dashboard",   "内部工具"),
        new("Grafana",                         "Grafana",         "内部工具"),
        new("Docker Desktop",                  "Docker",          "内部工具"),

        // API 调试与文档
        new("Swagger|Apifox|Postman",           "API 工具",         "API文档"),
        new("开放平台|API\\s*文档|Developer Docs", "开发文档",         "API文档"),

        // 杂项小工具
        new("JSON在线|Base64|在线解析|正则测试|时间戳转换", "在线小工具",  "工具"),
        new("应用使用监控|UsageWatch",            "UsageWatch",      "工具"),
        new("WorkBuddy",                       "WorkBuddy",       "AI工具"),
    };

    /// <summary>
    /// 应用映射：按进程名（不含 .exe）归类，大小写不敏感。
    /// ByProject=true 表示标题第二层是「项目」而不是「页面」，IDE 类应用用这个。
    /// 同样只收录通用软件，内部工具请配在 rules.json 的 apps 里。
    /// </summary>
    internal static readonly Dictionary<string, AppRule> Apps = new(StringComparer.OrdinalIgnoreCase)
    {
        // 浏览器
        ["msedge"] = new("msedge", "Edge", "浏览器"),
        ["chrome"] = new("chrome", "Chrome", "浏览器"),
        ["firefox"] = new("firefox", "Firefox", "浏览器"),

        // 编辑器与 IDE
        ["idea64"] = new("idea64", "IntelliJ IDEA", "编码", true),
        ["webstorm64"] = new("webstorm64", "WebStorm", "编码", true),
        ["cursor"] = new("cursor", "Cursor", "编码", true),
        ["code"] = new("code", "VS Code", "编码", true),
        ["sublime_text"] = new("sublime_text", "Sublime Text", "编码", true),
        ["datagrip64"] = new("datagrip64", "DataGrip", "编码", true),
        ["pycharm64"] = new("pycharm64", "PyCharm", "编码", true),
        ["notepad++"] = new("notepad++", "Notepad++", "编码"),
        ["devenv"] = new("devenv", "Visual Studio", "编码", true),

        // 沟通
        ["weixin"] = new("weixin", "微信", "沟通"),
        ["wechatappex"] = new("wechatappex", "微信小程序", "沟通"),
        ["wechat"] = new("wechat", "微信", "沟通"),
        ["wxwork"] = new("wxwork", "企业微信", "沟通"),
        ["cloudhub"] = new("cloudhub", "云之家", "沟通"),
        ["qq"] = new("qq", "QQ", "沟通"),
        ["dingtalk"] = new("dingtalk", "钉钉", "沟通"),
        ["feishu"] = new("feishu", "飞书", "沟通"),

        // 办公
        ["wps"] = new("wps", "WPS", "办公"),
        ["winword"] = new("winword", "Word", "办公"),
        ["excel"] = new("excel", "Excel", "办公"),
        ["powerpnt"] = new("powerpnt", "PowerPoint", "办公"),
        ["notepad"] = new("notepad", "记事本", "办公"),
        ["typora"] = new("typora", "Typora", "办公"),
        ["obsidian"] = new("obsidian", "Obsidian", "办公"),
        ["notion"] = new("notion", "Notion", "办公"),

        // API 与数据库工具
        ["apifox"] = new("apifox", "Apifox", "API文档"),
        ["postman"] = new("postman", "Postman", "API文档"),
        ["navicat"] = new("navicat", "Navicat", "编码"),
        ["dbeaver"] = new("dbeaver", "DBeaver", "编码"),

        // 效率与工具
        ["workbuddy"] = new("workbuddy", "WorkBuddy", "AI工具"),
        ["usagewatch"] = new("usagewatch", "UsageWatch", "工具"),
        ["snipaste"] = new("snipaste", "Snipaste", "工具"),
        ["switchhosts"] = new("switchhosts", "SwitchHosts", "工具"),
        ["baidunetdiskunite"] = new("baidunetdiskunite", "百度网盘", "工具"),

        // 娱乐
        ["cloudmusic"] = new("cloudmusic", "网易云音乐", "娱乐"),
        ["spotify"] = new("spotify", "Spotify", "娱乐"),
        ["potplayermini64"] = new("potplayermini64", "PotPlayer", "娱乐"),

        // 系统组件（这些会频繁抢占前台，必须单独归类，否则会污染专注统计）
        ["explorer"] = new("explorer", "资源管理器", "系统"),
        ["windterm"] = new("windterm", "WindTerm", "系统"),
        ["shellhost"] = new("shellhost", "ShellHost", "系统"),
        ["awesun"] = new("awesun", "向日葵远控", "系统"),
        ["applicationframehost"] = new("applicationframehost", "系统组件", "系统"),
        ["photos"] = new("photos", "照片", "系统"),
        ["windowsterminal"] = new("windowsterminal", "终端", "系统"),
        ["taskmgr"] = new("taskmgr", "任务管理器", "系统"),
        ["mstsc"] = new("mstsc", "远程桌面", "系统"),
    };

    private static bool _loaded;

    /// <summary>从 rules.json 读外部规则，插到最前面（优先级更高）。</summary>
    internal static void LoadOverrides()
    {
        if (_loaded) return;
        _loaded = true;

        var path = Path.Combine(Store.Root, "rules.json");
        if (!File.Exists(path)) return;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            if (root.TryGetProperty("sites", out var sites) && sites.ValueKind == JsonValueKind.Array)
            {
                var extra = new List<SiteRule>();
                foreach (var e in sites.EnumerateArray())
                {
                    var m = e.GetProperty("match").GetString();
                    var s = e.GetProperty("site").GetString();
                    var g = e.TryGetProperty("group", out var gg) ? gg.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(m) && !string.IsNullOrWhiteSpace(s))
                        extra.Add(new SiteRule(m!, s!, string.IsNullOrWhiteSpace(g) ? "其他" : g!));
                }
                if (extra.Count > 0) Sites.InsertRange(0, extra);
            }

            if (root.TryGetProperty("apps", out var apps) && apps.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in apps.EnumerateArray())
                {
                    var p = e.GetProperty("proc").GetString();
                    if (string.IsNullOrWhiteSpace(p)) continue;
                    var n = e.TryGetProperty("name", out var nn) ? nn.GetString() : p;
                    var g = e.TryGetProperty("group", out var gg) ? gg.GetString() : "其他";
                    var byProj = e.TryGetProperty("byProject", out var bp) && bp.ValueKind == JsonValueKind.True;
                    Apps[p!] = new AppRule(p!, n ?? p!, g ?? "其他", byProj);
                }
            }

            if (root.TryGetProperty("drop", out var drop) && drop.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in drop.EnumerateArray())
                {
                    var v = x.GetString();
                    if (!string.IsNullOrWhiteSpace(v) && !DropList.Contains(v)) DropList.Add(v!);
                }
            }
        }
        catch
        {
            // 规则文件写错了不能影响采集，静默退回内置规则
        }
    }
}
