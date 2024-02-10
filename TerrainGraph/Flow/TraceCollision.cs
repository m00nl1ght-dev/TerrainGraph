using System.Collections.Generic;
using System.Linq;
using TerrainGraph.Util;
using static TerrainGraph.Flow.Path;

namespace TerrainGraph.Flow;

[HotSwappable]
internal class TraceCollision
{
    /// <summary>
    /// First segment involved in the collision, the one that was actively being traced.
    /// </summary>
    public Segment segmentA;

    /// <summary>
    /// Second segment involved in the collision. May be the same as activeSegment if it collided with itself.
    /// </summary>
    public Segment segmentB;

    /// <summary>
    /// The position at which the collision occured.
    /// </summary>
    public Vector2d position;

    /// <summary>
    /// Trace frames of the first segment.
    /// </summary>
    public List<TraceFrame> framesA;

    /// <summary>
    /// Trace frames of the second segment.
    /// </summary>
    public List<TraceFrame> framesB;

    /// <summary>
    /// Current trace frame of the first segment at the time of the collision.
    /// </summary>
    public TraceFrame frameA => framesA[framesA.Count - 1];

    /// <summary>
    /// Current trace frame of the second segment at the time of the collision.
    /// </summary>
    public TraceFrame frameB => framesB[framesB.Count - 1];

    /// <summary>
    /// Whether the trace frames of both involved segments are available.
    /// </summary>
    public bool complete => framesA != null && framesB != null;

    /// <summary>
    /// Whether the passive segment is the same or any direct or indirect parent of the active segment.
    /// </summary>
    public bool cyclic;

    /// <summary>
    /// Whether any branches of the active segment have multiple parent segments.
    /// </summary>
    public bool hasMergeA;

    /// <summary>
    /// Whether any branches of the passive segment have multiple parent segments.
    /// </summary>
    public bool hasMergeB;

    public void Analyze()
    {
        this.cyclic = segmentA.IsBranchOf(segmentB, true);
        this.hasMergeA = segmentA.AnyBranchesMatch(s => s.ParentCount > 1, false);
        this.hasMergeB = segmentB.AnyBranchesMatch(s => s.ParentCount > 1, false);
    }

    public bool Precedes(TraceCollision other)
    {
        if (segmentA.IsBranchOf(other.segmentB, true)) return false;
        if (segmentB.IsParentOf(other.segmentA, true)) return true;
        if (segmentB.IsParentOf(other.segmentB, false)) return true;
        if (!complete && !other.complete) return false;
        if (segmentB == other.segmentB && frameB.dist < other.frameB.dist) return true;
        return false;
    }

    public List<Segment> FindEnclosedSegments()
    {
        if (segmentA == segmentB) return [];

        var enclosed = new List<Segment>();

        var shift = Vector2d.PointToLineOrientation(frameB.pos, frameB.pos + frameB.normal, frameA.pos);

        var rhs = shift < 0 ? segmentA : segmentB;
        var lhs = shift < 0 ? segmentB : segmentA;

        if (!TraverseEnclosed(rhs, lhs, enclosed, true)) return [];
        if (!TraverseEnclosed(lhs, rhs, enclosed, false)) return [];

        return enclosed;
    }

    private bool TraverseEnclosed(Segment start, Segment other, List<Segment> enclosed, bool reversed)
    {
        var current = start;

        bool foundOther = false;

        while (current.ParentCount > 0 && !foundOther)
        {
            var parent = reversed ? current.Parents.Last() : current.Parents.First();

            if (parent == start) break; // protection against cyclic graphs

            if (parent.BranchCount > 1)
            {
                foreach (var branch in reversed ? parent.Branches.Reverse() : parent.Branches)
                {
                    if (branch == current) break;

                    var branches = branch.ConnectedSegments(true, false);

                    if (branches.Contains(other))
                    {
                        foundOther = true;
                    }
                    else
                    {
                        enclosed.AddRange(branches);
                    }
                }
            }

            current = parent;
        }

        return foundOther;
    }

    public override string ToString() =>
        $"{nameof(segmentA)}: {segmentA.Id}, " +
        $"{nameof(segmentB)}: {segmentB.Id}, " +
        $"{nameof(frameA)}: {(framesA == null ? "?" : frameA)}, " +
        $"{nameof(frameB)}: {(framesB == null ? "?" : frameB)}, " +
        $"{nameof(position)}: {position}";
}
