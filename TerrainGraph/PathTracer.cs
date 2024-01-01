using System;
using System.Collections.Generic;
using System.Linq;
using TerrainGraph.Util;
using static TerrainGraph.Path;
using static TerrainGraph.GridFunction;

namespace TerrainGraph;

public class HotSwappableAttribute : Attribute;

[HotSwappable]
public class PathTracer
{
    private const int MaxTraceFrames = 1_000_000;

    private const double RadialThreshold = 0.5;

    public readonly Vector2d GridInnerSize;
    public readonly Vector2d GridOuterSize;
    public readonly Vector2d GridMargin;

    public readonly double TraceInnerMargin;
    public readonly double TraceOuterMargin;

    private readonly double[,] _mainGrid;
    private readonly double[,] _valueGrid;
    private readonly double[,] _offsetGrid;
    private readonly double[,] _distanceGrid;
    private readonly Segment[,] _segmentGrid;

    public IGridFunction<double> MainGrid => BuildGridFunction(_mainGrid);
    public IGridFunction<double> ValueGrid => BuildGridFunction(_valueGrid);
    public IGridFunction<double> OffsetGrid => BuildGridFunction(_offsetGrid);
    public IGridFunction<double> DistanceGrid => BuildGridFunction(_distanceGrid);

    private readonly GridKernel _followGridKernel = GridKernel.Square(3, 5);
    private readonly GridKernel _avoidGridKernel = GridKernel.Shield(2, 5, 3);

    private readonly IGridFunction<double> _overlapAvoidanceGrid = Zero;

    private List<TraceFrame> _frameBuffer = new(50);

    private int _totalFramesCalculated;

    public static Action<string> DebugOutput = _ => {};

    public PathTracer(
        int innerSizeX, int innerSizeZ, int gridMargin,
        double traceInnerMargin, double traceOuterMargin)
    {
        TraceInnerMargin = traceInnerMargin.WithMin(0);
        TraceOuterMargin = traceOuterMargin.WithMin(TraceInnerMargin);

        gridMargin = gridMargin.WithMin(0);

        innerSizeX = innerSizeX.WithMin(0);
        innerSizeZ = innerSizeZ.WithMin(0);

        GridMargin = new Vector2d(gridMargin, gridMargin);

        var outerSizeX = innerSizeX + gridMargin * 2;
        var outerSizeZ = innerSizeZ + gridMargin * 2;

        GridInnerSize = new Vector2d(innerSizeX, innerSizeZ);
        GridOuterSize = new Vector2d(outerSizeX, outerSizeZ);

        _mainGrid = new double[outerSizeX, outerSizeZ];
        _valueGrid = new double[outerSizeX, outerSizeZ];
        _offsetGrid = new double[outerSizeX, outerSizeZ];
        _distanceGrid = new double[outerSizeX, outerSizeZ];
        _segmentGrid = new Segment[outerSizeX, outerSizeZ];

        if (TraceOuterMargin > 0)
        {
            _overlapAvoidanceGrid = new ScaleWithBias(
                new Cache<double>(_distanceGrid, TraceOuterMargin), 1 / TraceOuterMargin, -1
            );
        }

        for (int x = 0; x < outerSizeX; x++)
        {
            for (int z = 0; z < outerSizeZ; z++)
            {
                _distanceGrid[x, z] = TraceOuterMargin;
            }
        }
    }

    /// <summary>
    /// Attempt to trace the given path, trying again in case of a collision.
    /// </summary>
    /// <param name="path">Path to trace</param>
    /// <param name="maxAttempts">Limit for the number of trace attempts</param>
    /// <returns>True if an attempt was successful, otherwise false</returns>
    public bool Trace(Path path, int maxAttempts = 50)
    {
        _totalFramesCalculated = 0;

        var simulatedCollisions = new List<PathCollision>();
        var occuredCollisions = new List<PathCollision>();

        for (int attempt = 0; attempt < maxAttempts - 1; attempt++)
        {
            TryTrace(path, occuredCollisions);
            if (occuredCollisions.Count == 0) return true;
            Clear();

            simulatedCollisions.AddRange(occuredCollisions);
            occuredCollisions.Clear();

            TryTrace(path, occuredCollisions, simulatedCollisions);
            if (occuredCollisions.Count == 0) return true;
            Clear();

            HandleFirstCollision(simulatedCollisions);

            simulatedCollisions.Clear();
            occuredCollisions.Clear();
        }

        TryTrace(path, occuredCollisions);
        return occuredCollisions.Count == 0;
    }

    public void Clear()
    {
        for (int x = 0; x < GridOuterSize.x; x++)
        {
            for (int z = 0; z < GridOuterSize.z; z++)
            {
                _mainGrid[x, z] = 0;
                _valueGrid[x, z] = 0;
                _offsetGrid[x, z] = 0;
                _distanceGrid[x, z] = TraceOuterMargin;
                _segmentGrid[x, z] = null;
            }
        }
    }

