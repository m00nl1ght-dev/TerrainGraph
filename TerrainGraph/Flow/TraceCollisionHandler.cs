using System;
using System.Collections.Generic;
using System.Linq;
using TerrainGraph.Util;
using static TerrainGraph.Flow.Path;

namespace TerrainGraph.Flow;

public class TraceCollisionHandler
{
    public int MaxDiversionPoints = 5;
    public int MaxStabilityPoints = 3;

    public double MergeValueDeltaLimit = 0.45;
    public double MergeOffsetDeltaLimit = 0.45;
    public double SimplificationLength = 10;
    public double DiversionMinLength = 5;
    public double StubBacktrackLength = 10;
    public double TenacityAdjStep = 0.15;
    public double TenacityAdjMax = 0.9;

    private readonly PathTracer _tracer;

    public TraceCollisionHandler(PathTracer tracer)
    {
        _tracer = tracer;
    }

    /// <summary>
    /// Rewrite path segments such that the earliest of the given collisions is avoided.
    /// </summary>
    /// <param name="collisions">list of collisions that should be considered</param>
    internal void HandleBestCollision(List<TraceCollision> collisions)
    {
        var best = FindBestCollision(collisions);
        if (best != null) HandleCollision(best);
    }

    private TraceCollision FindBestCollision(List<TraceCollision> collisions)
    {
        TraceCollision best = null;

        while (collisions.Count > 0)
        {
            best = null;

            foreach (var collision in collisions)
            {
                if (best == null || collision.Precedes(best))
                {
                    best = collision;
                }
            }

            var enclosedSegments = best!.FindEnclosedSegments();

            collisions = collisions.Where(c => c != best && enclosedSegments.Contains(c.taskA.segment)).ToList();

            #if DEBUG

            if (enclosedSegments.Count > 0)
            {
                var debugString = string.Join(", ", enclosedSegments.Select(s => s.Id));
                PathTracer.DebugOutput($"Enclosed segments for {best.taskA.segment.Id} vs {best.taskB.segment.Id}: {debugString}");
            }

            #endif
        }

        return best;
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

            Stub(c.taskA.segment, c.frameA.dist);
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

        var divCountA = c.taskA.segment.TraceParams.DiversionPoints?.Count ?? 0;
        var divCountB = c.taskB.segment.TraceParams.DiversionPoints?.Count ?? 0;

        var adjPrioA = c.taskA.segment.TraceParams.AdjustmentPriority;
        var adjPrioB = c.taskB.segment.TraceParams.AdjustmentPriority;

        var passiveFirst = adjPrioA != adjPrioB ? adjPrioB
            : divCountA != divCountB ? divCountA > divCountB
            : c.frameA.width > c.frameB.width;

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

        passiveFirst = adjPrioA != adjPrioB ? adjPrioB
            : c.frameA.width < c.frameB.width;

        if (TryStabilizeExtent(c, passiveFirst))
        {
            #if DEBUG
            PathTracer.DebugOutput("First extent stabilization attempt was successful");
            #endif

            return;
        }

        if (TryStabilizeExtent(c, !passiveFirst))
        {
            #if DEBUG
            PathTracer.DebugOutput("Second extent stabilization attempt was successful");
            #endif

            return;
        }

        passiveFirst = adjPrioA != adjPrioB ? adjPrioB
            : c.frameA.width > c.frameB.width;

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

        if (TryApplyTenacity(c))
        {
            #if DEBUG
            PathTracer.DebugOutput("Tenacity adjustment attempt was successful");
            #endif

            #if DEBUG
            throw new Exception("Could not handle collision without resorting to tenacity adjustment");
            #else
            return;
            #endif
        }

        #if DEBUG
        PathTracer.DebugOutput("Could not avert the collision, stubbing instead");
        #endif

        if (c.hasMergeA == c.hasMergeB ? c.frameA.width <= c.frameB.width : c.hasMergeB)
        {
            Stub(c.taskA.segment, c.frameA.dist);
        }
        else
        {
            Stub(c.taskB.segment, c.frameB.dist);
        }
    }

