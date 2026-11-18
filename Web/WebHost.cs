using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AppUsage;

internal static class WebHost
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = null,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static HttpListener? _listener;
    private static string? _indexHtml;

    internal static Task WhenReady => _ready.Task;

    internal static int Port { get; private set; } = 5279;

    internal static string Url => $"http://127.0.0.1:{Port}";

    internal static async Task RunAsync(int preferredPort, CancellationToken ct)
    {
        Port = FindFreePort(preferredPort);

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");

        try
        {
            _listener.Start();
        }
        catch (HttpListenerException)
        {
            // 127.0.0.1 前缀被占用时退回 localhost 通配
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Start();
        }

        Store.EnsureRoot();
        try { File.WriteAllText(Path.Combine(Store.Root, "port.txt"), Port.ToString()); } catch { }

        _ready.TrySetResult();

        using var reg = ct.Register(() =>
        {
            try { _listener?.Stop(); } catch { }
        });

        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync();
            }
            catch
            {
                break;
            }

            _ = Task.Run(() => Handle(ctx));
        }
    }

    private static void Handle(HttpListenerContext ctx)
    {
        try
        {
            var path = (ctx.Request.Url?.AbsolutePath ?? "/").TrimEnd('/');
            if (path.Length == 0) path = "/";
            var isPost = ctx.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase);

            switch (path)
            {
                case "/":
                    Write(ctx, "text/html; charset=utf-8", IndexHtml());
                    break;

                case "/api/live":
                    WriteJson(ctx, LivePayload());
                    break;

                case "/api/day":
                    var d = ctx.Request.QueryString["d"];
                    var day = ParseDate(d) ?? DateTime.Today;
                    WriteJson(ctx, Payload.Build(day, SegmentsWithCurrent(day)));
                    break;

                case "/api/dates":
                    WriteJson(ctx, Store.AvailableDates());
                    break;

                case "/api/baseline":
                    WriteJson(ctx, Baseline.Get(30, DateTime.Today));
                    break;

                case "/api/pause" when isPost:
                    Recorder.Instance?.Pause();
                    WriteJson(ctx, new { paused = true });
                    break;

                case "/api/resume" when isPost:
                    Recorder.Instance?.Resume();
                    WriteJson(ctx, new { paused = false });
                    break;

                default:
                    ctx.Response.StatusCode = 404;
                    Write(ctx, "text/plain; charset=utf-8", "not found");
                    break;
            }
        }
        catch
        {
            // 单次请求失败不影响服务
        }
        finally
        {
            try { ctx.Response.Close(); } catch { }
        }
    }

    private static void Write(HttpListenerContext ctx, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.ContentType = contentType;
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.Headers["Cache-Control"] = "no-store";
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteJson(HttpListenerContext ctx, object data)
        => Write(ctx, "application/json; charset=utf-8", JsonSerializer.Serialize(data, JsonOpts));

    private static object LivePayload()
    {
        var today = DateTime.Today;
        var payload = Payload.Build(today, SegmentsWithCurrent(today));
        var rec = Recorder.Instance;
        var cur = rec?.CurrentSegment;
        var last = rec?.LastSnapshot ?? default;

        var proc = cur?.Proc ?? last.Proc ?? "";
        var title = cur?.Title ?? last.Title ?? "";
        var cls = proc.Length > 0 ? Classify.Of(proc, title) : default;

        // 大标题显示站点（知乎 / GitHub / Cursor），比只显示 "Edge" 有用
        var label = !string.IsNullOrWhiteSpace(cls.Site)
            ? cls.Site
            : Store.FriendlyName(proc);

        return new
        {
            now = DateTime.Now.ToString("HH:mm:ss"),
            active = cur is not null,
            paused = rec?.Paused ?? false,
            label,
            app = cls.App ?? "",
            group = cls.Group ?? "",
            title,
            sinceSec = cur is not null ? Math.Round((DateTime.Now - cur.Start).TotalSeconds, 1) : 0,
            todayTotal = payload.totalSec,
            coverage = payload.coverage,
            switches = payload.switches,
            top = payload.sites.Take(6).Select(s => new { l = s.n, c = s.c, sec = s.sec, pct = s.pct, sw = 0 }),
        };
    }

    private static List<Segment> SegmentsWithCurrent(DateTime day)
    {
        var segs = Store.Load(day);
        var cur = Recorder.Instance?.CurrentSegment;
        if (cur is not null && cur.Start.Date == day.Date && DateTime.Now > cur.Start)
        {
            segs.Add(new Segment
            {
                Proc = cur.Proc,
                Title = cur.Title,
                Start = cur.Start,
                End = DateTime.Now,
            });
        }
        return segs;
    }

    private static DateTime? ParseDate(string? s)
        => DateTime.TryParseExact(s ?? "", "yyyy-MM-dd", null,
            System.Globalization.DateTimeStyles.None, out var d) ? d : null;

    private static int FindFreePort(int start)
    {
        for (var p = start; p < start + 30; p++)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, p);
                listener.Start();
                listener.Stop();
                return p;
            }
            catch
            {
                // 端口占用，试下一个
            }
        }
        return start;
    }

    private static string IndexHtml()
    {
        if (_indexHtml is not null) return _indexHtml;

        var html = Read("AppUsage.Web.index.html");
        var js = Read("AppUsage.Web.render.js");
        _indexHtml = html.Replace("<!--RENDER_JS-->", "<script>\n" + js + "\n</script>");
        return _indexHtml;
    }

    private static string Read(string resource)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(resource);
        if (stream is null) return $"<h1>缺少嵌入资源 {resource}</h1>";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