    /// <summary>
    /// Attempt to trace the given path once.
    /// </summary>
    /// <param name="path">Path to trace</param>
    /// <param name="occuredCollisions">Collisions that occur while tracing will be added to this list</param>
    /// <param name="simulatedCollisions">List of collisions to be simulated, may be null if there are none</param>
    /// <returns>Null if the path was fully traced, otherwise the collision that occured</returns>
    private void TryTrace(Path path, List<PathCollision> occuredCollisions, List<PathCollision> simulatedCollisions = null)
    {
        var taskQueue = new Queue<TraceTask>();
        var taskResults = new Dictionary<Segment, TraceResult>();
        var originFrame = new TraceFrame(GridMargin);

        foreach (var rootSegment in path.Roots)
        {
            Enqueue(rootSegment, originFrame);
        }

        while (taskQueue.Count > 0)
        {
            var task = taskQueue.Dequeue();

            var result = TryTrace(task);

            taskResults[task.segment] = result;

            if (result.collision != null)
            {
                occuredCollisions.Add(result.collision);
            }

            if (result.finalFrame.width > 0)
            {
                foreach (var branch in task.segment.Branches)
                {
                    if (branch.ParentIds.Count <= 1)
                    {
                        Enqueue(branch, result.finalFrame);
                    }
                    else
                    {
                        if (branch.Parents.Any(p => !taskResults.ContainsKey(p))) continue;

                        var parentResults = branch.Parents.Select(p => taskResults[p]).ToList();
                        var mergedFrame = new TraceFrame(parentResults);

                        DebugOutput($"Merged frame result: {mergedFrame}");

                        Enqueue(branch, mergedFrame);
                    }
                }
            }
        }

        return;

        void Enqueue(Segment branch, TraceFrame baseFrame)
        {
            if (taskResults.ContainsKey(branch) || taskQueue.Any(t => t.segment == branch)) return;

            var collisionList = simulatedCollisions?.Where(c => c.segmentB == branch).ToList();

            if (collisionList is { Count: 0 }) collisionList = null;

            var marginHead = branch.IsLeaf ? TraceInnerMargin : 0;
            var marginTail = branch.IsRoot ? TraceInnerMargin : 0;

            taskQueue.Enqueue(new TraceTask(branch, baseFrame, collisionList, marginHead, marginTail));
        }
    }

    private readonly struct TraceTask
    {
        /// <summary>
        /// The path segment that this task should trace.
        /// </summary>
        public readonly Segment segment;

        /// <summary>
        /// The trace frame containing the starting parameters for this path segment.
        /// </summary>
        public readonly TraceFrame baseFrame;

        /// <summary>
        /// Collisions with other path segments to be simulated, may be null if there are none.
        /// </summary>
        public readonly IEnumerable<PathCollision> simulated;

        /// <summary>
        /// The additional path length to trace at the head end of the segment.
        /// </summary>
        public readonly double marginHead;

        /// <summary>
        /// The additional path length to trace at the tail end of the segment.
        /// </summary>
        public readonly double marginTail;

        public TraceTask(
            Segment segment, TraceFrame baseFrame,
            IEnumerable<PathCollision> simulated,
            double marginHead, double marginTail)
        {
            this.segment = segment;
            this.baseFrame = baseFrame;
            this.simulated = simulated;
            this.marginHead = marginHead;
            this.marginTail = marginTail;
        }
    }

    private class TraceResult
    {
        /// <summary>
        /// The final trace frame that resulted from tracing the path segment.
        /// </summary>
        public readonly TraceFrame finalFrame;

        /// <summary>
        /// Information about a collision with another path segment, if any occured.
        /// </summary>
        public readonly PathCollision collision;

        public TraceResult(TraceFrame finalFrame, PathCollision collision = null)
        {
            this.finalFrame = finalFrame;
            this.collision = collision;
        }
    }

