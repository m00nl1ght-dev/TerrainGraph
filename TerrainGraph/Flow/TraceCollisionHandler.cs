using System;
using System.Collections.Generic;
using System.Linq;
using TerrainGraph.Util;

namespace TerrainGraph.Flow;

[HotSwappable]
public class TraceCollisionHandler
{
    public int MaxDiversionPoints = 5;

    public double MergeValueDeltaLimit = 0.45;
    public double MergeOffsetDeltaLimit = 0.45;
    public double SimplificationLength = 10;
    public double DiversionMinLength = 5;
    public double StubBacktrackLength = 10;

    private readonly PathTracer _tracer;

    public TraceCollisionHandler(PathTracer tracer)
    {
        _tracer = tracer;
    }

    /// <summary>
    /// Rewrite path segments such that the earliest of the given collisions is avoided.
    /// </summary>
    /// <param name="collisions">list of collisions that should be considered</param>
    internal void HandleFirstCollision(List<TraceCollision> collisions)
    {
        TraceCollision first = null;

        foreach (var collision in collisions)
        {
            if (first == null || collision.Precedes(first))
            {
                first = collision;
            }
        }

        if (first != null)
        {
            HandleCollision(first);
        }
    }

    /// <summary>
    /// Rewrite path segments such that the given collision is avoided.
    /// </summary>
    internal void HandleCollision(TraceCollision c)
    {
        c.Analyze();

        if (!c.complete)
        {
            #if DEBUG
            PathTracer.DebugOutput($"Collision missing data: {c}");
            #endif

            Stub(c.segmentA, c.framesA, c.segmentB);
            return;
        }

        #if DEBUG
        PathTracer.DebugOutput($"Handling collision: {c}");
        #endif

        if (TryMerge(c))
        {
            #if DEBUG
            PathTracer.DebugOutput("Path merge was successful");
            #endif

            return;
        }

        var divCountA = c.segmentA.TraceParams.DiversionPoints?.Count ?? 0;
        var divCountB = c.segmentB.TraceParams.DiversionPoints?.Count ?? 0;

        var passiveFirst = divCountA == divCountB ? c.frameA.width > c.frameB.width : divCountA > divCountB;

        if (TryDivert(c, passiveFirst))
        {
            #if DEBUG
            PathTracer.DebugOutput("First path diversion attempt was successful");
            #endif

            return;
        }

        if (TryDivert(c, !passiveFirst))
        {
            #if DEBUG
            PathTracer.DebugOutput("Second path diversion attempt was successful");
            #endif

            return;
        }

        // TODO do simplification first again, but only if AdjustmentCount < diversion count?
        // nah, instead try fixing it through collision handling order (check for collisions that would be "enclosed" by a merge, and handle those first)

        passiveFirst = c.frameA.width > c.frameB.width;

        if (TrySimplify(c, passiveFirst))
        {
            #if DEBUG
            PathTracer.DebugOutput("First path simplification attempt was successful");
            #endif

            return;
        }

        if (TrySimplify(c, !passiveFirst))
        {
            #if DEBUG
            PathTracer.DebugOutput("Second path simplification attempt was successful");
            #endif

            return;
        }

        #if DEBUG
        PathTracer.DebugOutput("Could not avert the collision, stubbing instead");
        #endif

        if (c.hasMergeA == c.hasMergeB ? c.frameA.width <= c.frameB.width : c.hasMergeB)
        {
            Stub(c.segmentA, c.framesA, c.segmentB);
        }
        else
        {
            Stub(c.segmentB, c.framesB, c.segmentA);
        }
    }

