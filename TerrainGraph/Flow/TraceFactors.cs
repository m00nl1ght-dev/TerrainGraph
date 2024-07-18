using TerrainGraph.Util;

namespace TerrainGraph.Flow;

public readonly struct TraceFactors
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

    public TraceFactors(TraceTask task, Vector2d pos, double dist)
    {
        var traceParams = task.segment.TraceParams;

        extentLeft = traceParams.ExtentLeft?.ValueFor(task, pos, dist) ?? 1;
        extentRight = traceParams.ExtentRight?.ValueFor(task, pos, dist) ?? 1;
        densityLeft = traceParams.DensityLeft?.ValueFor(task, pos, dist) ?? 1;
        densityRight = traceParams.DensityRight?.ValueFor(task, pos, dist) ?? 1;
        speed = traceParams.Speed?.ValueFor(task, pos, dist) ?? 1;

        var progress = task.segment.Length <= 0 ? 0 : (dist / task.segment.Length).InRange01();

        scalar = 1 - task.segment.LocalStabilityAt(progress);
    }

    public override string ToString() =>
        $"{nameof(extentLeft)}: {extentLeft}, " +
        $"{nameof(extentRight)}: {extentRight}, " +
        $"{nameof(speed)}: {speed}, " +
        $"{nameof(densityLeft)}: {densityLeft}, " +
        $"{nameof(densityRight)}: {densityRight}, " +
        $"{nameof(scalar)}: {scalar}";
}