    /// <summary>
    /// Attempt to merge the involved path segments in order to avoid the given collision.
    /// </summary>
    /// <returns>true if the segments were merged successfully, otherwise false</returns>
    private bool TryMerge(TraceCollision c)
    {
        var a = c.taskA.segment;
        var b = c.taskB.segment;

        if (c.cyclic || c.hasMergeA || c.hasMergeB) return false;
        if (a.TraceParams.PreventMerge || b.TraceParams.PreventMerge) return false;
        if (a.AnyParentsMatch(s => s.TraceParams.ResultUnstable, true)) return false;
        if (b.AnyParentsMatch(s => s.TraceParams.ResultUnstable, true)) return false;
        if (a.TraceParams.AdjustmentPriority != b.TraceParams.AdjustmentPriority) return false;

        var resultTrimA = a.TraceParams.MergeResultTrim;
        var resultTrimB = b.TraceParams.MergeResultTrim;

        if (resultTrimA < 0 && c.taskA.distFromRoot + c.frameA.dist < -resultTrimA) return false;
        if (resultTrimB < 0 && c.taskB.distFromRoot + c.frameB.dist < -resultTrimB) return false;

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
            frameA = c.framesA[c.framesA.Count - fA - 2];
            frameB = c.framesB[c.framesB.Count - fB - 2];

            #if DEBUG
            var debugPoint1 = PathTracer.DebugLines.FirstOrDefault(dl => dl.IsPointAt(frameA.pos));
            var debugPoint2 = PathTracer.DebugLines.FirstOrDefault(dl => dl.IsPointAt(frameB.pos));
            if (debugPoint1 == null) PathTracer.DebugLine(debugPoint1 = new TraceDebugLine(_tracer, frameA.pos, 0, 0, fA.ToString()));
            if (debugPoint2 == null) PathTracer.DebugLine(debugPoint2 = new TraceDebugLine(_tracer, frameB.pos, 0, 0, fB.ToString()));
            PathTracer.DebugOutput($"--> Attempting merge with {fA} | {fB} frames backtracked with A at {frameA.pos} and B at {frameB.pos}");
            #endif

            result = TryCalcArcs(
                c.taskA, c.taskB, normal, shift, ref frameA, ref frameB,
                out arcA, out arcB, out ductA, out ductB, 1
            );

            #if DEBUG
            debugPoint1.Label += "\n" + fB + " -> " + result;
            #endif

            if (result == ArcCalcResult.Success) break;
            if (result == ArcCalcResult.Obstructed) return false;

            result = TryCalcArcs(
                c.taskB, c.taskA, normal, -shift, ref frameB, ref frameA,
                out arcB, out arcA, out ductB, out ductA, 2
            );

            #if DEBUG
            debugPoint2.Label += "\n" + fA + " -> " + result;
            #endif

            if (result == ArcCalcResult.Success) break;
            if (result == ArcCalcResult.Obstructed) return false;
        }
        while (MathUtil.BalancedTraversal(ref fA, ref fB, ref ptr, c.framesA.Count - 2, c.framesB.Count - 2));

        if (result != ArcCalcResult.Success) return false;

        var valueAtMergeA = frameA.value + frameA.speed * (arcA + ductA);
        var valueAtMergeB = frameB.value + frameB.speed * (arcB + ductB);

        var targetDensity = 0.5.Lerp(frameA.density, frameB.density);

        var offsetAtMergeA = frameA.offset + frameA.width * targetDensity * 0.5 * shift;
        var offsetAtMergeB = frameB.offset + frameB.width * targetDensity * 0.5 * -shift;

        var connectedA = a.ConnectedSegments();
        var connectedB = b.ConnectedSegments();

        var valueDelta = valueAtMergeB - valueAtMergeA;
        var offsetDelta = offsetAtMergeB - offsetAtMergeA;