    /// <summary>
    /// Attempt to merge the involved path segments in order to avoid the given collision.
    /// </summary>
    /// <returns>true if the segments were merged successfully, otherwise false</returns>
    private bool TryMerge(TraceCollision c)
    {
        if (c.cyclic || c.hasMergeA || c.hasMergeB) return false;
        if (c.segmentA.TraceParams.AvoidOverlap > 0) return false;

        var dot = Vector2d.Dot(c.frameA.normal, c.frameB.normal);
        var perpDot = Vector2d.PerpDot(c.frameA.normal, c.frameB.normal);

        var normal = dot > 0 || perpDot.Abs() > 0.05
            ? (c.frameA.normal * c.frameA.width + c.frameB.normal * c.frameB.width).Normalized
            : perpDot >= 0 ? c.frameA.normal.PerpCCW : c.frameA.normal.PerpCW;

        var shift = Vector2d.PointToLineOrientation(c.frameB.pos, c.frameB.pos + c.frameB.normal, c.frameA.pos);

        #if DEBUG
        PathTracer.DebugOutput($"Target direction for merge is {normal} with dot {dot} perpDot {perpDot}");
        #endif

        double arcA, arcB, ductA, ductB;
        TraceFrame frameA, frameB;
        ArcCalcResult result;

        int ptr = 0, fA = 0, fB = 0;

        do
        {
            frameA = c.framesA[c.framesA.Count - fA - 1];
            frameB = c.framesB[c.framesB.Count - fB - 1];

            #if DEBUG
            var debugPoint1 = PathTracer.DebugLines.FirstOrDefault(dl => dl.IsPointAt(frameA.pos));
            var debugPoint2 = PathTracer.DebugLines.FirstOrDefault(dl => dl.IsPointAt(frameB.pos));
            if (debugPoint1 == null) PathTracer.DebugLine(debugPoint1 = new TraceDebugLine(_tracer, frameA.pos, 0, 0, fA.ToString()));
            if (debugPoint2 == null) PathTracer.DebugLine(debugPoint2 = new TraceDebugLine(_tracer, frameB.pos, 0, 0, fB.ToString()));
            PathTracer.DebugOutput($"--> Attempting merge with {fA} | {fB} frames backtracked with A at {frameA.pos} and B at {frameB.pos}");
            #endif

            result = TryCalcArcs(
                c.segmentA, c.segmentB, normal, shift, ref frameA, ref frameB,
                out arcA, out arcB, out ductA, out ductB, 1
            );

            #if DEBUG
            debugPoint1.Label += "\n" + fB + " -> " + result;
            #endif

            if (result == ArcCalcResult.Success) break;

            result = TryCalcArcs(
                c.segmentB, c.segmentA, normal, -shift, ref frameB, ref frameA,
                out arcB, out arcA, out ductB, out ductA, 2
            );

            #if DEBUG
            debugPoint2.Label += "\n" + fA + " -> " + result;
            #endif

            if (result == ArcCalcResult.Success) break;
        }
        while (MathUtil.BalancedTraversal(ref fA, ref fB, ref ptr, c.framesA.Count - 1, c.framesB.Count - 1));

        if (result != ArcCalcResult.Success) return false;

        var stabilityRangeA = c.segmentA.TraceParams.ArcStableRange.WithMin(1);
        var stabilityRangeB = c.segmentB.TraceParams.ArcStableRange.WithMin(1);

        var valueAtMergeA = frameA.value + frameA.speed * (arcA + ductA);
        var valueAtMergeB = frameB.value + frameB.speed * (arcB + ductB);

        var targetDensity = 0.5.Lerp(frameA.density, frameB.density);

        var offsetAtMergeA = frameA.offset + frameA.width * targetDensity * 0.5 * shift;
        var offsetAtMergeB = frameB.offset + frameB.width * targetDensity * 0.5 * -shift;

        var connectedA = c.segmentA.ConnectedSegments();
        var connectedB = c.segmentB.ConnectedSegments();

        var valueDelta = valueAtMergeB - valueAtMergeA;
        var offsetDelta = offsetAtMergeB - offsetAtMergeA;

        var tight = Math.Abs(frameA.angle - frameB.angle) < 5d;

        var interconnected = connectedA.Any(e => connectedB.Contains(e));

        if (interconnected)
        {
            var mutableDistA = c.segmentA.LinearParents().Sum(s => s == c.segmentA ? frameA.dist : s.Length);
            var mutableDistB = c.segmentB.LinearParents().Sum(s => s == c.segmentB ? frameB.dist : s.Length);

            if (!tight)
            {
                mutableDistA += arcA + ductA;
                mutableDistB += arcB + ductB;
            }

            #if DEBUG
            PathTracer.DebugOutput($"Merge probably spans {valueDelta:F2} / {offsetDelta:F2} over {mutableDistA:F2} + {mutableDistB:F2}");
            #endif

            var mutableDist = mutableDistA + mutableDistB;

            if (mutableDist <= 0) return false;

            var valueLimitExc = valueDelta.Abs() / mutableDist > MergeValueDeltaLimit;
            var offsetLimitExc = offsetDelta.Abs() / mutableDist > MergeOffsetDeltaLimit;

            if (valueLimitExc || offsetLimitExc)
            {
                #if DEBUG
                PathTracer.DebugLine(new TraceDebugLine(
                    _tracer, c.position, valueLimitExc ? 1 : 7, 0,
                    $"V {valueDelta:F2} O {offsetDelta:F2}\n" +
                    $"D {mutableDistA:F2} + {mutableDistB:F2}"
                ));
                #endif

                return false;
            }
        }

        var discardedBranches = c.segmentA.Branches.ToList();
        var followingBranches = c.segmentB.Branches.ToList();

        var orgLengthA = c.segmentA.Length;
        var orgLengthB = c.segmentB.Length;

        c.segmentA.DetachAll();
        c.segmentB.DetachAll();

        var endA = InsertArcWithDuct(c.segmentA, ref frameA, arcA, ductA);
        var endB = InsertArcWithDuct(c.segmentB, ref frameB, arcB, ductB);

        Path.Segment InsertArcWithDuct(
            Path.Segment segment,
            ref TraceFrame frame,
            double arcLength,
            double ductLength)
        {
            segment.Length = frame.dist;

            var arcAngle = -Vector2d.SignedAngle(frame.normal, normal);

            if (ductLength > 0)
            {
                segment = segment.InsertNew();
                segment.TraceParams.ApplyFixedAngle(0, true);
                segment.Length = ductLength;

                #if DEBUG
                PathTracer.DebugOutput($"Inserted duct {segment.Id} with length {ductLength}");
                #endif
            }

            if (arcLength > 0)
            {
                segment = segment.InsertNew();
                segment.TraceParams.ApplyFixedAngle(arcAngle / arcLength, true);
                segment.Length = arcLength;

                #if DEBUG
                PathTracer.DebugOutput($"Inserted arc {segment.Id} with length {arcLength}");
                #endif
            }

            return segment;
        }

        var remainingLength = Math.Max(orgLengthA - c.segmentA.Length, orgLengthB - c.segmentB.Length);

        endA.ApplyLocalStabilityAtHead(stabilityRangeA / 2, stabilityRangeA / 2);
        endB.ApplyLocalStabilityAtHead(stabilityRangeB / 2, stabilityRangeB / 2);

        if (valueDelta != 0 || offsetDelta != 0)
        {
            if (interconnected)
            {
                var linearParentsA = tight ? c.segmentA.LinearParents() : endA.LinearParents();
                var linearParentsB = tight ? c.segmentB.LinearParents() : endB.LinearParents();

                var allowSingleFramesA = valueDelta > 0 || linearParentsA.All(s => s.Length < s.TraceParams.StepSize);
                var allowSingleFramesB = valueDelta < 0 || linearParentsB.All(s => s.Length < s.TraceParams.StepSize);

                var mutableDistA = linearParentsA.Sum(s => s.FullStepsCount(allowSingleFramesA) == 0 ? 0 : s.Length);
                var mutableDistB = linearParentsB.Sum(s => s.FullStepsCount(allowSingleFramesB) == 0 ? 0 : s.Length);

                #if DEBUG
                PathTracer.DebugOutput($"Merge actually spans {valueDelta:F2} / {offsetDelta:F2} over {mutableDistA:F2} + {mutableDistB:F2}");
                #endif

                var mutableDist = mutableDistA + mutableDistB;

                var diffSplitRatio = mutableDist > 0 ? mutableDistB / mutableDist : 0.5;

                var valueDeltaA = valueDelta * (1 - diffSplitRatio);
                var valueDeltaB = -1 * valueDelta * diffSplitRatio;

                var offsetDeltaA = offsetDelta * (1 - diffSplitRatio);
                var offsetDeltaB = -1 * offsetDelta * diffSplitRatio;

                #if DEBUG
                PathTracer.DebugLine(new TraceDebugLine(
                    _tracer, c.position, 2, 0,
                    $"V {valueDelta:F2} O {offsetDelta:F2}\n" +
                    $"D {mutableDist:F2} R {diffSplitRatio:F2}\n" +
                    $"D1 {mutableDistA:F2} D2 {mutableDistB:F2}\n" +
                    $"V1 {valueDeltaA:F2} V2 {valueDeltaB:F2}"
                ));
                #endif

                linearParentsA.Reverse();
                linearParentsB.Reverse();

                ModifySegments(linearParentsA, valueDeltaA, offsetDeltaA, allowSingleFramesA);
                ModifySegments(linearParentsB, valueDeltaB, offsetDeltaB, allowSingleFramesB);
            }
            else
            {
                ModifyRoots(connectedA, 0.5 * valueDelta, 0.5 * offsetDelta);
                ModifyRoots(connectedB, -0.5 * valueDelta, -0.5 * offsetDelta);
            }
        }

        void ModifySegments(List<Path.Segment> segments, double valueDiff, double offsetDiff, bool allowSingleFrames)
        {
            var totalSteps = segments.Sum(s => s.FullStepsCount(allowSingleFrames));

            var padding = totalSteps / 8;

            var currentSteps = 0;

            foreach (var segment in segments)
            {
                var fullSteps = segment.FullStepsCount(allowSingleFrames);

                if (fullSteps > 0)
                {
                    segment.SmoothDelta = new Path.SmoothDelta(valueDiff, offsetDiff, totalSteps, currentSteps, padding);
                    currentSteps += fullSteps;
                }
            }
        }

        void ModifyRoots(List<Path.Segment> segments, double valueDiff, double offsetDiff)
        {
            foreach (var segment in segments.Where(segment => segment.IsRoot))
            {
                segment.RelValue += valueDiff;
                segment.RelOffset += offsetDiff;
            }
        }

        if (frameA.density != frameB.density)
        {
            if (endA.Length > 0) endA.TraceParams.DensityLoss = (frameA.density - targetDensity) / endA.Length;
            if (endB.Length > 0) endB.TraceParams.DensityLoss = (frameB.density - targetDensity) / endB.Length;
        }

        var merged = new Path.Segment(endA.Path)
        {
            TraceParams = Path.TraceParams.Merge(c.segmentA.TraceParams, c.segmentB.TraceParams),
            Length = remainingLength
        };

        merged.ApplyLocalStabilityAtTail(0, 0.5.Lerp(stabilityRangeA, stabilityRangeB) / 2);

        endA.Attach(merged);
        endB.Attach(merged);

        foreach (var branch in followingBranches)
        {
            merged.Attach(branch);

            #if DEBUG
            PathTracer.DebugOutput($"Re-attaching branch {branch.Id}");
            #endif
        }

        foreach (var branch in discardedBranches)
        {
            branch.Discard();

            #if DEBUG
            PathTracer.DebugOutput($"Discarding branch {branch.Id}");
            #endif
        }

        return true;
    }