    [HotSwappable]
    private readonly struct TraceFrame
    {
        /// <summary>
        /// The absolute position in the grid.
        /// </summary>
        public readonly Vector2d pos;

        /// <summary>
        /// The unit vector pointing in the current direction.
        /// </summary>
        public readonly Vector2d normal;

        /// <summary>
        /// The angle in degrees pointing in the current direction.
        /// Note: Positive angles rotate clockwise in x/z grid space.
        /// </summary>
        public readonly double angle;

        /// <summary>
        /// The path width at the current position.
        /// </summary>
        public readonly double width;

        /// <summary>
        /// The rate of value change at the current position.
        /// </summary>
        public readonly double speed;

        /// <summary>
        /// The output value at the current position.
        /// </summary>
        public readonly double value;

        /// <summary>
        /// The offset at the current position.
        /// </summary>
        public readonly double offset;

        /// <summary>
        /// The offset density at the current position.
        /// </summary>
        public readonly double density;

        /// <summary>
        /// The total distance traveled so far from the start of the segment.
        /// </summary>
        public readonly double dist;

        /// <summary>
        /// Multipliers based on the trace parameters at the current position.
        /// </summary>
        public readonly LocalFactors factors;

        /// <summary>
        /// The path width at the current position, with local multiplier applied.
        /// </summary>
        public double widthMul => width * factors.width.ScaleAround(1, factors.scalar);

        /// <summary>
        /// The rate of value change at the current position, with local multiplier applied.
        /// </summary>
        public double speedMul => speed * factors.speed.ScaleAround(1, factors.scalar);

        /// <summary>
        /// The offset density at the current position, with local multiplier applied.
        /// </summary>
        public double densityMul => density * factors.density.ScaleAround(1, factors.scalar);

        /// <summary>
        /// The unit vector perpendicular clockwise to the current direction.
        /// </summary>
        public Vector2d perpCW => normal.PerpCW;

        /// <summary>
        /// The unit vector perpendicular counter-clockwise to the current direction.
        /// </summary>
        public Vector2d perpCCW => normal.PerpCCW;

        /// <summary>
        /// Construct an origin frame at the given position.
        /// </summary>
        public TraceFrame(Vector2d pos)
        {
            this.width = 1;
            this.speed = 1;
            this.density = 1;
            this.pos = pos;
            this.normal = new Vector2d(1, 0);
            this.factors = new LocalFactors();
        }

        /// <summary>
        /// Construct the initial frame for tracing the given path segment.
        /// </summary>
        /// <param name="parent">Last frame of the preceding path segment</param>
        /// <param name="segment">Next path segment to trace</param>
        /// <param name="gridOffset">Offset applied when retrieving factors from external grids</param>
        /// <param name="distOffset">Offset applied to the initial trace distance</param>
        public TraceFrame(TraceFrame parent, Segment segment, Vector2d gridOffset, double distOffset = 0)
        {
            this.angle = (parent.angle + segment.RelAngle).NormalizeDeg();
            this.width = parent.width * segment.RelWidth - distOffset * segment.TraceParams.WidthLoss;
            this.speed = parent.speed * segment.RelSpeed - distOffset * segment.TraceParams.SpeedLoss;
            this.value = parent.value + segment.RelValue + distOffset * (distOffset < 0 ? speed : parent.speed);
            this.offset = parent.offset + segment.RelOffset - segment.RelShift * parent.widthMul * parent.densityMul;
            this.normal = Vector2d.Direction(-angle);
            this.pos = parent.pos + segment.RelPosition + segment.RelShift * parent.perpCCW * parent.widthMul + distOffset * normal;
            this.factors = new LocalFactors(segment, pos - gridOffset, distOffset);
            this.density = parent.density * segment.RelDensity;
            this.dist = distOffset;
        }

        public TraceFrame(List<TraceResult> mergingSegments)
        {
            foreach (var result in mergingSegments)
            {
                this.normal += result.finalFrame.normal;
                this.width += result.finalFrame.width;
                this.speed += result.finalFrame.speed;
                this.value += result.finalFrame.value;
                this.density += result.finalFrame.density;
            }

            var widthAvg = this.width / mergingSegments.Count;

            foreach (var result in mergingSegments)
            {
                var widthFactor = result.finalFrame.width / widthAvg;

                this.pos += result.finalFrame.pos * widthFactor;
                this.offset += result.finalFrame.offset * widthFactor;
            }

            this.pos /= mergingSegments.Count;
            this.normal /= mergingSegments.Count;
            this.speed /= mergingSegments.Count;
            this.value /= mergingSegments.Count;
            this.offset /= mergingSegments.Count;
            this.density /= mergingSegments.Count;

            this.angle = -Vector2d.SignedAngle(new Vector2d(1, 0), this.normal);
            this.factors = new LocalFactors();
        }

        private TraceFrame(
            Vector2d pos, Vector2d normal,
            double angle, double width, double speed,
            double density, double value, double offset,
            double dist, LocalFactors factors)
        {
            this.pos = pos;
            this.normal = normal;
            this.angle = angle;
            this.width = width;
            this.speed = speed;
            this.density = density;
            this.value = value;
            this.offset = offset;
            this.dist = dist;
            this.factors = factors;
        }

        /// <summary>
        /// Move the frame forward in its current direction, returning the result as a new frame.
        /// </summary>
        /// <param name="segment">Segment with parameters defining how the values of the frame should evolve</param>
        /// <param name="distDelta">Distance to move forward in the current direction</param>
        /// <param name="angleDelta">Total angle change to be applied continuously while advancing</param>
        /// <param name="extraValue">Additional value delta to be applied continuously while advancing</param>
        /// <param name="extraOffset">Additional offset delta to be applied continuously while advancing</param>
        /// <param name="gridOffset">Offset applied when retrieving factors from external grids</param>
        /// <param name="pivotPoint">Pivot point resulting from the angle change and distance</param>
        /// <param name="pivotOffset">Signed distance of the pivot point from the frame position</param>
        /// <returns></returns>
        public TraceFrame Advance(
            Segment segment, double distDelta, double angleDelta, double extraValue, double extraOffset,
            Vector2d gridOffset, out Vector2d pivotPoint, out double pivotOffset)
        {
            var newAngle = (angle + angleDelta).NormalizeDeg();
            var newNormal = Vector2d.Direction(-newAngle);

            Vector2d newPos;

            if (Math.Abs(angleDelta) >= RadialThreshold)
            {
                pivotOffset = 180 * distDelta / (Math.PI * -angleDelta);
                pivotPoint = pos + perpCCW * pivotOffset;

                newPos = pivotPoint - newNormal.PerpCCW * pivotOffset;
            }
            else
            {
                pivotOffset = 0d;
                pivotPoint = pos;

                newPos = pos + distDelta * normal;
            }

            var newValue = value + extraValue;
            var newOffset = offset + extraOffset;

            newValue += distDelta * (dist >= 0 ? speedMul : speed);

            return new TraceFrame(newPos, newNormal, newAngle,
                width - distDelta * segment.TraceParams.WidthLoss,
                speed - distDelta * segment.TraceParams.SpeedLoss,
                density - distDelta * segment.TraceParams.DensityLoss,
                newValue, newOffset, dist + distDelta,
                new LocalFactors(segment, pos - gridOffset, dist)
            );
        }

        public override string ToString() =>
            $"{nameof(pos)}: {pos}, " +
            $"{nameof(angle)}: {angle}, " +
            $"{nameof(normal)}: {normal}, " +
            $"{nameof(width)}: {width}, " +
            $"{nameof(speed)}: {speed}, " +
            $"{nameof(value)}: {value}, " +
            $"{nameof(offset)}: {offset}, " +
            $"{nameof(density)}: {density}, " +
            $"{nameof(dist)}: {dist}, " +
            $"{nameof(factors)}: [{factors}]";
    }

