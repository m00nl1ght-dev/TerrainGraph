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

    public object ContextA = CurrentContextA;
    public object ContextB = CurrentContextB;

    public static object CurrentContextA = null;
    public static object CurrentContextB = null;

    public bool IsPointAt(Vector2d p) => p == Pos1 && Pos1 == Pos2;

    public Vector2d MapPos1 => Tracer == null ? Pos1 : Pos1 - Tracer.GridMargin;
    public Vector2d MapPos2 => Tracer == null ? Pos2 : Pos2 - Tracer.GridMargin;

    public TraceDebugLine(PathTracer tracer, Vector2d pos1, int color = 0, int group = 0, string label = "") :
        this(tracer, pos1, pos1, color, group, label) {}

    public TraceDebugLine(PathTracer tracer, Vector2d pos1, Vector2d pos2, int color = 0, int group = 0, string label = "")
    {
        Pos1 = pos1;
        Pos2 = pos2;
        Tracer = tracer;
        Label = label;
        Group = group;
        Color = color;
    }
}