    private ArcCalcResult TryCalcArcs(
        Path.Segment a, Path.Segment b,
        Vector2d normal, double shift,
        ref TraceFrame frameA, ref TraceFrame frameB,
        out double arcLengthA, out double arcLengthB,
        out double ductLengthA, out double ductLengthB, int debugGroup)
    {
        var arcAngleA = -Vector2d.SignedAngle(frameA.normal, normal);
        var arcAngleB = -Vector2d.SignedAngle(frameB.normal, normal);

        // calculate the vector between the end points of the arcs

        var shiftDir = shift > 0 ? normal.PerpCW : normal.PerpCCW;
        var shiftSpan = (0.5 * frameA.width + 0.5 * frameB.width) * shiftDir;

        // calculate min arc lengths based on width and tenacity

        arcLengthA = arcAngleA.Abs() / 180 * frameA.width * Math.PI / (1 - a.TraceParams.AngleTenacity.WithMax(0.9));
        arcLengthB = arcAngleB.Abs() / 180 * frameB.width * Math.PI / (1 - b.TraceParams.AngleTenacity.WithMax(0.9));

        ductLengthA = 0;
        ductLengthB = 0;

        // check angle locks of each segment

        if (frameA.dist < a.AngleDeltaPosLockLength && arcAngleA > 0) return ArcCalcResult.ExcAngleLock;
        if (frameA.dist < a.AngleDeltaNegLockLength && arcAngleA < 0) return ArcCalcResult.ExcAngleLock;
        if (frameB.dist < b.AngleDeltaPosLockLength && arcAngleB > 0) return ArcCalcResult.ExcAngleLock;
        if (frameB.dist < b.AngleDeltaNegLockLength && arcAngleB < 0) return ArcCalcResult.ExcAngleLock;

        // calculate chord vector that spans arc B at its minimum length

        if (arcAngleA == 0 || arcAngleB == 0) return ArcCalcResult.NoPointF;

        var minPivotOffsetB = 180 * arcLengthB / (Math.PI * -arcAngleB);
        var minArcChordVecB = frameB.perpCCW * minPivotOffsetB - normal.PerpCCW * minPivotOffsetB;

        // try to find the min length for arc A needed to avoid ExcBoundF and ExcMaxAngle results

        var arcChordDirA = (frameA.normal + normal).Normalized;
        var targetAnchorA = frameB.pos + minArcChordVecB - shiftSpan;

        if (Vector2d.TryIntersect(frameA.pos, targetAnchorA, arcChordDirA, frameB.normal, out _, out var minChordLengthA, 0.01))
        {
            var arcAngleRadAbsA = arcAngleA.Abs().ToRad();
            var minArcLengthA = arcAngleRadAbsA * 0.5 * minChordLengthA / Math.Sin(0.5 * arcAngleRadAbsA);

            if (minArcLengthA > arcLengthA)
            {
                arcLengthA = minArcLengthA + 0.01;
            }
        }

        // calculate fixed end position of arc A

        var pivotOffsetA = 180 * arcLengthA / (Math.PI * -arcAngleA);
        var pivotPointA = frameA.pos + frameA.perpCCW * pivotOffsetA;
        var arcEndPosA = pivotPointA - normal.PerpCCW * pivotOffsetA;

        // find arc and duct lengths for side B based on https://math.stackexchange.com/a/1572508

        var pointB = frameB.pos;
        var pointC = arcEndPosA + shiftSpan;

        #if DEBUG

        PathTracer.DebugLine(new TraceDebugLine(_tracer, arcEndPosA, pointC, 0, debugGroup));

        PathTracer.DebugLine(new TraceDebugLine(_tracer, frameA.pos, pivotPointA, 2, debugGroup));
        PathTracer.DebugLine(new TraceDebugLine(_tracer, arcEndPosA, pivotPointA, 3, debugGroup));

        if (Vector2d.TryIntersect(frameA.pos, arcEndPosA, frameA.normal, normal, out var pointJ, 0.001))
        {
            PathTracer.DebugLine(new TraceDebugLine(_tracer, frameA.pos, pointJ, 5, debugGroup));
            PathTracer.DebugLine(new TraceDebugLine(_tracer, arcEndPosA, pointJ, 1, debugGroup));
        }

        #endif

        if (!Vector2d.TryIntersect(pointB, pointC, frameB.normal, normal, out var pointF, out var scalarB, 0.001))
        {
            #if DEBUG
            PathTracer.DebugOutput("Point F could not be constructed");
            #endif

            return ArcCalcResult.NoPointF;
        }

        #if DEBUG
        PathTracer.DebugLine(new TraceDebugLine(_tracer, pointB, pointF, 5, debugGroup));
        PathTracer.DebugLine(new TraceDebugLine(_tracer, pointC, pointF, 1, debugGroup));
        #endif

        if (scalarB < 0)
        {
            #if DEBUG
            PathTracer.DebugOutput($"Scalar B {scalarB} is below 0");
            #endif

            return ArcCalcResult.ExcBoundB;
        }

        var scalarF = Vector2d.PerpDot(frameB.normal, pointB - pointC) / Vector2d.PerpDot(frameB.normal, normal);

        if (scalarF > 0)
        {
            #if DEBUG
            PathTracer.DebugOutput($"Scalar F {scalarF} is above 0");
            #endif

            return ArcCalcResult.ExcBoundF;
        }

        var distBF = Vector2d.Distance(pointB, pointF);
        var distCF = Vector2d.Distance(pointC, pointF);

        if (distBF < distCF)
        {
            #if DEBUG
            PathTracer.DebugOutput($"Distance from B to F {distBF} is lower than distance from C to F {distCF}");
            #endif

            return ArcCalcResult.DuctBelowZero;
        }

        ductLengthB = distBF - distCF;

        var pointG = pointB + frameB.normal * ductLengthB;

        if (!Vector2d.TryIntersect(pointG, pointC, frameB.perpCW, normal.PerpCW, out var pointK, 0.001))
        {
            #if DEBUG
            PathTracer.DebugOutput("The point K could not be constructed");
            #endif

            return ArcCalcResult.NoPointK;
        }

        var radiusB = Vector2d.Distance(pointG, pointK);
        var chordLengthB = Vector2d.Distance(pointG, pointC);

        #if DEBUG
        PathTracer.DebugLine(new TraceDebugLine(_tracer, pointG, pointK, 2, debugGroup));
        PathTracer.DebugLine(new TraceDebugLine(_tracer, pointC, pointK, 3, debugGroup));
        #endif

        // calculate length of arc B based on https://www.omnicalculator.com/math/arc-length

        arcLengthB = 2 * radiusB * Math.Asin(0.5 * chordLengthB / radiusB);

        if (double.IsNaN(arcLengthB))
        {
            #if DEBUG
            PathTracer.DebugOutput($"The arc length is NaN for chord length {chordLengthB} and radius {radiusB}");
            #endif

            return ArcCalcResult.ArcLengthNaN;
        }

        // check for potential arc overlap

        var rotRateA = arcAngleA / arcLengthA;
        var rotRateB = arcAngleB / arcLengthB;

        if (rotRateA > 0 == rotRateB > 0 && shift < 0 != rotRateA > rotRateB)
        {
            #if DEBUG
            PathTracer.DebugOutput($"The arcs with rotation rates {rotRateA} vs {rotRateB} would overlap");
            #endif

            return ArcCalcResult.ArcOverlap;
        }

        // calculate max angle for arc B and make sure it is not exceeded

        var arcAngleMaxB = (1 - b.TraceParams.AngleTenacity) * 180 * arcLengthB / (frameB.width * Math.PI);

        if (Math.Round(arcAngleB.Abs()) > arcAngleMaxB)
        {
            #if DEBUG
            PathTracer.DebugOutput($"The arc angle {arcAngleB} is larger than the limit {arcAngleMaxB}");
            #endif

            return ArcCalcResult.ExcMaxAngle;
        }

        #if DEBUG
        PathTracer.DebugOutput($"Success with arc1 {arcLengthA} duct1 {ductLengthB} arc2 {arcLengthB} duct2 {ductLengthB}");
        PathTracer.DebugOutput($"Target for arc1 is {arcEndPosA} and target for arc2 is {pointC}");
        #endif

        return ArcCalcResult.Success;
    }