    [HotSwappable]
    private readonly struct LocalFactors
    {
        /// <summary>
        /// Local width multiplier at the current frame position.
        /// </summary>
        public readonly double width = 1;

        /// <summary>
        /// Local speed multiplier at the current frame position.
        /// </summary>
        public readonly double speed = 1;

        /// <summary>
        /// Local density multiplier at the current frame position.
        /// </summary>
        public readonly double density = 1;

        /// <summary>
        /// Scalar applied to all local multipliers.
        /// </summary>
        public readonly double scalar = 1;

        public LocalFactors() {}

        public LocalFactors(Segment segment, Vector2d pos, double dist)
        {
            width = segment.TraceParams.WidthGrid?.ValueAt(pos) ?? 1;
            speed = segment.TraceParams.SpeedGrid?.ValueAt(pos) ?? 1;
            density = segment.TraceParams.DensityGrid?.ValueAt(pos) ?? 1;

            var progress = segment.Length <= 0 ? 0 : (dist / segment.Length).InRange01();

            scalar = 1 - progress.Lerp(segment.LocalStabilityAtTail, segment.LocalStabilityAtHead).InRange01();

            // if (scalar != 1) DebugOutput($"scalar at {pos + new Vector2d(3, 3)} is {scalar} and width is {width.ScaleAround(1, scalar)}");
        }

        public override string ToString() =>
            $"{nameof(width)}: {width}, " +
            $"{nameof(speed)}: {speed}, " +
            $"{nameof(density)}: {density}, " +
            $"{nameof(scalar)}: {scalar}";
    }

    [HotSwappable]
    private class PathCollision
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
        /// The length of first segment to adjust and retrace to avoid this collision.
        /// </summary>
        public double retraceRangeA => segmentA.TraceParams.ArcRetraceRange.WithMin(1);

        /// <summary>
        /// The length of second segment to adjust and retrace to avoid this collision.
        /// </summary>
        public double retraceRangeB => segmentB.TraceParams.ArcRetraceRange.WithMin(1);

        /// <summary>
        /// Whether the trace frames of both involved segments are available.
        /// </summary>
        public bool complete => framesA != null && framesB != null;

        public bool Precedes(PathCollision other)
        {
            if (segmentB.IsParentOf(other.segmentA, true)) return true;
            if (segmentB.IsParentOf(other.segmentB, false)) return true;
            if (!complete && !other.complete) return false;
            if (segmentB == other.segmentB && frameB.dist < other.frameB.dist) return true;
            return false;
        }