        var tight = Math.Abs(frameA.angle - frameB.angle) < 5d;

        var interconnected = connectedA.Any(e => connectedB.Contains(e));

        if (interconnected)
        {
            var mutableDistA = a.LinearParents().Sum(s => s == a ? frameA.dist : s.Length);
            var mutableDistB = b.LinearParents().Sum(s => s == b ? frameB.dist : s.Length);

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

        var discardedBranches = a.Branches.ToList();
        var followingBranches = b.Branches.ToList();

        var orgLengthA = a.Length;
        var orgLengthB = b.Length;

        a.DetachAll();
        b.DetachAll();

        var endA = InsertArcWithDuct(a, ref frameA, arcA, ductA);
        var endB = InsertArcWithDuct(b, ref frameB, arcB, ductB);

        Segment InsertArcWithDuct(
            Segment segment,
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
                segment.TraceParams.PreventMerge = true;
                segment.Length = ductLength;

                #if DEBUG
                PathTracer.DebugOutput($"Inserted duct {segment.Id} with length {ductLength}");
                #endif
            }

            if (arcLength > 0)
            {
                segment = segment.InsertNew();
                segment.TraceParams.ApplyFixedAngle(arcAngle / arcLength, true);
                segment.TraceParams.PreventMerge = true;
                segment.Length = arcLength;

                #if DEBUG
                PathTracer.DebugOutput($"Inserted arc {segment.Id} with length {arcLength} and angle {arcAngle}");
                #endif
            }

            return segment;
        }

        var remainingLength = Math.Max(orgLengthA - a.Length, orgLengthB - b.Length);

        if (valueDelta != 0 || offsetDelta != 0)
        {
            if (interconnected)
            {
                var linearParentsA = tight ? a.LinearParents() : endA.LinearParents();
                var linearParentsB = tight ? b.LinearParents() : endB.LinearParents();

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
                foreach (var segment in connectedA.Where(segment => segment.IsRoot).Except(discardedBranches))
                {
                    segment.RelValue += valueDelta / 2;
                    segment.RelOffset += offsetDelta / 2;
                }

                foreach (var segment in connectedB.Where(segment => segment.IsRoot).Except(followingBranches))
                {
                    segment.RelValue -= valueDelta / 2;
                    segment.RelOffset -= offsetDelta / 2;
                }
            }
        }

        void ModifySegments(List<Segment> segments, double valueDiff, double offsetDiff, bool allowSingleFrames)
        {
            var totalSteps = segments.Sum(s => s.FullStepsCount(allowSingleFrames));

            var padding = totalSteps / 8;

            var currentSteps = 0;

            foreach (var segment in segments)
            {
                var fullSteps = segment.FullStepsCount(allowSingleFrames);

                if (fullSteps > 0)
                {
                    segment.ApplyDelta(new SmoothDelta(valueDiff, offsetDiff, totalSteps, currentSteps, padding));
                    currentSteps += fullSteps;
                }
            }
        }

        if (frameA.density != frameB.density)
        {
            if (endA.Length > 0) endA.TraceParams.DensityLoss = (frameA.density - targetDensity) / endA.Length;
            if (endB.Length > 0) endB.TraceParams.DensityLoss = (frameB.density - targetDensity) / endB.Length;
        }

        if (resultTrimA < 0 || resultTrimB < 0) remainingLength = 0;
        else
        {
            if (resultTrimA > 0 && remainingLength > resultTrimA) remainingLength = resultTrimA;
            if (resultTrimB > 0 && remainingLength > resultTrimB) remainingLength = resultTrimB;
        }

