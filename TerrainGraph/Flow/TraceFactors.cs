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

    public TraceFactors(PathTracer tracer, TraceTask task, Vector2d pos, double dist)
    {
        var traceParams = task.segment.TraceParams;

        extentLeft = traceParams.ExtentLeft?.ValueFor(tracer, task, pos, dist) ?? 1;
        extentRight = traceParams.ExtentRight?.ValueFor(tracer, task, pos, dist) ?? 1;
        densityLeft = traceParams.DensityLeft?.ValueFor(tracer, task, pos, dist) ?? 1;
        densityRight = traceParams.DensityRight?.ValueFor(tracer, task, pos, dist) ?? 1;
        speed = traceParams.Speed?.ValueFor(tracer, task, pos, dist) ?? 1;

        var progress = task.segment.Length <= 0 ? 0 : (dist / task.segment.Length).InRange01();

        scalar = 1 - task.segment.LocalStabilityAt(progress);
    }

    public override string ToString() =>
        $"{nameof(extentLeft)}: {extentLeft:F2}, " +
        $"{nameof(extentRight)}: {extentRight:F2}, " +
        $"{nameof(speed)}: {speed:F2}, " +
        $"{nameof(densityLeft)}: {densityLeft:F2}, " +
        $"{nameof(densityRight)}: {densityRight:F2}, " +
        $"{nameof(scalar)}: {scalar:F2}";
}
