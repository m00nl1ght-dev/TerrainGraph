using System;
using System.Collections.Generic;
using System.Linq;
using TerrainGraph.Util;

namespace TerrainGraph.Flow;

public class TraceCollisionHandler
{
    public int MaxDiversionPoints = 5;

    public double StubBacktrackLength = 10;
    public double MergeValueDeltaLimit = 0.45;
    public double MergeOffsetDeltaLimit = 0.45;

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
            if (first.complete)
            {
                PathTracer.DebugOutput($"Attempting merge: {first}");

                if (TryMerge(first))
                {
                    PathTracer.DebugOutput($"Path merge was successful");
                    return;
                }

                PathTracer.DebugOutput($"Path merge not possible, moving on to first diversion attempt");

                var divCountA = first.segmentA.TraceParams.DiversionPoints?.Count ?? 0;
                var divCountB = first.segmentB.TraceParams.DiversionPoints?.Count ?? 0;

                var passiveFirst = divCountA == divCountB ? first.frameA.width > first.frameB.width : divCountA > divCountB;

                if (TryDivert(first, passiveFirst))
                {
                    PathTracer.DebugOutput($"Path diversion was successful");
                    return;
                }

                PathTracer.DebugOutput($"Path diversion not possible, moving on to second diversion attempt");

                if (TryDivert(first, !passiveFirst))
                {
                    PathTracer.DebugOutput($"Path diversion was successful");
                    return;
                }

                PathTracer.DebugOutput($"Path diversion not possible, stubbing instead");
                Stub(first);
            }
            else
            {
                PathTracer.DebugOutput($"!!! Collision missing data: {first}");
                Stub(first);
            }
        }
    }

    /// <summary>
    /// Attempt to merge the involved path segments in order to avoid the given collision.
    /// </summary>
    /// <returns>true if the segments were merged successfully, otherwise false</returns>
    private bool TryMerge(TraceCollision c)
    {
        var dot = Vector2d.Dot(c.frameA.normal, c.frameB.normal);
        var perpDot = Vector2d.PerpDot(c.frameA.normal, c.frameB.normal);

        var normal = dot > 0 || perpDot.Abs() > 0.05
            ? (c.frameA.normal * c.frameA.width + c.frameB.normal * c.frameB.width).Normalized
            : perpDot >= 0 ? c.frameA.normal.PerpCCW : c.frameA.normal.PerpCW;

        var shift = Vector2d.PointToLineOrientation(c.frameB.pos, c.frameB.pos + c.frameB.normal, c.frameA.pos);

        PathTracer.DebugOutput($"Target direction for merge is {normal} with dot {dot} perpDot {perpDot}");

        if (c.segmentA.TraceParams.AvoidOverlap > 0) return false;
        if (c.segmentA.IsBranchOf(c.segmentB, true)) return false;

        if (c.segmentA.AnyBranchesMatch(s => s.ParentCount > 1, false)) return false;
        if (c.segmentB.AnyBranchesMatch(s => s.ParentCount > 1, false)) return false;

        double arcA, arcB, ductA, ductB;
        TraceFrame frameA, frameB;
        ArcCalcResult result;

        int ptr = 0, fA = 0, fB = 0;

        do
        {
            frameA = c.framesA[c.framesA.Count - fA - 1];
            frameB = c.framesB[c.framesB.Count - fB - 1];

            PathTracer.DebugOutput($"--> Attempting merge with {fA} | {fB} frames backtracked with A at {frameA.pos} and B at {frameB.pos}");

            TraceDebugLine debugPoint1 = null;
            TraceDebugLine debugPoint2 = null;

            if (_tracer.DebugLines != null)
            {
                debugPoint1 = _tracer.DebugLines.FirstOrDefault(dl => dl.IsPointAt(frameA.pos));
                debugPoint2 = _tracer.DebugLines.FirstOrDefault(dl => dl.IsPointAt(frameB.pos));

                if (debugPoint1 == null) _tracer.DebugLines.Add(debugPoint1 = new TraceDebugLine(_tracer, frameA.pos, 0, 0, fA.ToString()));
                if (debugPoint2 == null) _tracer.DebugLines.Add(debugPoint2 = new TraceDebugLine(_tracer, frameB.pos, 0, 0, fB.ToString()));
            }

            result = TryCalcArcs(
                c.segmentA, c.segmentB, normal, shift, ref frameA, ref frameB,
                out arcA, out arcB, out ductA, out ductB, _tracer.DebugLines, 1
            );

            if (debugPoint1 != null) debugPoint1.Label += "\n" + fB + " -> " + result;
            if (result == ArcCalcResult.Success) break;

            result = TryCalcArcs(
                c.segmentB, c.segmentA, normal, -shift, ref frameB, ref frameA,
                out arcB, out arcA, out ductB, out ductA, _tracer.DebugLines, 2
            );

            if (debugPoint2 != null) debugPoint2.Label += "\n" + fA + " -> " + result;
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

        var interconnected = connectedA.Any(e => connectedB.Contains(e));

        if (interconnected)
        {
            var mutableDistA = arcA + ductA + c.segmentA.LinearParents().Sum(s => s == c.segmentA ? frameA.dist : s.Length);
            var mutableDistB = arcB + ductB + c.segmentB.LinearParents().Sum(s => s == c.segmentB ? frameB.dist : s.Length);

            PathTracer.DebugOutput($"Merge probably spans {valueDelta:F2} / {offsetDelta:F2} over {mutableDistA:F2} + {mutableDistB:F2}");

            var mutableDist = mutableDistA + mutableDistB;

            if (mutableDist <= 0) return false;

            var valueLimitExc = valueDelta.Abs() / mutableDist > MergeValueDeltaLimit;
            var offsetLimitExc = offsetDelta.Abs() / mutableDist > MergeOffsetDeltaLimit;

            if (valueLimitExc || offsetLimitExc)
            {
                _tracer.DebugLines?.Add(new TraceDebugLine(
                    _tracer, c.position, valueLimitExc ? 1 : 7, 0,
                    $"V {valueDelta:F2} O {offsetDelta:F2}\n" +
                    $"D {mutableDistA:F2} + {mutableDistB:F2}"
                ));

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
                PathTracer.DebugOutput($"Inserted duct {segment.Id} with length {ductLength}");
            }

            if (arcLength > 0)
            {
                segment = segment.InsertNew();
                segment.TraceParams.ApplyFixedAngle(arcAngle / arcLength, true);
                segment.Length = arcLength;
                PathTracer.DebugOutput($"Inserted arc {segment.Id} with length {arcLength}");
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
                var linearParentsA = endA.LinearParents();
                var linearParentsB = endB.LinearParents();

                var allowSingleFramesA = valueDelta > 0 || linearParentsA.All(s => s.Length < s.TraceParams.StepSize);
                var allowSingleFramesB = valueDelta < 0 || linearParentsB.All(s => s.Length < s.TraceParams.StepSize);

                var mutableDistA = linearParentsA.Sum(s => s.FullStepsCount(allowSingleFramesA) == 0 ? 0 : s.Length);
                var mutableDistB = linearParentsB.Sum(s => s.FullStepsCount(allowSingleFramesB) == 0 ? 0 : s.Length);

                PathTracer.DebugOutput($"Merge actually spans {valueDelta:F2} / {offsetDelta:F2} over {mutableDistA:F2} + {mutableDistB:F2}");

                var mutableDist = mutableDistA + mutableDistB;

                var diffSplitRatio = mutableDist > 0 ? mutableDistB / mutableDist : 0.5;

                var valueDeltaA = valueDelta * (1 - diffSplitRatio);
                var valueDeltaB = -1 * valueDelta * diffSplitRatio;

                var offsetDeltaA = offsetDelta * (1 - diffSplitRatio);
                var offsetDeltaB = -1 * offsetDelta * diffSplitRatio;

                _tracer.DebugLines?.Add(new TraceDebugLine(
                    _tracer, c.position, 2, 0,
                    $"V {valueDelta:F2} O {offsetDelta:F2}\n" +
                    $"D {mutableDist:F2} R {diffSplitRatio:F2}\n" +
                    $"D1 {mutableDistA:F2} D2 {mutableDistB:F2}\n" +
                    $"V1 {valueDeltaA:F2} V2 {valueDeltaB:F2}"
                ));

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
                    PathTracer.DebugOutput($"Smooth delta for segment {segment.Id} => {segment.SmoothDelta}");
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
            PathTracer.DebugOutput($"Re-attaching branch {branch.Id}");
            merged.Attach(branch);
        }

        foreach (var branch in discardedBranches)
        {
            PathTracer.DebugOutput($"Discarding branch {branch.Id}");
            branch.Discard();
        }

        return true;
    }

    private ArcCalcResult TryCalcArcs(
        Path.Segment a, Path.Segment b,
        Vector2d normal, double shift,
        ref TraceFrame frameA, ref TraceFrame frameB,
        out double arcLengthA, out double arcLengthB,
        out double ductLengthA, out double ductLengthB,
        List<TraceDebugLine> debugLines, int debugGroup)
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

        if (debugLines != null)
        {
            debugLines.Add(new TraceDebugLine(_tracer, arcEndPosA, pointC, 0, debugGroup));

            debugLines.Add(new TraceDebugLine(_tracer, frameA.pos, pivotPointA, 2, debugGroup));
            debugLines.Add(new TraceDebugLine(_tracer, arcEndPosA, pivotPointA, 3, debugGroup));

            if (Vector2d.TryIntersect(frameA.pos, arcEndPosA, frameA.normal, normal, out var pointJ, 0.001))
            {
                debugLines.Add(new TraceDebugLine(_tracer, frameA.pos, pointJ, 5, debugGroup));
                debugLines.Add(new TraceDebugLine(_tracer, arcEndPosA, pointJ, 1, debugGroup));
            }
        }

        if (!Vector2d.TryIntersect(pointB, pointC, frameB.normal, normal, out var pointF, out var scalarB, 0.001))
        {
            PathTracer.DebugOutput($"Point F could not be constructed");
            return ArcCalcResult.NoPointF;
        }

        if (debugLines != null)
        {
            debugLines.Add(new TraceDebugLine(_tracer, pointB, pointF, 5, debugGroup));
            debugLines.Add(new TraceDebugLine(_tracer, pointC, pointF, 1, debugGroup));
        }

        if (scalarB < 0)
        {
            PathTracer.DebugOutput($"Scalar B {scalarB} is below 0");
            return ArcCalcResult.ExcBoundB;
        }

        var scalarF = Vector2d.PerpDot(frameB.normal, pointB - pointC) / Vector2d.PerpDot(frameB.normal, normal);

        if (scalarF > 0)
        {
            PathTracer.DebugOutput($"Scalar F {scalarF} is above 0");
            return ArcCalcResult.ExcBoundF;
        }

        var distBF = Vector2d.Distance(pointB, pointF);
        var distCF = Vector2d.Distance(pointC, pointF);

        if (distBF < distCF)
        {
            PathTracer.DebugOutput($"Distance from B to F {distBF} is lower than distance from C to F {distCF}");
            return ArcCalcResult.DuctBelowZero;
        }

        ductLengthB = distBF - distCF;

        var pointG = pointB + frameB.normal * ductLengthB;

        if (!Vector2d.TryIntersect(pointG, pointC, frameB.perpCW, normal.PerpCW, out var pointK, 0.001))
        {
            PathTracer.DebugOutput($"The point K could not be constructed");
            return ArcCalcResult.NoPointK;
        }

        var radiusB = Vector2d.Distance(pointG, pointK);
        var chordLengthB = Vector2d.Distance(pointG, pointC);

        if (debugLines != null)
        {
            debugLines.Add(new TraceDebugLine(_tracer, pointG, pointK, 2, debugGroup));
            debugLines.Add(new TraceDebugLine(_tracer, pointC, pointK, 3, debugGroup));
        }

        // calculate length of arc B based on https://www.omnicalculator.com/math/arc-length

        arcLengthB = 2 * radiusB * Math.Asin(0.5 * chordLengthB / radiusB);

        if (double.IsNaN(arcLengthB))
        {
            PathTracer.DebugOutput($"The arc length is NaN for chord length {chordLengthB} and radius {radiusB}");
            return ArcCalcResult.ArcLengthNaN;
        }

        // calculate max angle for arc B and make sure it is not exceeded

        var arcAngleMaxB = (1 - b.TraceParams.AngleTenacity) * 180 * arcLengthB / (frameB.width * Math.PI);

        if (Math.Round(arcAngleB.Abs()) > arcAngleMaxB)
        {
            PathTracer.DebugOutput($"The arc angle {arcAngleB} is larger than the limit {arcAngleMaxB}");
            return ArcCalcResult.ExcMaxAngle;
        }

        PathTracer.DebugOutput($"Success with arcA {arcLengthA} ductA {ductLengthB} arcB {arcLengthB} ductB {ductLengthB}");
        PathTracer.DebugOutput($"Target for arcA is {arcEndPosA} and target for arcB is {pointC}");
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
        ExcMaxAngle // arc radius is too small for this segment width -> go back in frames, hoping to get more space between the arms
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
            segmentP = c.segmentA;
            segmentD = c.segmentB;
            frameP = c.frameA;
            frameD = c.frameB;
        }
        else
        {
            segmentP = c.segmentB;
            segmentD = c.segmentA;
            frameP = c.frameB;
            frameD = c.frameA;
        }

        if (segmentD.TraceParams.ArcRetraceRange <= 0) return false;
        if (segmentD.TraceParams.ArcRetraceFactor <= 0) return false;

        if (segmentD.AnyBranchesMatch(s => s.ParentCount > 1, false)) return false;

        if ((segmentD.TraceParams.DiversionPoints?.Count ?? 0) >= MaxDiversionPoints) return false;

        var normal = frameP.perpCW * Vector2d.PointToLineOrientation(frameD.pos, frameD.pos + frameD.normal, frameP.pos);
        var reflected = Vector2d.Reflect(frameD.normal, normal) * segmentD.TraceParams.ArcRetraceFactor;

        var point = new Path.DiversionPoint(frameD.pos, reflected, segmentD.TraceParams.ArcRetraceRange);

        var segments = segmentD.ConnectedSegments(false, true,
            s => s.BranchCount == 1 || !s.AnyBranchesMatch(b => b == segmentP || b.ParentCount > 1, false),
            s => s.ParentCount == 1
        );

        if (segments.Sum(s => s == segmentD ? frameD.dist : s.Length) < segmentD.TraceParams.StepSize) return false;

        var distanceCovered = 0d;

        foreach (var segment in segments)
        {
            distanceCovered += segment == segmentD ? frameD.dist : segment.Length;

            segment.TraceParams.AddDiversionPoint(point);

            PathTracer.DebugOutput($"Added diversion to {segment.Id} with data {point}");

            if (distanceCovered >= point.Range) break;
        }

        if (_tracer.DebugLines != null)
        {
            _tracer.DebugLines.Add(new TraceDebugLine(_tracer, frameD.pos, 4, 0, $"D {distanceCovered}"));
            _tracer.DebugLines.Add(new TraceDebugLine(_tracer, frameD.pos, frameD.pos + reflected, 4));
            _tracer.DebugLines.Add(new TraceDebugLine(_tracer, frameP.pos, frameP.pos + normal, 4));
        }

        return true;
    }

    /// <summary>
    /// Stub one of the involved path segments in order to avoid the given collision.
    /// </summary>
    private void Stub(TraceCollision c)
    {
        Path.Segment stub;
        List<TraceFrame> frames;

        var hasAnyMergeA = c.segmentA.AnyBranchesMatch(s => s.ParentCount > 1, false);
        var hasAnyMergeB = c.segmentB.AnyBranchesMatch(s => s.ParentCount > 1, false);

        if (hasAnyMergeA == hasAnyMergeB ? c.frameA.width <= c.frameB.width : hasAnyMergeB)
        {
            stub = c.segmentA;
            frames = c.framesA;
        }
        else
        {
            stub = c.segmentB;
            frames = c.framesB;
        }

        stub.Length = frames[frames.Count - 1].dist;

        var lengthDiff = -1 * StubBacktrackLength;

        var widthAtTail = frames[0].width;
        var densityAtTail = frames[0].density;
        var speedAtTail = frames[0].speed;

        while (stub.Length + lengthDiff < 2.5 * widthAtTail)
        {
            if (stub.ParentCount == 0 || stub.RelWidth <= 0 || stub.RelDensity <= 0 || stub.RelSpeed <= 0)
            {
                stub.Discard();
                return;
            }

            if (stub.ParentCount == 1)
            {
                var parent = stub.Parents.First();
                if (parent.AnyBranchesMatch(b => b.ParentCount > 1, false)) break;

                widthAtTail = widthAtTail / stub.RelWidth + parent.TraceParams.WidthLoss * parent.Length;
                densityAtTail = densityAtTail / stub.RelDensity + parent.TraceParams.DensityLoss * parent.Length;
                speedAtTail = speedAtTail / stub.RelSpeed + parent.TraceParams.SpeedLoss * parent.Length;

                lengthDiff += stub.Length;
                stub = parent;
            }
            else
            {
                break;
            }
        }

        PathTracer.DebugOutput($"Stubbing segment {stub.Id}");

        stub.Length += lengthDiff;

        if (stub.Length > 0)
        {
            stub.TraceParams.WidthLoss = widthAtTail / stub.Length;
            stub.TraceParams.DensityLoss = -3 * densityAtTail / stub.Length;
            stub.TraceParams.SpeedLoss = -3 * speedAtTail / stub.Length;
        }

        stub.DetachAll(true);
    }
}
