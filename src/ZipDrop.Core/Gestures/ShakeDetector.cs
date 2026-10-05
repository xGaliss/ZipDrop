namespace ZipDrop.Core.Gestures;

/// <summary>
/// Pure horizontal-shake recognizer. Feed it cursor samples (logical px, ms);
/// it returns true exactly once per recognized shake. No Windows dependencies,
/// so it can be unit-tested with synthetic cursor paths.
///
/// Model: the horizontal motion is split into "swings" at every direction reversal
/// (ignoring reversals smaller than <see cref="ShakeOptions.ReversalTolerance"/>).
/// A shake is N consecutive swings, each long enough, fast enough and mostly horizontal,
/// all inside the time window. The swing in progress counts as soon as it qualifies,
/// so the shake fires mid-gesture instead of waiting for another reversal.
/// </summary>
public sealed class ShakeDetector
{
    private readonly record struct Swing(double Distance, double VerticalSpan, long StartMs, long EndMs);

    private const int MaxRememberedSwings = 16;

    private readonly List<Swing> _completed = new();
    private bool _hasLast;
    private long _lastMs;
    private long _lastTriggerMs = long.MinValue / 2;

    // Swing in progress.
    private int _dir;           // -1 left, +1 right, 0 unknown yet
    private double _startX;
    private long _startMs;
    private double _extremeX;
    private long _extremeMs;
    private double _minY, _maxY;

    public ShakeDetector(ShakeOptions? options = null) => Options = options ?? new ShakeOptions();

    public ShakeOptions Options { get; set; }

    /// <summary>Forget the current gesture (call on button release).</summary>
    public void Reset()
    {
        _completed.Clear();
        _hasLast = false;
        _dir = 0;
    }

    public bool Feed(double x, double y, long timeMs)
    {
        var o = Options;

        if (timeMs - _lastTriggerMs < o.CooldownMs)
        {
            _hasLast = false; // restart cleanly once the cooldown ends
            return false;
        }

        if (!_hasLast || timeMs - _lastMs > o.TimeWindowMs || timeMs < _lastMs)
        {
            StartFresh(x, y, timeMs);
            return false;
        }
        _lastMs = timeMs;

        if (_dir == 0)
        {
            if (Math.Abs(x - _startX) >= o.ReversalTolerance)
            {
                _dir = Math.Sign(x - _startX);
                _extremeX = x;
                _extremeMs = timeMs;
            }
        }
        else if ((x - _extremeX) * _dir > 0)
        {
            _extremeX = x;
            _extremeMs = timeMs;
        }
        else if (Math.Abs(x - _extremeX) > o.ReversalTolerance)
        {
            // Reversal: close the current swing at its extreme, start the opposite one there.
            _completed.Add(new Swing(Math.Abs(_extremeX - _startX), _maxY - _minY, _startMs, _extremeMs));
            if (_completed.Count > MaxRememberedSwings) _completed.RemoveAt(0);

            _dir = -_dir;
            _startX = _extremeX;
            _startMs = _extremeMs;
            _extremeX = x;
            _extremeMs = timeMs;
            _minY = _maxY = y;
        }

        _minY = Math.Min(_minY, y);
        _maxY = Math.Max(_maxY, y);

        if (!IsShake(timeMs)) return false;

        _lastTriggerMs = timeMs;
        Reset();
        return true;
    }

    private bool IsShake(long nowMs)
    {
        var o = Options;
        var count = 0;
        long windowStart = nowMs - o.TimeWindowMs;

        if (_dir != 0)
        {
            var current = new Swing(Math.Abs(_extremeX - _startX), _maxY - _minY, _startMs, _extremeMs);
            if (Qualifies(current, windowStart)) count++;
            // An in-progress swing that does not qualify yet does not break the chain.
        }

        for (var i = _completed.Count - 1; i >= 0 && count < o.RequiredSwings; i--)
        {
            if (!Qualifies(_completed[i], windowStart)) break;
            count++;
        }

        return count >= o.RequiredSwings;
    }

    private bool Qualifies(Swing s, long windowStart)
    {
        var o = Options;
        if (s.StartMs < windowStart) return false;
        if (s.Distance < o.MinSwingDistance) return false;
        if (s.VerticalSpan > s.Distance * o.MaxVerticalRatio) return false;
        var durationMs = Math.Max(1, s.EndMs - s.StartMs);
        var speed = s.Distance / durationMs * 1000.0;
        return speed >= o.MinSwingSpeed;
    }

    private void StartFresh(double x, double y, long timeMs)
    {
        _completed.Clear();
        _hasLast = true;
        _lastMs = timeMs;
        _dir = 0;
        _startX = _extremeX = x;
        _startMs = _extremeMs = timeMs;
        _minY = _maxY = y;
    }
}