        if (remainingLength > 0)
        {
            var merged = new Segment(endA.Path)
            {
                TraceParams = frameA.width > frameB.width ? a.TraceParams : b.TraceParams,
                Length = remainingLength
            };

            if (shift < 0) // order matters for TraceCollision.TraverseEnclosed
            {
                endA.Attach(merged);
                endB.Attach(merged);
            }
            else
            {
                endB.Attach(merged);
                endA.Attach(merged);
            }

            foreach (var branch in followingBranches)
            {
                merged.Attach(branch);

                #if DEBUG
                PathTracer.DebugOutput($"Re-attached branch {branch.Id}");
                #endif
            }
        }
        else
        {
            foreach (var branch in followingBranches)
            {
                if (shift < 0) // order matters for TraceCollision.TraverseEnclosed
                {
                    endA.Attach(branch);
                    endB.Attach(branch);
                }
                else
                {
                    endB.Attach(branch);
                    endA.Attach(branch);
                }

                #if DEBUG
                PathTracer.DebugOutput($"Re-attached branch {branch.Id} to both ends");
                #endif
            }
        }

        foreach (var branch in discardedBranches)
        {
            branch.Discard();

            #if DEBUG
            PathTracer.DebugOutput($"Discarded branch {branch.Id}");
            #endif
        }

