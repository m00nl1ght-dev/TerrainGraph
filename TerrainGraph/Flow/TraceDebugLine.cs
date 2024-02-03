using TerrainGraph.Util;

namespace TerrainGraph.Flow;

public class TraceDebugLine
{
    public readonly Vector2d Pos1;
    public readonly Vector2d Pos2;
    public readonly PathTracer Tracer;

    public string Label;
    public int Group;
    public int Color;

    public bool IsPointAt(Vector2d p) => p - Tracer.GridMargin == Pos1 && Pos1 == Pos2;

    public TraceDebugLine(PathTracer tracer, Vector2d pos1, int color = 0, int group = 0, string label = "") :
        this(tracer, pos1, pos1, color, group, label) {}

    public TraceDebugLine(PathTracer tracer, Vector2d pos1, Vector2d pos2, int color = 0, int group = 0, string label = "")
    {
        Pos1 = pos1 - tracer.GridMargin;
        Pos2 = pos2 - tracer.GridMargin;
        Tracer = tracer;
        Label = label;
        Group = group;
        Color = color;
    }
}
