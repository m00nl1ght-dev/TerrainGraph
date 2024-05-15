using TerrainGraph.Util;

namespace TerrainGraph.Flow;

[HotSwappable]
internal readonly struct TraceFactors
{
    /// <summary>
    /// Local multiplier for the left extent at the current frame position.
    /// </summary>
    public readonly double extentLeft = 1;

    /// <summary>
    /// Local multiplier for the right extent at the current frame position.
    /// </summary>
    public readonly double extentRight = 1;

    /// <summary>
    /// Local speed multiplier at the current frame position.
    /// </summary>
    public readonly double speed = 1;

    /// <summary>
    /// Local multiplier for the left density at the current frame position.
    /// </summary>
    public readonly double densityLeft = 1;

    /// <summary>
    /// Local multiplier for the right density at the current frame position.
    /// </summary>
    public readonly double densityRight = 1;

    /// <summary>
    /// Scalar applied to all local multipliers.
    /// </summary>
    public readonly double scalar = 1;

    public TraceFactors() {}

    public TraceFactors(Path.Segment segment, Vector2d pos, double dist)
    {
        extentLeft = segment.TraceParams.ExtentLeftGrid?.ValueAt(pos) ?? 1;
        extentRight = segment.TraceParams.ExtentRightGrid?.ValueAt(pos) ?? 1;
        speed = segment.TraceParams.SpeedGrid?.ValueAt(pos) ?? 1;
        densityLeft = segment.TraceParams.DensityLeftGrid?.ValueAt(pos) ?? 1;
        densityRight = segment.TraceParams.DensityRightGrid?.ValueAt(pos) ?? 1;

        var progress = segment.Length <= 0 ? 0 : (dist / segment.Length).InRange01();

        scalar = 1 - segment.LocalStabilityAt(progress);
    }

    public override string ToString() =>
        $"{nameof(extentLeft)}: {extentLeft}, " +
        $"{nameof(extentRight)}: {extentRight}, " +
        $"{nameof(speed)}: {speed}, " +
        $"{nameof(densityLeft)}: {densityLeft}, " +
        $"{nameof(densityRight)}: {densityRight}, " +
        $"{nameof(scalar)}: {scalar}";
}
