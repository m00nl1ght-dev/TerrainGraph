using System.Collections.Generic;
using System.Linq;
using TerrainGraph.Util;
using static TerrainGraph.Flow.Path;

namespace TerrainGraph.Flow;

internal class TraceCollision
{
    /// <summary>
    /// Task of the first segment involved in the collision, the one that was actively being traced.
    /// </summary>
    public TraceTask taskA;

    /// <summary>
    /// Task of the second segment involved in the collision. Will be the same as activeSegment if it collided with itself.
    /// </summary>
    public TraceTask taskB;

    /// <summary>
    /// The position at which the collision occured.
    /// </summary>
    public Vector2d position;

    /// <summary>
    /// The frame progress in the first segment at which the collision occured.
    /// </summary>
    public double progressA;

    /// <summary>
    /// The frame progress in the second segment at which the collision occured.
    /// </summary>
    public double progressB;

    /// <summary>
    /// The shift relative to the first segment at which the collision occured.
    /// </summary>
    public double shiftA;

    /// <summary>
    /// The shift relative to the second segment at which the collision occured.
    /// </summary>
    public double shiftB;

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
    public TraceFrame frameA => framesA[framesA.Count - 2];

    /// <summary>
    /// Current trace frame of the second segment at the time of the collision.
    /// </summary>
    public TraceFrame frameB => framesB[framesB.Count - 2];

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
        this.cyclic = taskA.segment.IsBranchOf(taskB.segment, true);
        this.hasMergeA = taskA.segment.AnyBranchesMatch(s => s.ParentCount > 1, false);
        this.hasMergeB = taskB.segment.AnyBranchesMatch(s => s.ParentCount > 1, false);
    }

    public bool Precedes(TraceCollision other)
    {
        if (taskA.segment.IsBranchOf(other.taskB.segment, true)) return false;
        if (taskB.segment.IsParentOf(other.taskA.segment, true)) return true;
        if (taskB.segment.IsParentOf(other.taskB.segment, false)) return true;
        if (!complete && !other.complete) return false;
        if (taskB.segment == other.taskB.segment && frameB.dist < other.frameB.dist) return true;
        return false;
    }

    public List<Segment> FindEnclosedSegments()
    {
        if (taskA.segment == taskB.segment || !complete) return [];

        var enclosed = new List<Segment>();

        var shift = Vector2d.PointToLineOrientation(frameB.pos, frameB.pos + frameB.normal, frameA.pos);

        var rhs = shift < 0 ? taskA.segment : taskB.segment;
        var lhs = shift < 0 ? taskB.segment : taskA.segment;

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
        $"{nameof(taskA)}: {taskA.segment.Id}, " +
        $"{nameof(taskB)}: {taskB.segment.Id}, " +
        $"{nameof(frameA)}: {(framesA == null ? "?" : frameA)}, " +
        $"{nameof(frameB)}: {(framesB == null ? "?" : frameB)}, " +
        $"{nameof(position)}: {position}";
}
