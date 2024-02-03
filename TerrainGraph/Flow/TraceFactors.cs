using TerrainGraph.Util;

namespace TerrainGraph.Flow;

[HotSwappable]
internal readonly struct TraceFactors
{
    /// <summary>
    /// Local width multiplier at the current frame position.
    /// </summary>
    public readonly double width = 1;

    /// <summary>
    /// Local speed multiplier at the current frame position.
    /// </summary>
    public readonly double speed = 1;

    /// <summary>
    /// Local density multiplier at the current frame position.
    /// </summary>
    public readonly double density = 1;

    /// <summary>
    /// Scalar applied to all local multipliers.
    /// </summary>
    public readonly double scalar = 1;

    public TraceFactors() {}

    public TraceFactors(Path.Segment segment, Vector2d pos, double dist)
    {
        width = segment.TraceParams.WidthGrid?.ValueAt(pos) ?? 1;
        speed = segment.TraceParams.SpeedGrid?.ValueAt(pos) ?? 1;
        density = segment.TraceParams.DensityGrid?.ValueAt(pos) ?? 1;

        var progress = segment.Length <= 0 ? 0 : (dist / segment.Length).InRange01();

        scalar = 1 - segment.LocalStabilityAt(progress);
    }

    public override string ToString() =>
        $"{nameof(width)}: {width}, " +
        $"{nameof(speed)}: {speed}, " +
        $"{nameof(density)}: {density}, " +
        $"{nameof(scalar)}: {scalar}";
}
