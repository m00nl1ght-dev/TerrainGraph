namespace TerrainGraph.Flow;

internal class TraceResult
{
    /// <summary>
    /// The final trace frame that resulted from tracing the path segment.
    /// </summary>
    public readonly TraceFrame finalFrame;

    /// <summary>
    /// Information about a collision with another path segment, if any occured.
    /// </summary>
    public readonly TraceCollision collision;

    /// <summary>
    /// Whether this or any previous segment has any frames fully within the bounds of the outer grid.
    /// </summary>
    public readonly bool everInBounds;

    public TraceResult(TraceFrame finalFrame, bool everInBounds, TraceCollision collision = null)
    {
        this.finalFrame = finalFrame;
        this.everInBounds = everInBounds;
        this.collision = collision;
    }
}
