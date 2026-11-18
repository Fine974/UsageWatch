namespace AppUsage;

internal sealed class Recorder
{
    private readonly int _interval;
    private readonly int _idleLimit;
    private readonly bool _recordTitle;

    private Segment? _current;
    private DateTime _lastBeat = DateTime.MinValue;
    private volatile bool _paused;
    private Snapshot _last = default;

    internal static Recorder? Instance { get; private set; }

    internal Recorder(int interval, int idleLimit, bool recordTitle)
    {
        _interval = Math.Max(1, interval);
        _idleLimit = Math.Max(30, idleLimit);
        _recordTitle = recordTitle;
        Instance = this;
    }

    internal bool Paused => _paused;

    internal Segment? CurrentSegment => _current;

    internal Snapshot LastSnapshot => _last;

    internal void Pause()
    {
        Flush(DateTime.Now);
        _paused = true;
    }

    internal void Resume() => _paused = false;

    internal void Start(CancellationToken ct)
    {
        Recover();
        _ = Task.Run(() => LoopAsync(ct), ct);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_paused)
                {
                    if (_current is not null) Flush(DateTime.Now);
                }
                else
                {
                    Tick();
                }
            }
            catch
            {
                // 单次采样失败不应终止采集
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_interval), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Flush(DateTime.Now);
    }

    private void Tick()
    {
        var s = Collector.Take();
        _last = s;

        var active = !s.IsLockScreen && s.HasWindow && s.IdleSeconds < _idleLimit && s.Proc.Length > 0;
        var now = DateTime.Now;

        if (!active)
        {
            // 空闲触发的离开：真正最后一次操作在 now - IdleSeconds，
            // 若直接按 now 结算，每次离开都会多记最多 _idleLimit 秒（实测锁屏一段多记 10 分钟）。
            var endAt = now;
            if (!s.IsLockScreen && s.IdleSeconds > 0)
            {
                var lastInput = now.AddSeconds(-s.IdleSeconds);
                if (_current is not null && lastInput > _current.Start) endAt = lastInput;
            }

            Flush(endAt);
            return;
        }

        if (_current is null || !_current.Proc.Equals(s.Proc, StringComparison.OrdinalIgnoreCase))
        {
            Flush(now);
            _current = new Segment
            {
                Proc = s.Proc,
                Title = _recordTitle ? s.Title : "",
                Start = now,
            };
            Store.SaveState(_current.Proc, _current.Start, now);
            _lastBeat = now;
            return;
        }

        if (_recordTitle && !string.IsNullOrWhiteSpace(s.Title)) _current.Title = s.Title;

        if ((now - _lastBeat).TotalSeconds >= 30)
        {
            Store.SaveState(_current.Proc, _current.Start, now);
            _lastBeat = now;
        }
    }

    private void Flush(DateTime? at = null)
    {
        if (_current is null) return;
        _current.End = at ?? DateTime.Now;
        if (_current.End > _current.Start) Store.AppendSegment(_current);
        _current = null;
        Store.ClearState();
    }

    private void Recover()
    {
        var st = Store.LoadState();
        if (st is null) return;

        var (proc, start, beat) = st.Value;
        Store.ClearState();
        if (beat <= start) return;

        var seg = new Segment { Proc = proc, Start = start, End = beat, Title = "" };
        if (seg.Seconds < 5) return;

        Store.AppendSegment(seg);
    }
}
