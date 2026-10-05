using ZipDrop.Core.Gestures;

namespace ZipDrop.Core.Tests;

/// <summary>
/// The detector is fed synthetic cursor paths sampled every 8 ms (~125 Hz,
/// a typical mouse report rate), so the math is tested without any Windows hook.
/// </summary>
public class ShakeDetectorTests
{
    private const int SampleMs = 8;

    /// <summary>Linear path through waypoints, each leg lasting legMs.</summary>
    private static IEnumerable<(double X, double Y, long T)> Path(long startMs, int legMs, params (double X, double Y)[] points)
    {
        long t = startMs;
        yield return (points[0].X, points[0].Y, t);
        for (var i = 1; i < points.Length; i++)
        {
            var (x0, y0) = points[i - 1];
            var (x1, y1) = points[i];
            var steps = Math.Max(1, legMs / SampleMs);
            for (var s = 1; s <= steps; s++)
            {
                t += SampleMs;
                yield return (x0 + (x1 - x0) * s / steps, y0 + (y1 - y0) * s / steps, t);
            }
        }
    }

    private static int CountTriggers(ShakeDetector d, IEnumerable<(double X, double Y, long T)> samples) =>
        samples.Count(s => d.Feed(s.X, s.Y, s.T));

    private static (double, double)[] Zigzag(double amplitude, int swings, double y = 500, double drift = 0)
    {
        var pts = new List<(double, double)> { (500, y) };
        for (var i = 0; i < swings; i++)
            pts.Add((500 + (i % 2 == 0 ? amplitude : 0), y + drift * (i + 1)));
        return pts.ToArray();
    }

    [Fact]
    public void Quick_horizontal_shake_triggers_once()
    {
        var d = new ShakeDetector();
        Assert.Equal(1, CountTriggers(d, Path(0, 90, Zigzag(80, 5))));
    }

    [Fact]
    public void Trigger_happens_mid_gesture_not_after_it()
    {
        var d = new ShakeDetector(new ShakeOptions { RequiredSwings = 4 });
        long? firedAt = null;
        foreach (var s in Path(0, 90, Zigzag(80, 6)))
            if (d.Feed(s.X, s.Y, s.T)) { firedAt = s.T; break; }

        Assert.NotNull(firedAt);
        Assert.True(firedAt < 6 * 90, $"fired at {firedAt}ms");
    }

    [Fact]
    public void Straight_drag_does_not_trigger()
    {
        var d = new ShakeDetector();
        Assert.Equal(0, CountTriggers(d, Path(0, 600, (0, 0), (1200, 300), (1500, 900))));
    }

    [Fact]
    public void Slow_back_and_forth_does_not_trigger()
    {
        var d = new ShakeDetector();
        // 80px per 600ms = 133 px/s, below MinSwingSpeed, and too slow for the window.
        Assert.Equal(0, CountTriggers(d, Path(0, 600, Zigzag(80, 6))));
    }

    [Fact]
    public void Tiny_jitter_does_not_trigger()
    {
        var d = new ShakeDetector();
        Assert.Equal(0, CountTriggers(d, Path(0, 30, Zigzag(15, 12))));
    }

    [Fact]
    public void Vertical_shake_does_not_trigger()
    {
        var d = new ShakeDetector();
        var pts = new[] { (500.0, 500.0), (500, 600), (500, 500), (500, 600), (500, 500), (500, 600) };
        Assert.Equal(0, CountTriggers(d, Path(0, 80, pts)));
    }

    [Fact]
    public void Diagonal_zigzag_with_large_vertical_travel_does_not_trigger()
    {
        var d = new ShakeDetector();
        // Each swing moves 60px horizontally but 80px vertically (scrolling a selection, drawing).
        Assert.Equal(0, CountTriggers(d, Path(0, 80, Zigzag(60, 6, drift: 80))));
    }

    [Fact]
    public void Too_few_swings_do_not_trigger()
    {
        var d = new ShakeDetector(new ShakeOptions { RequiredSwings = 4 });
        Assert.Equal(0, CountTriggers(d, Path(0, 90, Zigzag(80, 3))));
    }

    [Fact]
    public void Swings_spread_over_a_long_time_do_not_trigger()
    {
        var d = new ShakeDetector(new ShakeOptions { RequiredSwings = 4, TimeWindowMs = 750 });
        // Fast swings, but with pauses between them so the four never fit in the window.
        var samples = new List<(double, double, long)>();
        long t = 0;
        double x = 500;
        for (var i = 0; i < 6; i++)
        {
            var target = i % 2 == 0 ? 580 : 500;
            samples.AddRange(Path(t, 90, (x, 500), (target, 500)));
            t += 90 + 400; // pause at the extreme
            x = target;
            samples.Add((x, 500, t));
        }
        Assert.Equal(0, CountTriggers(d, samples));
    }

    [Fact]
    public void Cooldown_prevents_double_trigger_from_one_long_shake()
    {
        var d = new ShakeDetector(new ShakeOptions { CooldownMs = 1200 });
        // 12 swings * 90ms = 1080ms of continuous shaking: only one trigger.
        Assert.Equal(1, CountTriggers(d, Path(0, 90, Zigzag(80, 12))));
    }

    [Fact]
    public void Can_trigger_again_after_cooldown()
    {
        var d = new ShakeDetector(new ShakeOptions { CooldownMs = 500 });
        var first = CountTriggers(d, Path(0, 90, Zigzag(80, 5)));
        var second = CountTriggers(d, Path(3000, 90, Zigzag(80, 5)));
        Assert.Equal(1, first);
        Assert.Equal(1, second);
    }

    [Fact]
    public void Reset_discards_partial_gesture()
    {
        var d = new ShakeDetector(new ShakeOptions { RequiredSwings = 4 });
        CountTriggers(d, Path(0, 90, Zigzag(80, 3)));
        d.Reset();
        // Two more swings would complete it without the reset; with reset it must not.
        Assert.Equal(0, CountTriggers(d, Path(300, 90, (500, 500), (580, 500), (500, 500))));
    }

    [Fact]
    public void Drag_then_shake_triggers()
    {
        var d = new ShakeDetector();
        var samples = Path(0, 400, (0, 0), (600, 400))
            .Concat(Path(408, 90, (600, 400), (680, 400), (600, 400), (680, 400), (600, 400), (680, 400)));
        Assert.Equal(1, CountTriggers(d, samples));
    }

    [Theory]
    [InlineData(ShakeSensitivity.Low, 40, 0)]   // small shake ignored at low sensitivity
    [InlineData(ShakeSensitivity.High, 40, 1)]  // ...but accepted at high sensitivity
    [InlineData(ShakeSensitivity.Low, 100, 1)]
    public void Sensitivity_presets(ShakeSensitivity sensitivity, double amplitude, int expected)
    {
        var d = new ShakeDetector(ShakeOptions.ForSensitivity(sensitivity));
        Assert.Equal(expected, CountTriggers(d, Path(0, 90, Zigzag(amplitude, 6))));
    }

    [Fact]
    public void Time_going_backwards_is_handled()
    {
        var d = new ShakeDetector();
        Assert.False(d.Feed(0, 0, 1000));
        Assert.False(d.Feed(50, 0, 500));
        Assert.False(d.Feed(100, 0, 520));
    }
}
