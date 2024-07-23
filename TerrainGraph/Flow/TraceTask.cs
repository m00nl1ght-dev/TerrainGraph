using System.Collections.Generic;
using System.Linq;

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
    /// The task for the first segment of the current branch of the path, or the root segment.
    /// </summary>
    public readonly TraceTask branchParent;

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
    /// Whether any previous segment has any frames fully within the bounds of the outer grid.
    /// </summary>
    public readonly bool everInBounds;

    /// <summary>
    /// Collisions with other path segments to be simulated, may be null if there are none.
    /// </summary>
    internal readonly IEnumerable<TraceCollision> simulated;

    public double WidthAt(double dist) => baseFrame.width * segment.RelWidth - dist * segment.TraceParams.WidthLoss;
    public double DensityAt(double dist) => baseFrame.density * segment.RelDensity - dist * segment.TraceParams.DensityLoss;
    public double SpeedAt(double dist) => baseFrame.speed * segment.RelSpeed - dist * segment.TraceParams.SpeedLoss;

    public double TurnLockRight => baseFrame.width * segment.Siblings().Where(b => b.RelShift < segment.RelShift).Sum(b => b.RelWidth);
    public double TurnLockLeft => baseFrame.width * segment.Siblings().Where(b => b.RelShift > segment.RelShift).Sum(b => b.RelWidth);

    internal TraceTask(
        Path.Segment segment, TraceFrame baseFrame, TraceTask branchParent, IEnumerable<TraceCollision> simulated,
        double marginHead, double marginTail, double distFromRoot, bool everInBounds)
    {
        this.segment = segment;
        this.baseFrame = baseFrame;
        this.simulated = simulated;
        this.marginHead = marginHead;
        this.marginTail = marginTail;
        this.distFromRoot = distFromRoot;
        this.branchParent = branchParent ?? this;
        this.everInBounds = everInBounds;
    }
}
