using System.Reflection;
using System.Text.Json;

namespace AppUsage;

internal static class Dashboard
{
    internal static string BuildHtml(DateTime day, DayPayload p)
    {
        var html = Read("AppUsage.Web.index.html");
        var js = Read("AppUsage.Web.render.js");
        var json = JsonSerializer.Serialize(p).Replace("</", "<\\/");

        // 静态日报也带上基线，否则「今天算多算少」这个最重要的判断就没了。
        // 传 day.Date 而不是 day+1：Compute 会排除 >= today 的日子，避免拿当天自己跟自己比
        var baseline = JsonSerializer.Serialize(Baseline.Get(30, day.Date)).Replace("</", "<\\/");
        var boot = "window.__STATIC__=true;document.body.classList.add('static');renderDashboard(" + json + "," + baseline + ");";

        return html
            .Replace("<!--RENDER_JS-->", "<script>\n" + js + "\n</script>")
            .Replace("/*__BOOT__*/", boot);
    }

    private static string Read(string resource)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"缺少嵌入资源: {resource}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
