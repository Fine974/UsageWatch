using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace AppUsage;

internal sealed class TrayApp : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private readonly Recorder _rec;
    private readonly CancellationTokenSource _cts;
    private readonly int _port;
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _tick;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly Icon _iconActive;
    private readonly Icon _iconIdle;
    private bool _paused;

    internal TrayApp(Recorder rec, CancellationTokenSource cts, int port)
    {
        _rec = rec;
        _cts = cts;
        _port = port;

        _iconActive = MakeIcon(Color.FromArgb(0x4F, 0x9C, 0xF9));
        _iconIdle = MakeIcon(Color.FromArgb(0x7A, 0x83, 0x96));

        var menu = new ContextMenuStrip();
        menu.Items.Add("打开监控页", null, (_, _) => OpenPage());
        menu.Items.Add("保存今日报告", null, (_, _) => SaveReport());
        menu.Items.Add(new ToolStripSeparator());
        _pauseItem = new ToolStripMenuItem("暂停采集", null, (_, _) => TogglePause());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Shutdown());

        _tray = new NotifyIcon
        {
            Icon = _iconActive,
            Text = "usagewatch",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => OpenPage();

        _tick = new System.Windows.Forms.Timer { Interval = 2000 };
        _tick.Tick += (_, _) => RefreshTray();
        _tick.Start();

        RefreshTray();
    }

    internal void Announce()
    {
        try
        {
            _tray.ShowBalloonTip(4000, "usagewatch 已在后台记录",
                $"打开 {WebHost.Url} 查看实时监控", ToolTipIcon.None);
        }
        catch
        {
            // 通知中心被禁用时忽略
        }
    }

    private void RefreshTray()
    {
        var cur = _rec.CurrentSegment;
        var paused = _rec.Paused;

        if (paused != _paused)
        {
            _paused = paused;
            _tray.Icon = paused ? _iconIdle : _iconActive;
            _pauseItem.Text = paused ? "恢复采集" : "暂停采集";
        }

        string text;
        if (paused) text = "usagewatch · 已暂停";
        else if (cur is null) text = "usagewatch · 空闲";
        else
        {
            var span = DateTime.Now - cur.Start;
            text = $"usagewatch · {Store.FriendlyName(cur.Proc)} {span.Hours:D2}:{span.Minutes:D2}";
        }

        _tray.Text = text.Length > 62 ? text[..62] : text;
    }

    private void TogglePause()
    {
        if (_rec.Paused) _rec.Resume();
        else _rec.Pause();
        RefreshTray();
    }

    private void OpenPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(WebHost.Url) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private void SaveReport()
    {
        try
        {
            var day = DateTime.Today;
            var payload = Payload.Build(day, Store.Load(day));
            var html = Dashboard.BuildHtml(day, payload);
            var path = Path.Combine(Store.Root, $"report-{day:yyyy-MM-dd}.html");
            File.WriteAllText(path, html, new UTF8Encoding(false));
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private void Shutdown()
    {
        _tick.Stop();
        _tray.Visible = false;
        try { _cts.Cancel(); } catch { }
        Application.Exit();
    }

    public void Dispose()
    {
        _tick?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _iconActive?.Dispose();
        _iconIdle?.Dispose();
    }

    private static Icon MakeIcon(Color fg)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var bg = new SolidBrush(Color.FromArgb(0x12, 0x16, 0x1F));
            g.FillEllipse(bg, 0, 0, 31, 31);

            using var ring = new Pen(fg, 2.0f);
            g.DrawEllipse(ring, 3, 3, 25, 25);

            using var bar = new SolidBrush(fg);
            g.FillRectangle(bar, 9, 16, 3, 8);
            g.FillRectangle(bar, 14, 11, 3, 13);
            g.FillRectangle(bar, 19, 14, 3, 10);
        }

        var handle = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(handle);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }
}
