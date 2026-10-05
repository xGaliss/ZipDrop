namespace ZipDrop.Core.Gestures;

public enum ShakeSensitivity
{
    Low = 0,
    Medium = 1,
    High = 2,
}

/// <summary>
/// Every tunable of the shake detector in one place. Distances are in logical
/// pixels (96 DPI); the caller converts physical cursor coordinates first.
/// </summary>
public sealed record ShakeOptions
{
    /// <summary>A swing must travel at least this far horizontally to count.</summary>
    public double MinSwingDistance { get; init; } = 40;

    /// <summary>Number of consecutive alternating swings needed (4 = ← → ← →).</summary>
    public int RequiredSwings { get; init; } = 4;

    /// <summary>All required swings must happen inside this window (ms).</summary>
    public long TimeWindowMs { get; init; } = 750;

    /// <summary>Average horizontal speed of each swing (logical px per second).</summary>
    public double MinSwingSpeed { get; init; } = 250;

    /// <summary>Max vertical travel of a swing relative to its horizontal travel. Rejects circles/diagonals.</summary>
    public double MaxVerticalRatio { get; init; } = 0.8;

    /// <summary>Moving back less than this from the furthest point is noise, not a reversal.</summary>
    public double ReversalTolerance { get; init; } = 8;

    /// <summary>After a trigger, ignore everything for this long (ms).</summary>
    public long CooldownMs { get; init; } = 1200;

    /// <summary>
    /// Movement from the button-down point (logical px) before we consider a drag started.
    /// Windows' own SM_CXDRAG default is 4px; we are stricter to ignore click-jitter.
    /// </summary>
    public double DragStartDistance { get; init; } = 10;

    public static ShakeOptions ForSensitivity(ShakeSensitivity sensitivity) => sensitivity switch
    {
        ShakeSensitivity.Low => new ShakeOptions { MinSwingDistance = 60, RequiredSwings = 5, TimeWindowMs = 900, MinSwingSpeed = 320 },
        ShakeSensitivity.High => new ShakeOptions { MinSwingDistance = 28, RequiredSwings = 3, TimeWindowMs = 650, MinSwingSpeed = 200 },
        _ => new ShakeOptions(),
    };
}