    private enum ArcCalcResult
    {
        Success,
        NoPointF, // angle between the arm and target direction is extremely small -> go back in frames, hoping it increases
        ExcBoundB, // arc can not reach target because it is too far out -> go back in frames, hoping to get more space between the arms
        ExcBoundF, // arc can not reach target because it is too far in -> increase radius on fixed side to move the target further out
        DuctBelowZero, // frame is too close to the target -> go back in frames, ideally specifically on the dynamic side
        NoPointK, // same angle and same problem as NoPointF -> go back in frames, hoping it increases
        ArcLengthNaN, // something went very wrong, likely some angle was 0 -> go back in frames, hoping that fixes it
        ExcMaxAngle, // arc radius is too small for this segment width -> go back in frames, hoping to get more space between the arms
        ExcAngleLock, // arc angle would violate angle delta lock of the segment -> go back in frames
        ArcOverlap // arcs curve in the same direction and are located is such a way that they would overlap -> go back in frames
    }

    /// <summary>
    /// Attempt to divert one of the involved path segments in order to avoid the given collision.
    /// </summary>
    /// <returns>true if one of the segments was diverted successfully, otherwise false</returns>
    private bool TryDivert(TraceCollision c, bool passiveBranch)
    {
        Path.Segment segmentD, segmentP;
        TraceFrame frameD, frameP;

        if (passiveBranch)
        {
            if (c.hasMergeB) return false;

            segmentP = c.segmentA;
            segmentD = c.segmentB;

            frameP = c.frameA;
            frameD = c.frameB;
        }
        else
        {
            if (c.hasMergeA) return false;

            segmentP = c.segmentB;
            segmentD = c.segmentA;

            frameP = c.frameB;
            frameD = c.frameA;
        }

        if (segmentD.TraceParams.ArcRetraceRange <= 0) return false;
        if (segmentD.TraceParams.ArcRetraceFactor <= 0) return false;

        if ((segmentD.TraceParams.DiversionPoints?.Count ?? 0) >= MaxDiversionPoints) return false;

        var normal = frameP.perpCW * Vector2d.PointToLineOrientation(frameD.pos, frameD.pos + frameD.normal, frameP.pos);

        var perpDotD = Vector2d.PerpDot((frameP.pos - frameD.pos).Normalized, frameD.normal);
        var perpDotP = Vector2d.PerpDot((frameD.pos - frameP.pos).Normalized, frameP.normal);

        // this is low in the case of a frontal/head-on collision
        var perpScore = 0.5 * (perpDotD.Abs() + perpDotP.Abs());

        var diversion = perpScore > 0.2 ? Vector2d.Reflect(frameD.normal, normal) : normal;

        var factor = segmentD.TraceParams.ArcRetraceFactor;

        var point = new Path.DiversionPoint(frameD.pos, diversion * factor, segmentD.TraceParams.ArcRetraceRange);

        var segments = segmentD.ConnectedSegments(false, true,
            s => s.BranchCount == 1 || !s.AnyBranchesMatch(b => b == segmentP || b.ParentCount > 1, false),
            s => s.ParentCount == 1
        );

        if (segments.Sum(s => s == segmentD ? frameD.dist : s.Length) < DiversionMinLength) return false;

        var distanceCovered = 0d;

        foreach (var segment in segments)
        {
            distanceCovered += segment == segmentD ? frameD.dist : segment.Length;

            segment.TraceParams.AddDiversionPoint(point);

            #if DEBUG
            PathTracer.DebugOutput($"Added diversion to {segment.Id} with data {point}");
            #endif

            if (distanceCovered >= point.Range) break;
        }

        #if DEBUG
        PathTracer.DebugLine(new TraceDebugLine(_tracer, frameD.pos, 4, 0, $"DC {distanceCovered:F2}\nPSC {perpScore:F2}"));
        PathTracer.DebugLine(new TraceDebugLine(_tracer, frameD.pos, frameD.pos + diversion, 4));
        PathTracer.DebugLine(new TraceDebugLine(_tracer, frameP.pos, frameP.pos + normal, 4));
        #endif

        return true;
    }

