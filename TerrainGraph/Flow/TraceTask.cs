using System.Collections.Generic;

namespace TerrainGraph.Flow;

internal readonly struct TraceTask
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
    public readonly IEnumerable<TraceCollision> simulated;

    /// <summary>
    /// The additional path length to trace at the head end of the segment.
    /// </summary>
    public readonly double marginHead;

    /// <summary>
    /// The additional path length to trace at the tail end of the segment.
    /// </summary>
    public readonly double marginTail;

    /// <summary>
    /// Whether any previous segment has any frames fully within the bounds of the outer grid.
    /// </summary>
    public readonly bool everInBounds;

    public TraceTask(
        Path.Segment segment, TraceFrame baseFrame, IEnumerable<TraceCollision> simulated,
        double marginHead, double marginTail, bool everInBounds)
    {
        this.segment = segment;
        this.baseFrame = baseFrame;
        this.simulated = simulated;
        this.marginHead = marginHead;
        this.marginTail = marginTail;
        this.everInBounds = everInBounds;
    }
}