        return true;
    }

    private ArcCalcResult TryCalcArcs(
        TraceTask taskA, TraceTask taskB,
        Vector2d normal, double shift,
        ref TraceFrame frameA, ref TraceFrame frameB,
        out double arcLengthA, out double arcLengthB,
        out double ductLengthA, out double ductLengthB, int debugGroup)
    {
        var a = taskA.segment;
        var b = taskB.segment;

        var arcAngleA = -Vector2d.SignedAngle(frameA.normal, normal);
        var arcAngleB = -Vector2d.SignedAngle(frameB.normal, normal);

        // calculate the vector between the end points of the arcs

        var shiftDir = shift > 0 ? normal.PerpCW : normal.PerpCCW;
        var shiftSpan = (0.5 * frameA.width + 0.5 * frameB.width) * shiftDir;

        // calculate min arc lengths based on width and tenacity

        var widthForTenacityA = a.TraceParams.StaticAngleTenacity ? _tracer.TraceResults[a].initialFrame.width : frameA.width;
        var widthForTenacityB = b.TraceParams.StaticAngleTenacity ? _tracer.TraceResults[b].initialFrame.width : frameB.width;

        widthForTenacityA *= a.TraceParams.MaxExtentFactor(_tracer, taskA, frameA.pos - _tracer.GridMargin, frameA.dist);
        widthForTenacityB *= b.TraceParams.MaxExtentFactor(_tracer, taskB, frameB.pos - _tracer.GridMargin, frameB.dist);

        arcLengthA = arcAngleA.Abs() / 180 * widthForTenacityA * Math.PI / (1 - a.TraceParams.AngleTenacity.WithMax(0.9));
        arcLengthB = arcAngleB.Abs() / 180 * widthForTenacityB * Math.PI / (1 - b.TraceParams.AngleTenacity.WithMax(0.9));

        ductLengthA = 0;
        ductLengthB = 0;

        // check angle locks of each segment

        if (frameA.dist < taskA.TurnLockRight(false) && arcAngleA > 0) return ArcCalcResult.ExcAngleLock;
        if (frameA.dist < taskA.TurnLockLeft(false) && arcAngleA < 0) return ArcCalcResult.ExcAngleLock;
        if (frameB.dist < taskB.TurnLockRight(false) && arcAngleB > 0) return ArcCalcResult.ExcAngleLock;
        if (frameB.dist < taskB.TurnLockLeft(false) && arcAngleB < 0) return ArcCalcResult.ExcAngleLock;

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

        var widthForTenacityG = frameB.width * b.TraceParams.MaxExtentFactor(_tracer, taskB, pointG - _tracer.GridMargin, frameB.dist);
        var arcAngleMaxB = MathUtil.AngleLimit(widthForTenacityG, b.TraceParams.AngleTenacity) * arcLengthB;

        if (Math.Round(arcAngleB.Abs()) > arcAngleMaxB)
        {
            #if DEBUG
            PathTracer.DebugOutput($"The arc angle {arcAngleB} is larger than the limit {arcAngleMaxB}");
            #endif

            return ArcCalcResult.ExcMaxAngle;
        }

        // check that ducts are not excessively long

        if (ductLengthA > Math.Max(0.5 * a.TraceParams.ArcStableRange, a.TraceParams.StepSize)) return ArcCalcResult.ExcessiveDuct;
        if (ductLengthB > Math.Max(0.5 * b.TraceParams.ArcStableRange, b.TraceParams.StepSize)) return ArcCalcResult.ExcessiveDuct;

        // check that the end points of the arcs and the area beyond are not obstructed

        var cap1 = arcEndPosA - shiftDir * (0.5 * frameA.width + 1);
        var cap2 = arcEndPosA + shiftDir * 0.5 * frameA.width;
        var cap3 = arcEndPosA + shiftDir * (0.5 * frameA.width + frameB.width + 1);

        for (int s = 0; s < 2 * (frameA.width + frameB.width); s += 5)
        {
            if (CheckForObstruction(cap1 + normal * s)) return ArcCalcResult.Obstructed;
            if (CheckForObstruction(cap2 + normal * s)) return ArcCalcResult.Obstructed;
            if (CheckForObstruction(cap3 + normal * s)) return ArcCalcResult.Obstructed;
        }

        bool CheckForObstruction(Vector2d pos)
        {
            var rx = (int) Math.Round(pos.x);
            var rz = (int) Math.Round(pos.z);

            #if DEBUG
            PathTracer.DebugLine(new TraceDebugLine(_tracer, pos, 2));
            #endif

            if (rx < 0 || rz < 0 || rx >= _tracer.GridOuterSize.x || rz >= _tracer.GridOuterSize.z) return false;
            if (_tracer._mainGrid[rx, rz] <= 0) return false;

            var other = _tracer._taskGrid[rx, rz].segment;
            return other != null && !a.IsParentOf(other, true) && !b.IsParentOf(other, true);
        }

        // everything is good

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
        ArcOverlap, // arcs curve in the same direction and are located is such a way that they would overlap -> go back in frames
        ExcessiveDuct, // the length of at least one duct is excessive -> go back in frames
        Obstructed // the end point of at least one point is obstructed by another segment that has already been traced -> abort merge attempt
    }

    /// <summary>
    /// Attempt to divert one of the involved path segments in order to avoid the given collision.
    /// </summary>
    /// <returns>true if one of the segments was diverted successfully, otherwise false</returns>
    private bool TryDivert(TraceCollision c, bool passiveBranch)
    {
        Segment segmentD, segmentP;
        TraceFrame frameD, frameP;

        if (passiveBranch)
        {
            if (c.hasMergeB || c.cyclic) return false;

            segmentP = c.taskA.segment;
            segmentD = c.taskB.segment;

            frameP = c.frameA;
            frameD = c.frameB;
        }
        else
        {
            if (c.hasMergeA) return false;

            segmentP = c.taskB.segment;
            segmentD = c.taskA.segment;

            frameP = c.frameB;
            frameD = c.frameA;
        }

        if (segmentD.TraceParams.Target != null) return false;
        if (segmentD.TraceParams.ArcRetraceRange <= 0) return false;
        if (segmentD.TraceParams.ArcRetraceFactor <= 0) return false;

        var existingCount = segmentD.TraceParams.DiversionPoints?.Count ?? 0;
        if (existingCount >= MaxDiversionPoints) return false;

        var divertableSegments = segmentD.ConnectedSegments(false, true,
            s => s.BranchCount == 1 || !s.AnyBranchesMatch(b => b == segmentP || b.ParentCount > 1, false),
            s => s.ParentCount == 1 && s != segmentP
        );

        var divertableLength = divertableSegments.Sum(DivertableLength);

        if (divertableLength < DiversionMinLength) return false;

        var normal = frameP.perpCCW * Vector2d.PointToLineOrientation(frameP.pos, frameP.pos + frameP.normal, frameD.pos);

        var perpDotD = Vector2d.PerpDot((frameP.pos - frameD.pos).Normalized, frameD.normal);
        var perpDotP = Vector2d.PerpDot((frameD.pos - frameP.pos).Normalized, frameP.normal);

        // this is low in the case of a frontal/head-on collision
        var perpScore = 0.5 * (perpDotD.Abs() + perpDotP.Abs());

        var factor = segmentD.TraceParams.ArcRetraceFactor;
        var range = frameD.width / 2 + segmentD.TraceParams.ArcRetraceRange * (1 + existingCount / (double) MaxDiversionPoints);

        var diversion = normal;

        if (c.cyclic)
        {
            diversion = 0.5 * normal - 0.5 * frameP.normal;
            range = 0.5 * divertableLength;
        }
        else if (perpScore > 0.2)
        {
            diversion = Vector2d.Reflect(frameD.normal, normal);
        }

        var point = new DiversionPoint(frameD.pos, diversion * factor, range);

        var divertedLength = 0d;

        foreach (var segment in divertableSegments)
        {
            var divertableInSegment = DivertableLength(segment);

            var adjSegment = segment;

            if (divertedLength + divertableInSegment > range && c.cyclic)
            {
                var anchorAt = segment == segmentD ? frameD.dist : segment.Length;
                var splitAt = anchorAt - (range - divertedLength);

                adjSegment = segment.InsertNew();
                adjSegment.Length = segment.Length - splitAt;
                segment.Length = splitAt;
                divertedLength = range;
            }

            divertedLength += divertableInSegment;

            adjSegment.TraceParams.AddDiversionPoint(point);

            #if DEBUG
            PathTracer.DebugOutput($"Added diversion to {adjSegment.Id} with data {point}");
            #endif

            if (divertedLength >= range) break;
        }

        #if DEBUG
        PathTracer.DebugLine(new TraceDebugLine(_tracer, frameD.pos, 4, 0, $"DL {divertedLength:F2}\nPSC {perpScore:F2}\nR {range:F2}"));
        PathTracer.DebugLine(new TraceDebugLine(_tracer, frameD.pos, frameD.pos + diversion, 4));
        PathTracer.DebugLine(new TraceDebugLine(_tracer, frameP.pos, frameP.pos + normal, 4));
        #endif

        return true;

        double DivertableLength(Segment segment)
        {
            if (segment == segmentD)
            {
                if (segmentD == segmentP)
                {
                    return frameD.dist - frameP.dist;
                }

                return frameD.dist;
            }

            if (segment == segmentP)
            {
                return segmentP.Length - frameP.dist;
            }

            return segment.Length;
        }
    }

    /// <summary>
    /// Attempt to stabilize the extent of one of the involved path segments in order to avoid the given collision.
    /// </summary>
    /// <returns>true if the segment was modified successfully, otherwise false</returns>
    private bool TryStabilizeExtent(TraceCollision c, bool passiveBranch)
    {
        Segment segment;
        List<TraceFrame> frames;
        double progress;
        double shift;

        if (passiveBranch)
        {
            segment = c.taskB.segment;
            frames = c.framesB;
            progress = c.progressB;
            shift = c.shiftB;
        }
        else
        {
            segment = c.taskA.segment;
            frames = c.framesA;
            progress = c.progressA;
            shift = c.shiftA;
        }

        var existingCount = segment.TraceParams.StabilityPoints?.Count ?? 0;
        if (existingCount >= MaxStabilityPoints) return false;

        var frame1 = frames[frames.Count - 2];
        var frame2 = frames[frames.Count - 1];

        var baseExtent = progress.Lerp(frame1.width, frame2.width) / 2;
        if (shift.Abs() <= baseExtent) return false;

        var point1 = new StabilityPoint(frame1.pos, frame1.width / 4);
        var point2 = new StabilityPoint(frame2.pos, frame2.width / 4);

        foreach (var connectedSegment in segment.ConnectedSegments(true, false))
        {
            connectedSegment.TraceParams.AddStabilityPoint(point1);
            connectedSegment.TraceParams.AddStabilityPoint(point2);
        }

        foreach (var connectedSegment in segment.ConnectedSegments(false))
        {
            connectedSegment.TraceParams.AddStabilityPoint(point1);
            connectedSegment.TraceParams.AddStabilityPoint(point2);
        }

        return true;
    }

    /// <summary>
    /// Attempt to simplify the preceding segment chains in order to avoid the given collision.
    /// </summary>
    /// <returns>true if one of the segment chains was adjusted successfully, otherwise false</returns>
    private bool TrySimplify(TraceCollision c, bool passiveBranch)
    {
        if (passiveBranch ? c.hasMergeB : c.hasMergeA) return false;

        var segment = passiveBranch ? c.taskB.segment : c.taskA.segment;
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
    /// Attempt to increase tenacity of involved segments in order to avoid the given collision.
    /// </summary>
    /// <returns>true if any segments were adjusted successfully, otherwise false</returns>
    private bool TryApplyTenacity(TraceCollision c)
    {
        if (c.hasMergeB || !c.cyclic) return false;

        var segments = c.taskA.segment.ConnectedSegments(false, true, s => s != c.taskB.segment, s => s.ParentCount == 1);

        if (segments.Count == 0) segments.Add(c.taskA.segment);

        var segmentsAdjusted = 0;

        foreach (var segment in segments)
        {
            var newTenacity = segment.TraceParams.AngleTenacity + TenacityAdjStep;
            if (newTenacity <= TenacityAdjMax)
            {
                #if DEBUG
                PathTracer.DebugOutput($"Adjusting tenacity of segment {segment.Id} to {newTenacity}");
                #endif

                segment.TraceParams.AngleTenacity = newTenacity;
                segmentsAdjusted++;
            }
        }

        return segmentsAdjusted > 0;
    }

    /// <summary>
    /// Stub the given path segment in order to avoid a collision.
    /// </summary>
    private void Stub(Segment segment, double toLength)
    {
        var lengthDiff = toLength - StubBacktrackLength - segment.Length;

        while (segment.Length + lengthDiff < MinStubLengthFor(segment))
        {
            if (segment.ParentCount == 0) break;

            if (segment.ParentCount > 1 && segment.AnyBranchesMatch(b => b.ParentCount > 1, false))
            {
                foreach (var parent in segment.Parents.ToList())
                {
                    if (parent.RelWidth > 0) Stub(parent, parent.Length);
                }

                return;
            }

            lengthDiff += segment.Length;

            segment = segment.Parents.OrderBy(s => _tracer.TraceResults[s].initialFrame.width).First();

            if (segment.BranchCount > 1)
            {
                lengthDiff = MinStubLengthFor(segment) - segment.Length;
                break;
            }
        }

        segment.Length += lengthDiff;

        if (segment.IsRoot || segment.Length <= 0)
        {
            segment.Discard();

            #if DEBUG
            PathTracer.DebugOutput($"Discarded stub segment {segment.Id}");
            #endif
        }
        else
        {
            var initialFrame = _tracer.TraceResults[segment].initialFrame;

            segment.TraceParams.WidthLoss = initialFrame.width / segment.Length;
            segment.TraceParams.DensityLoss = -3 * initialFrame.density / segment.Length;
            segment.TraceParams.SpeedLoss = -3 * initialFrame.speed / segment.Length;

            segment.TraceParams.StaticAngleTenacity = true;
            segment.TraceParams.AdjustmentPriority = true;

            #if DEBUG
            PathTracer.DebugOutput($"Stubbed segment {segment.Id}");
            #endif
        }

        segment.DetachAll(true);
    }

    private double MinStubLengthFor(Segment segment)
    {
        return 2 * _tracer.TraceResults[segment].initialFrame.width;
    }
}