    /// <summary>
    /// Attempt to simplify the preceding segment chains in order to avoid the given collision.
    /// </summary>
    /// <returns>true if one of the segment chains was adjusted successfully, otherwise false</returns>
    private bool TrySimplify(TraceCollision c, bool passiveBranch)
    {
        if (passiveBranch ? c.hasMergeB : c.hasMergeA) return false;

        var segment = passiveBranch ? c.segmentB : c.segmentA;
        if (segment.Length >= _tracer.GridInnerSize.Magnitude) return false;

        var postAnchor = segment.LinearParents().Last();
        if (postAnchor.ParentCount != 1) return false;

        var preAnchor = postAnchor.Parents.First();
        if (preAnchor.BranchCount <= 1) return false;

        if (preAnchor.Branches.Any(b => b != postAnchor && b.AnyBranchesMatch(s => s.ParentCount > 1, true))) return false;

        var lengthAdj = SimplificationLength * Math.Pow(2d, preAnchor.AdjustmentCount);

        #if DEBUG
        PathTracer.DebugOutput($"Adjusting length of segment {preAnchor.Id} by {lengthAdj}");
        #endif

        preAnchor.Length += lengthAdj;
        preAnchor.AdjustmentCount++;

        return true;
    }

    /// <summary>
    /// Stub the given path segment in order to avoid a collision.
    /// </summary>
    private void Stub(Path.Segment segment, List<TraceFrame> frames, Path.Segment cause)
    {
        segment.Length = frames[frames.Count - 1].dist;

        var lengthDiff = -1 * StubBacktrackLength;

        var widthAtTail = frames[0].width;
        var densityAtTail = frames[0].density;
        var speedAtTail = frames[0].speed;

        bool discardingCause = false;

        while (segment.Length + lengthDiff < 2 * widthAtTail)
        {
            if (segment.ParentCount == 0 || segment.RelWidth <= 0 || segment.RelDensity <= 0 || segment.RelSpeed <= 0)
            {
                segment.Discard();
                return;
            }

            if (segment.ParentCount == 1)
            {
                var parent = segment.Parents.First();

                widthAtTail = widthAtTail / segment.RelWidth + parent.TraceParams.WidthLoss * parent.Length;
                densityAtTail = densityAtTail / segment.RelDensity + parent.TraceParams.DensityLoss * parent.Length;
                speedAtTail = speedAtTail / segment.RelSpeed + parent.TraceParams.SpeedLoss * parent.Length;

                if (!discardingCause && parent.IsParentOf(cause, true))
                {
                    lengthDiff += StubBacktrackLength;
                    discardingCause = true;
                }

                lengthDiff += segment.Length;
                segment = parent;
            }
            else // TODO handle ParentCount > 1 (pick one of them to stub, leave the rest untouched) - need to check for merges in the other ones though
            {
                break;
            }
        }

        segment.Length += lengthDiff;

        if (segment.IsRoot || segment.Length <= 0)
        {
            segment.Discard();

            #if DEBUG
            PathTracer.DebugOutput($"Discarding stub segment {segment.Id}");
            #endif
        }
        else
        {
            segment.TraceParams.WidthLoss = widthAtTail / segment.Length;
            segment.TraceParams.DensityLoss = -3 * densityAtTail / segment.Length;
            segment.TraceParams.SpeedLoss = -3 * speedAtTail / segment.Length;

            #if DEBUG
            PathTracer.DebugOutput($"Stubbing segment {segment.Id}");
            #endif
        }

        segment.DetachAll(true);
    }
}