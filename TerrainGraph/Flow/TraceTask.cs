using System.Collections.Generic;

namespace TerrainGraph.Flow;

public class TraceTask
{
    /// <summary>
    /// The path segment that this task should trace.
    /// </summary>
    public readonly Path.Segment segment;

    /// <summary>
    /// The trace frame containing the starting parameters for this path segment.
    /// </summary>
    public readonly TraceFrame baseFrame;

    /// <summary>
    /// Collisions with other path segments to be simulated, may be null if there are none.
    /// </summary>
    internal readonly IEnumerable<TraceCollision> simulated;

    /// <summary>
    /// The additional path length to trace at the head end of the segment.
    /// </summary>
    public readonly double marginHead;

    /// <summary>
    /// The additional path length to trace at the tail end of the segment.
    /// </summary>
    public readonly double marginTail;

    /// <summary>
    /// The total distance of the base frame from the origin of the path.
    /// </summary>
    public readonly double distFromRoot;

    /// <summary>
    /// The width of the last segment with stability 1, or the root segment.
    /// </summary>
    public readonly double lastStableWidth;

    /// <summary>
    /// Whether any previous segment has any frames fully within the bounds of the outer grid.
    /// </summary>
    public readonly bool everInBounds;

    public double WidthAt(double dist) => baseFrame.width * segment.RelWidth - dist * segment.TraceParams.WidthLoss;
    public double DensityAt(double dist) => baseFrame.density * segment.RelDensity - dist * segment.TraceParams.DensityLoss;
    public double SpeedAt(double dist) => baseFrame.speed * segment.RelSpeed - dist * segment.TraceParams.SpeedLoss;

    internal TraceTask(
        Path.Segment segment, TraceFrame baseFrame, IEnumerable<TraceCollision> simulated,
        double marginHead, double marginTail, double distFromRoot, double lastStableWidth, bool everInBounds)
    {
        this.segment = segment;
        this.baseFrame = baseFrame;
        this.simulated = simulated;
        this.marginHead = marginHead;
        this.marginTail = marginTail;
        this.distFromRoot = distFromRoot;
        this.lastStableWidth = lastStableWidth;
        this.everInBounds = everInBounds;
    }
}