        public override string ToString() =>
            $"{nameof(segmentA)}: {segmentA.GetHashCode()}, " +
            $"{nameof(segmentB)}: {segmentB.GetHashCode()}, " +
            $"{nameof(frameA)}: {(framesA == null ? "?" : frameA)}, " +
            $"{nameof(frameB)}: {(framesB == null ? "?" : frameB)}, " +
            $"{nameof(position)}: {position}";
    }

    /// <summary>
    /// Attempt to trace a single path segment, with the parameters defined by the given task.
    /// </summary>
    /// <exception cref="Exception">Thrown if MaxTraceFrames is exceeded during this task</exception>
    private TraceResult TryTrace(TraceTask task)
    {
        var length = task.segment.Length;
        var extParams = task.segment.TraceParams;

        var stepSize = task.segment.TraceParams.StepSize.WithMin(1);

        var initialFrame = new TraceFrame(task.baseFrame, task.segment, GridMargin, -task.marginTail);

        DebugOutput($"Trace start with initial frame [{initialFrame}] and length {length}");

        if (length <= 0) return new TraceResult(initialFrame);

        var a = initialFrame;

        _frameBuffer.Clear();

        while (a.dist < length + task.marginHead)
        {
            _frameBuffer.Add(a);

            double distDelta = 0d;
            double angleDelta = 0d;
            double extraValue = 0d;
            double extraOffset = 0d;

            if (a.dist >= 0)
            {
                distDelta = Math.Min(stepSize, length + task.marginHead - a.dist);

                var followVec = Vector2d.Zero;

                if (extParams.AbsFollowGrid != null || extParams.RelFollowGrid != null)
                {
                    followVec = _followGridKernel.CalculateAt(
                        new(1, 0), new(0, 1),
                        extParams.AbsFollowGrid,
                        extParams.RelFollowGrid,
                        a.pos - GridMargin,
                        a.pos - initialFrame.pos,
                        initialFrame.angle - 90
                    );
                }

                if (extParams.AvoidOverlap > 0)
                {
                    followVec += extParams.AvoidOverlap * _avoidGridKernel.CalculateAt(
                        a.normal, a.perpCW,
                        _overlapAvoidanceGrid, null,
                        a.pos, Vector2d.Zero, 0
                    );
                }

                if (followVec != Vector2d.Zero)
                {
                    angleDelta -= Vector2d.SignedAngle(a.normal, a.normal + followVec);
                }

                if (extParams.SwerveGrid != null)
                {
                    angleDelta += extParams.SwerveGrid.ValueAt(a.pos - GridMargin);
                }

                var maxAngleDelta = (1 - extParams.AngleTenacity) * 180 * distDelta / (a.width * Math.PI);
                angleDelta = (distDelta * angleDelta).NormalizeDeg().InRange(-maxAngleDelta, maxAngleDelta);

                if (task.segment.SmoothDelta != null)
                {
                    var smoothDelta = task.segment.SmoothDelta;

                    if (smoothDelta.StepsTotal <= 0)
                    {
                        extraValue += smoothDelta.ValueDelta;
                        extraOffset += smoothDelta.OffsetDelta;
                    }
                    else
                    {
                        var stepsDone = (int) Math.Floor(a.dist / stepSize);
                        var stepsTotal = (int) Math.Floor(length / stepSize);

                        var pointer = stepsDone;
                        var factor = 1d;

                        if (stepsDone == stepsTotal - 1)
                        {
                            factor = stepSize / (stepSize + length % stepSize);
                        }
                        else if (stepsDone == stepsTotal && stepsTotal > 0)
                        {
                            pointer = stepsTotal - 1;
                            factor = length % stepSize / (stepSize + length % stepSize);
                        }

                        var n = smoothDelta.StepsTotal;
                        var x = smoothDelta.StepsStart + pointer;

                        if (n > smoothDelta.StepsPadding * 2)
                        {
                            n -= smoothDelta.StepsPadding * 2;
                            x -= smoothDelta.StepsPadding;
                        }

                        var value = x < 0 || x >= n ? 0 : MathUtil.LinearDist(n, x);

                        DebugOutput($"step {x} of {n} v {value} f {factor}");

                        extraValue += smoothDelta.ValueDelta * value * factor;
                        extraOffset += smoothDelta.OffsetDelta * value * factor;
                    }
                }
            }
            else
            {
                distDelta -= a.dist;
            }

            var b = a.Advance(
                task.segment, distDelta, angleDelta,
                extraValue, extraOffset, GridMargin,
                out var pivotPoint, out var pivotOffset
            );

            var extendA = a.widthMul / 2;
            var extendB = b.widthMul / 2;

            var boundIa = extendA + TraceInnerMargin;
            var boundIb = extendB + TraceInnerMargin;
            var boundOa = extendA + TraceOuterMargin;
            var boundOb = extendB + TraceOuterMargin;

            if (extendA <= 0 && a.dist >= 0)
            {
                length = Math.Min(length, b.dist);
            }

            var boundP1 = a.pos + a.perpCCW * boundOa;
            var boundP2 = a.pos + a.perpCW * boundOa;
            var boundP3 = b.pos + b.perpCCW * boundOb;
            var boundP4 = b.pos + b.perpCW * boundOb;

            var boundMin = Vector2d.Min(Vector2d.Min(boundP1, boundP2), Vector2d.Min(boundP3, boundP4));
            var boundMax = Vector2d.Max(Vector2d.Max(boundP1, boundP2), Vector2d.Max(boundP3, boundP4));

            var xMax = (int) Math.Min(Math.Ceiling(boundMax.x), GridOuterSize.x - 1);
            var zMax = (int) Math.Min(Math.Ceiling(boundMax.z), GridOuterSize.z - 1);

            var xMin = (int) Math.Max(Math.Floor(boundMin.x), 0);
            var zMin = (int) Math.Max(Math.Floor(boundMin.z), 0);

            for (int x = xMin; x <= xMax; x++)
            {
                for (int z = zMin; z <= zMax; z++)
                {
                    var pos = new Vector2d(x, z);

                    var dotA = Vector2d.Dot(a.normal, pos - a.pos);
                    var dotB = Vector2d.Dot(b.normal, pos - b.pos);

                    if (dotA >= 0 && dotB < 0)
                    {
                        double shift;
                        double shiftAbs;

                        double progress = 0.5;

                        if (pivotOffset != 0)
                        {
                            var pivotVec = pos - pivotPoint;

                            shift = Math.Sign(-angleDelta) * (pivotVec.Magnitude - Math.Abs(pivotOffset));
                            shiftAbs = Math.Abs(shift);

                            if (shiftAbs <= boundIa || shiftAbs <= boundIb)
                            {
                                progress = Vector2d.Angle(a.pos - pivotPoint, pivotVec) / Math.Abs(angleDelta);
                            }
                        }
                        else
                        {
                            shift = -Vector2d.PerpDot(a.normal, pos - a.pos);
                            shiftAbs = Math.Abs(shift);

                            progress = dotA / distDelta;
                        }

                        var extend = progress.Lerp(extendA, extendB);

                        if (shiftAbs <= extend + TraceOuterMargin)
                        {
                            var preDist = _distanceGrid[x, z];
                            var nowDist = shiftAbs - extend;

                            if (nowDist < preDist)
                            {
                                _distanceGrid[x, z] = nowDist;
                            }

                            if (shiftAbs <= extend + TraceInnerMargin)
                            {
                                var dist = a.dist + distDelta * progress;

                                var value = progress.Lerp(a.value, b.value);
                                var offset = progress.Lerp(a.offset, b.offset);
                                var density = progress.Lerp(a.densityMul, b.densityMul);

                                if (nowDist < preDist)
                                {
                                    _valueGrid[x, z] = value;
                                    _offsetGrid[x, z] = offset + shift * density;
                                }

                                if (shiftAbs <= extend && dist >= 0 && dist <= length)
                                {
                                    if (_mainGrid[x, z] > 0)
                                    {
                                        var collided = _segmentGrid[x, z];

                                        if (CanCollide(task.segment, collided, dist))
                                        {
                                            return new TraceResult(a, new PathCollision
                                            {
                                                segmentA = task.segment,
                                                segmentB = collided,
                                                framesA = ExchangeFrameBuffer(),
                                                position = pos
                                            });
                                        }
                                    }

                                    if (task.simulated != null)
                                    {
                                        foreach (var simulated in task.simulated)
                                        {
                                            if (simulated.position == pos && simulated.framesB == null)
                                            {
                                                simulated.framesB = ExchangeFrameBuffer(true);
                                            }
                                        }
                                    }

                                    _segmentGrid[x, z] = task.segment;
                                    _mainGrid[x, z] = extend;
                                }
                            }
                        }
                    }
                }
            }

            a = b;

            if (++_totalFramesCalculated > MaxTraceFrames)
            {
                throw new Exception("PathTracer exceeded frame limit");
            }
        }

        return new TraceResult(a);
    }

    private bool CanCollide(Segment active, Segment passive, double dist)
    {
        if (active.TraceParams.ArcRetraceRange <= 0 || passive.TraceParams.ArcRetraceRange <= 0) return false;
        if (active.ParentIds.ElementsEqual(passive.ParentIds) && dist < active.TraceParams.ArcRetraceRange) return false;
        return true;
    }

    /// <summary>
    /// Rewrite path segments such that the earliest of the given collisions is avoided.
    /// </summary>
    /// <param name="collisions">list of collisions that should be considered</param>
    private void HandleFirstCollision(List<PathCollision> collisions)
    {
        PathCollision first = null;

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
                DebugOutput($"Attempting merge: {first}");

                if (TryMerge(first))
                {
                    DebugOutput($"Path merge was successful");
                }
                else
                {
                    DebugOutput($"Path merge not possible, stubbing instead");
                    Stub(first);
                }
            }
            else
            {
                DebugOutput($"!!! Collision missing data: {first}");
                Stub(first);
            }
        }
    }

    /// <summary>
    /// Stub the active path segment in order to avoid the given collision.
    /// </summary>
    private void Stub(PathCollision c)
    {
        c.segmentA.Length = Math.Max(0, c.frameA.dist - c.retraceRangeA);
        c.segmentA.TraceParams.WidthLoss = c.framesA[0].width / c.segmentA.Length;
        c.segmentA.TraceParams.DensityLoss = -2 * c.framesA[0].density / c.segmentA.Length;
        c.segmentA.DetachAll();
    }

    /// <summary>
    /// Attempt to merge the involved path segments in order to avoid the given collision.
    /// </summary>
    /// <returns>true if the segments were merged successfully, otherwise false</returns>
    private bool TryMerge(PathCollision c)
    {
        Vector2d normal;

        if (Vector2d.TryIntersect(c.frameA.pos, c.frameB.pos, c.frameA.normal, c.frameB.normal, out var midpoint, 0.05))
        {
            normal = (c.frameA.normal * c.frameA.width + c.frameB.normal * c.frameB.width).Normalized;
        }
        else
        {
            var perpDot = Vector2d.PerpDot(c.frameA.normal, c.frameB.normal);
            normal = perpDot >= 0 ? c.frameA.normal.PerpCCW : c.frameA.normal.PerpCW;
            midpoint = c.position;
        }

        if (c.segmentA.IsBranchOf(c.segmentB, true)) return false;

        if (c.segmentA.AnyBranchesMatch(s => s.ParentIds.Count > 1, false)) return false;
        if (c.segmentB.AnyBranchesMatch(s => s.ParentIds.Count > 1, false)) return false;

        var shift = Math.Sign(Vector2d.PerpDot(normal, c.frameA.normal));

        for (int i = 0; i < 7; i++)
        {
            var range = Math.Max(c.retraceRangeA, c.retraceRangeB) * (1 + i * i * 0.25);
            var target = midpoint + normal * range;

            var orgLengthA = c.segmentA.Length;
            var orgLengthB = c.segmentB.Length;

            DebugOutput($"range: {range} target: {target} normal: {normal} perpDot: {Vector2d.PerpDot(c.frameA.normal, c.frameB.normal)}");

            var frameIdxA = CalcArcWithDuct(
                c.segmentA, c.framesA, target, normal.PerpCW * shift,
                c.retraceRangeA, out var arcLengthA, out var ductLengthA
            );

            if (frameIdxA < 0) continue;

            var frameIdxB = CalcArcWithDuct(
                c.segmentB, c.framesB, target, normal.PerpCCW * shift,
                c.retraceRangeB, out var arcLengthB, out var ductLengthB
            );

            if (frameIdxB < 0) continue;

            var connectedA = c.segmentA.ConnectedSegments();
            var connectedB = c.segmentB.ConnectedSegments();

            var frameA = c.framesA[frameIdxA];
            var frameB = c.framesB[frameIdxB];

            var valueAtMergeA = frameA.value + frameA.speed * (arcLengthA + ductLengthA);
            var valueAtMergeB = frameB.value + frameB.speed * (arcLengthB + ductLengthB);

            var offsetAtMergeA = frameA.offset + frameA.width * 0.5 * -shift;
            var offsetAtMergeB = frameB.offset + frameB.width * 0.5 * shift;

            var arcA = InsertArcWithDuct(c.segmentA, ref frameA, arcLengthA, ductLengthA);
            var arcB = InsertArcWithDuct(c.segmentB, ref frameB, arcLengthB, ductLengthB);

            arcA.DetachAll();
            arcB.DetachAll();

            if (connectedA.Any(e => connectedB.Contains(e)))
            {
                ModifyParents(arcA, valueAtMergeB - valueAtMergeA, offsetAtMergeB - offsetAtMergeA);
                ModifyParents(arcB, valueAtMergeA - valueAtMergeB, offsetAtMergeA - offsetAtMergeB);
            }
            else
            {
                ModifyRoots(connectedA, valueAtMergeB - valueAtMergeA, offsetAtMergeB - offsetAtMergeA);
                ModifyRoots(connectedB, valueAtMergeA - valueAtMergeB, offsetAtMergeA - offsetAtMergeB);
            }

            void ModifyParents(Segment segment, double valueDiff, double offsetDiff)
            {
                var linearParents = new List<Segment>{segment};

                var totalSteps = segment.FullStepsCount(valueDiff > 0);

                while (linearParents.Count < 99)
                {
                    var current = linearParents.Last();
                    if (current.ParentIds.Count != 1) break;

                    var next = current.Parents.First();
                    if (next.BranchIds.Count != 1) break;

                    totalSteps += next.FullStepsCount(valueDiff > 0);
                    linearParents.Add(next);
                }

                linearParents.Reverse();

                var padding = totalSteps / 8;

                var currentSteps = 0;

                foreach (var parent in linearParents)
                {
                    var fullSteps = parent.FullStepsCount(valueDiff > 0);

                    if (fullSteps > 0)
                    {
                        parent.SmoothDelta = new SmoothDelta(0.5 * valueDiff, 0.5 * offsetDiff, totalSteps, currentSteps, padding);
                        currentSteps += fullSteps;
                    }
                }
            }

            void ModifyRoots(List<Segment> segments, double valueDiff, double offsetDiff)
            {
                foreach (var segment in segments.Where(segment => segment.IsRoot))
                {
                    segment.RelValue += 0.5 * valueDiff;
                    segment.RelOffset += 0.5 * offsetDiff;
                }
            }

            var remainingLength = Math.Max(orgLengthA - c.segmentA.Length, orgLengthB - c.segmentB.Length);

            var merged = new Segment(arcA.Path)
            {
                TraceParams = TraceParams.Merge(c.segmentA.TraceParams, c.segmentB.TraceParams),
                Length = remainingLength
            };

            var stabilityRangeA = c.segmentA.TraceParams.ArcStableRange.WithMin(1);
            var stabilityRangeB = c.segmentB.TraceParams.ArcStableRange.WithMin(1);

            arcA.ApplyLocalStabilityAtHead(stabilityRangeA / 2, stabilityRangeA / 2);
            arcB.ApplyLocalStabilityAtHead(stabilityRangeB / 2, stabilityRangeB / 2);

            merged.ApplyLocalStabilityAtTail(0, 0.5.Lerp(stabilityRangeA, stabilityRangeB) / 2);

            arcA.Attach(merged);
            arcB.Attach(merged);

            // TODO re-attach original branches to the merged one

            return true;
        }

        return false;

        Segment InsertArcWithDuct(
            Segment segment,
            ref TraceFrame frame,
            double arcLength,
            double ductLength)
        {
            segment.Length = frame.dist;

            var arcAngle = -Vector2d.SignedAngle(frame.normal, normal);

            var ductSegment = segment.InsertNew();
            ductSegment.TraceParams.ApplyFixedAngle(0, true);
            ductSegment.Length = ductLength;

            var arcSegment = ductSegment.InsertNew();
            arcSegment.TraceParams.ApplyFixedAngle(arcAngle / arcLength, true);
            arcSegment.Length = arcLength;

            return arcSegment;
        }

        int CalcArcWithDuct(
            Segment segment,
            List<TraceFrame> frames,
            Vector2d target,
            Vector2d shiftDir,
            double maxRange,
            out double arcLength,
            out double ductLength)
        {
            for (var frameIdx = frames.Count - 1; frameIdx >= 0; frameIdx--)
            {
                var frame = frames[frameIdx];

                var pointB = frame.pos;
                var pointC = target + shiftDir * 0.5 * frame.width;

                if (Vector2d.Distance(pointB, c.position) < maxRange && frameIdx > 0) continue;

                var arcAngle = -Vector2d.SignedAngle(frame.normal, normal);

                if (Vector2d.TryIntersect(pointB, pointC, frame.normal, normal, out var pointF, out var scalarB, 0.05))
                {
                    if (scalarB < 0) continue;

                    var scalarF = Vector2d.PerpDot(frame.normal, pointB - pointC) / Vector2d.PerpDot(frame.normal, normal);

                    if (scalarF > 0) continue;

                    // https://math.stackexchange.com/a/1572508
                    var distBF = Vector2d.Distance(pointB, pointF);
                    var distCF = Vector2d.Distance(pointC, pointF);

                    if (distBF >= distCF)
                    {
                        ductLength = distBF - distCF;

                        var pointG = pointB + frame.normal * ductLength;

                        if (Vector2d.TryIntersect(pointG, pointC, frame.perpCW, normal.PerpCW, out var pointK, 0.05))
                        {
                            var radius = Vector2d.Distance(pointG, pointK);
                            var chordLength = Vector2d.Distance(pointG, pointC);

                            // https://www.omnicalculator.com/math/arc-length
                            arcLength = 2 * radius * Math.Asin(0.5 * chordLength / radius);

                            DebugOutput($"pointB: {pointB} pointC: {pointC} pointF: {pointF} pointG: {pointG} pointK: {pointK}");
                            DebugOutput($"dist: {frame.dist} radius: {radius} chordLength: {chordLength} arcLength: {arcLength} ductLength: {ductLength}");

                            if (double.IsNaN(arcLength)) continue;

                            var arcAngleMax = (1 - segment.TraceParams.AngleTenacity) * 180 * arcLength / (frame.width * Math.PI);

                            DebugOutput($"arcAngle: {arcAngle} arcAngleMax: {arcAngleMax}");

                            return arcAngle.Abs() <= arcAngleMax ? frameIdx : -1;
                        }
                    }
                }
            }

            arcLength = 0;
            ductLength = 0;

            return -1;
        }
    }

    /// <summary>
    /// Wrap the given raw grid array in a GridFunction, transforming values into map space.
    /// </summary>
    private IGridFunction<double> BuildGridFunction(double[,] grid)
    {
        if (GridMargin == Vector2d.Zero) return new Cache<double>(grid);
        return new Transform<double>(new Cache<double>(grid), -GridMargin.x, -GridMargin.z, 1, 1);
    }

    /// <summary>
    /// Create and set a new trace frame buffer, returning the old one.
    /// </summary>
    private List<TraceFrame> ExchangeFrameBuffer(bool copy = false)
    {
        var buffer = _frameBuffer;
        _frameBuffer = copy ? new(buffer) : new(50);
        return buffer;
    }
}
