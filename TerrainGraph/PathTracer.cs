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

        for (int attempt = 0; attempt < maxAttempts - 1; attempt++)
        {
            var collisionA = TryTrace(path);
            if (collisionA == null) return true;
            Clear();

            var collisionB = TryTrace(path, collisionA);
            if (collisionB == null) return true;
            Clear();

            HandleCollision(collisionA, collisionB);
        }

        return TryTrace(path) == null;
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
    /// <param name="collision">Expected collision to stop at, if any</param>
    /// <returns>Null if the path was fully traced, otherwise the collision that occured</returns>
    private PathCollision TryTrace(Path path, PathCollision collision = null)
    {
        var taskQueue = new Queue<TraceTask>();
        var taskResults = new Dictionary<Segment, TraceResult>();

        foreach (var origin in path.Origins)
        {
            var baseFrame = new TraceFrame(origin, GridInnerSize, GridMargin);

            foreach (var branch in origin.Branches)
            {
                Enqueue(branch, baseFrame);
            }
        }

        while (taskQueue.Count > 0)
        {
            var task = taskQueue.Dequeue();

            var result = TryTrace(task);

            taskResults[task.segment] = result;

            if (result.collision != null)
            {
                return result.collision;
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

        return null;

        void Enqueue(Segment branch, TraceFrame baseFrame)
        {
            if (taskResults.ContainsKey(branch) || taskQueue.Any(t => t.segment == branch)) return;

            var stopAtCol = collision != null && collision.passiveSegment == branch ? collision : null;

            var marginHead = branch.IsLeaf ? TraceInnerMargin : 0;
            var marginTail = branch.IsRoot ? TraceInnerMargin : 0;

            taskQueue.Enqueue(new TraceTask(branch, baseFrame, stopAtCol, marginHead, marginTail));
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
        /// An expected collision with another path segment to stop at, if any.
        /// </summary>
        public readonly PathCollision collision;

        /// <summary>
        /// The additional path length to trace at the head end of the segment.
        /// </summary>
        public readonly double marginHead;

        /// <summary>
        /// The additional path length to trace at the tail end of the segment.
        /// </summary>
        public readonly double marginTail;

        public TraceTask(Segment segment, TraceFrame baseFrame, PathCollision collision, double marginHead, double marginTail)
        {
            this.segment = segment;
            this.baseFrame = baseFrame;
            this.collision = collision;
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
        public double widthMul => width * factors.width;

        /// <summary>
        /// The rate of value change at the current position, with local multiplier applied.
        /// </summary>
        public double speedMul => speed * factors.speed;

        /// <summary>
        /// The offset density at the current position, with local multiplier applied.
        /// </summary>
        public double densityMul => density * factors.density;

        /// <summary>
        /// The unit vector perpendicular clockwise to the current direction.
        /// </summary>
        public Vector2d perpCW => normal.PerpCW;

        /// <summary>
        /// The unit vector perpendicular counter-clockwise to the current direction.
        /// </summary>
        public Vector2d perpCCW => normal.PerpCCW;

        /// <summary>
        /// Construct the base frame for the given path origin.
        /// </summary>
        /// <param name="origin">Path origin that defines the frame</param>
        /// <param name="gridScalar">Multiplier applied to origin position</param>
        /// <param name="gridOffset">Offset applied to origin position</param>
        public TraceFrame(Origin origin, Vector2d gridScalar, Vector2d gridOffset)
        {
            this.angle = origin.BaseAngle;
            this.width = origin.BaseWidth;
            this.speed = origin.BaseSpeed;
            this.value = origin.BaseValue;
            this.normal = Vector2d.Direction(-angle);
            this.pos = origin.Position * gridScalar + gridOffset;
            this.factors = new LocalFactors();
            this.density = origin.BaseDensity;
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
            this.value = parent.value + (distOffset < 0 ? speed : parent.speed) * distOffset;
            this.offset = parent.offset - parent.width * parent.factors.width * parent.factors.density * segment.RelOffset;
            this.normal = Vector2d.Direction(-angle);
            this.pos = parent.pos + parent.perpCCW * parent.width * parent.factors.width * segment.RelOffset + distOffset * normal;
            this.factors = new LocalFactors(segment.TraceParams, pos - gridOffset);
            this.density = parent.density;
            this.dist = distOffset;
        }

        public TraceFrame(List<TraceResult> mergingSegments)
        {
            foreach (var result in mergingSegments)
            {
                this.pos += result.finalFrame.pos;
                this.normal += result.finalFrame.normal;
                this.width += result.finalFrame.width;
                this.speed += result.finalFrame.speed;
                this.value += result.finalFrame.value;
                this.density += result.finalFrame.density;
            }

            this.pos /= mergingSegments.Count;
            this.normal /= mergingSegments.Count;
            this.speed /= mergingSegments.Count;
            this.value /= mergingSegments.Count;
            this.density /= mergingSegments.Count;

            this.angle = -Vector2d.SignedAngle(new Vector2d(1, 0), this.normal);
            this.factors = new LocalFactors();

            this.offset = 0; // TODO
            this.dist = 0;
        }

        private TraceFrame(
            Vector2d pos, Vector2d normal,
            double angle, double width, double speed,
            double value, double offset, double density,
            double dist, LocalFactors factors)
        {
            this.pos = pos;
            this.normal = normal;
            this.angle = angle;
            this.width = width;
            this.speed = speed;
            this.value = value;
            this.offset = offset;
            this.density = density;
            this.dist = dist;
            this.factors = factors;
        }

        /// <summary>
        /// Move the frame forward in its current direction, returning the result as a new frame.
        /// </summary>
        /// <param name="extParams">Path parameters defining how the values of the frame should evolve</param>
        /// <param name="distDelta">Distance to move forward in the current direction</param>
        /// <param name="angleDelta">Total angle change to be applied continuously while advancing</param>
        /// <param name="gridOffset">Offset applied when retrieving factors from external grids</param>
        /// <param name="pivotPoint">Pivot point resulting from the angle change and distance</param>
        /// <param name="pivotOffset">Signed distance of the pivot point from the frame position</param>
        /// <returns></returns>
        public TraceFrame Advance(
            TraceParams extParams, double distDelta, double angleDelta,
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

            return new TraceFrame(newPos, newNormal, newAngle,
                width - distDelta * extParams.WidthLoss,
                speed - distDelta * extParams.SpeedLoss,
                value + distDelta * (dist >= 0 ? speedMul : speed),
                offset, density - distDelta * extParams.DensityLoss,
                dist + distDelta, new LocalFactors(extParams, pos - gridOffset)
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
        public readonly double width;

        /// <summary>
        /// Local speed multiplier at the current frame position.
        /// </summary>
        public readonly double speed;

        /// <summary>
        /// Local density multiplier at the current frame position.
        /// </summary>
        public readonly double density;

        public LocalFactors()
        {
            width = 1;
            speed = 1;
            density = 1;
        }

        public LocalFactors(TraceParams traceParams, Vector2d pos)
        {
            width = traceParams.WidthGrid?.ValueAt(pos) ?? 1;
            speed = traceParams.SpeedGrid?.ValueAt(pos) ?? 1;
            density = traceParams.DensityGrid?.ValueAt(pos) ?? 1;
        }

        public override string ToString() =>
            $"{nameof(width)}: {width}, " +
            $"{nameof(speed)}: {speed}, " +
            $"{nameof(density)}: {density}";
    }

    private class PathCollision
    {
        /// <summary>
        /// First segment involved in the collision, the one that was actively being traced.
        /// </summary>
        public Segment activeSegment;

        /// <summary>
        /// Second segment involved in the collision. May be the same as activeSegment if it collided with itself.
        /// </summary>
        public Segment passiveSegment;

        /// <summary>
        /// The position at which the collision occured.
        /// </summary>
        public Vector2d position;

        /// <summary>
        /// Trace frames of the active segment.
        /// </summary>
        public List<TraceFrame> frames;

        /// <summary>
        /// Current trace frame of the active segment at the time of the collision.
        /// </summary>
        public TraceFrame frame => frames[frames.Count - 1];

        /// <summary>
        /// The length of segment to adjust and retrace to avoid this collision.
        /// </summary>
        public double retraceRange => activeSegment.TraceParams.ArcRetraceRange.WithMin(1);

        public bool CorrespondsTo(PathCollision other) =>
            other.activeSegment == this.passiveSegment &&
            other.passiveSegment == this.activeSegment &&
            other.position == this.position;

        public override string ToString() =>
            $"{nameof(activeSegment)}: {activeSegment.GetHashCode()}, " +
            $"{nameof(passiveSegment)}: {passiveSegment.GetHashCode()}, " +
            $"{nameof(frame)}: {frame}, " +
            $"{nameof(position)}: {position}";
    }

    /// <summary>
    /// Attempt to trace a single path segment, with the parameters defined by the given task.
    /// </summary>
    /// <returns>result object containing the final frame and collision data (if any has occured)</returns>
    /// <exception cref="Exception">thrown if MaxTraceFrames is exceeded during this task</exception>
    private TraceResult TryTrace(TraceTask task)
    {
        var length = task.segment.Length;
        var extParams = task.segment.TraceParams;

        var stepSize = task.segment.TraceParams.StepSize.WithMin(1);

        var initialFrame = new TraceFrame(task.baseFrame, task.segment, GridMargin, -task.marginTail);

        DebugOutput($"Trace start with initial frame [{initialFrame}] and length {length}");

        var a = initialFrame;

        _frameBuffer.Clear();

        while (a.dist < length + task.marginHead)
        {
            _frameBuffer.Add(a);

            double distDelta = 0d;
            double angleDelta = 0d;

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
            }
            else
            {
                distDelta -= a.dist;
            }

            var b = a.Advance(extParams, distDelta, angleDelta, GridMargin, out var pivotPoint, out var pivotOffset);

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

                                        if (extParams.ArcRetraceRange > 0 && collided.TraceParams.ArcRetraceRange > 0)
                                        {
                                            return new TraceResult(a, new PathCollision
                                            {
                                                activeSegment = task.segment,
                                                passiveSegment = collided,
                                                frames = ExchangeFrameBuffer(),
                                                position = pos
                                            });
                                        }
                                    }

                                    if (task.collision != null && task.collision.position == pos)
                                    {
                                        return new TraceResult(a, new PathCollision
                                        {
                                            activeSegment = task.segment,
                                            passiveSegment = task.collision.activeSegment,
                                            frames = ExchangeFrameBuffer(),
                                            position = pos
                                        });
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

    /// <summary>
    /// Rewrite path segments such that the given collision is avoided.
    /// </summary>
    /// <param name="a">the collision from the perspective of the first path segment involved</param>
    /// <param name="b">the collision from the perspective of the second path segment involved</param>
    /// <exception cref="Exception">thrown if the given collision objects do not correspond to each other</exception>
    private void HandleCollision(PathCollision a, PathCollision b)
    {
        DebugOutput($"Path collision: A = [{a}] B = [{b}]");

        if (!a.CorrespondsTo(b))
        {
            throw new Exception("PathTracer internal consistency error");
        }

        if (a.passiveSegment.IsSupportOf(a.activeSegment) || !TryMerge(a, b))
        {
            a.activeSegment.Length = Math.Max(0, a.frame.dist - a.retraceRange);
            a.activeSegment.RemoveAllBranches();

            DebugOutput($"Path merge not possible");
        }
    }

    /// <summary>
    /// Attempt to merge the involved path segments in order to avoid the given collision.
    /// </summary>
    /// <param name="a">the collision from the perspective of the first path segment involved</param>
    /// <param name="b">the collision from the perspective of the second path segment involved</param>
    /// <returns>true if the segments were merged successfully, otherwise false</returns>
    private bool TryMerge(PathCollision a, PathCollision b)
    {
        Vector2d normal;

        if (Vector2d.TryIntersect(a.frame.pos, b.frame.pos, a.frame.normal, b.frame.normal, out var midpoint, 0.05))
        {
            normal = (a.frame.normal + b.frame.normal).Normalized;
        }
        else
        {
            var perpDot = Vector2d.PerpDot(a.frame.normal, b.frame.normal);
            normal = perpDot >= 0 ? a.frame.normal.PerpCCW : a.frame.normal.PerpCW;
            midpoint = a.position;
        }

        var shift = Math.Sign(Vector2d.PerpDot(normal, a.frame.normal));

        for (int i = 0; i < 5; i++)
        {
            var range = Math.Max(a.retraceRange, b.retraceRange) * (1 + i * 0.5);
            var target = midpoint + normal * range;

            var orgLengthA = a.activeSegment.Length;
            var orgLengthB = b.activeSegment.Length;

            DebugOutput($"range: {range} target: {target} normal: {normal} perpDot: {Vector2d.PerpDot(a.frame.normal, b.frame.normal)}");

            if (!CalcArcWithDuct(a, target, normal.PerpCW * shift, out var frameIdxA, out var arcLengthA, out var ductLengthA)) continue;
            if (!CalcArcWithDuct(b, target, normal.PerpCCW * shift, out var frameIdxB, out var arcLengthB, out var ductLengthB)) continue;

            var arcA = InsertArcWithDuct(a, frameIdxA, arcLengthA, ductLengthA);
            var arcB = InsertArcWithDuct(b, frameIdxB, arcLengthB, ductLengthB);

            arcA.RemoveAllBranches();
            arcB.RemoveAllBranches();

            var remainingLength = Math.Max(orgLengthA - a.activeSegment.Length, orgLengthB - b.activeSegment.Length);

            var merged = new Segment(arcA.Path)
            {
                TraceParams = TraceParams.Merge(a.activeSegment.TraceParams, b.activeSegment.TraceParams),
                Length = remainingLength
            };

            arcA.Attach(merged);
            arcB.Attach(merged);

            return true;
        }

        return false;

        Segment InsertArcWithDuct(
            PathCollision c,
            int frameIdx,
            double arcLength,
            double ductLength)
        {
            var frame = c.frames[frameIdx];

            c.activeSegment.Length = frame.dist;

            var arcAngle = -Vector2d.SignedAngle(frame.normal, normal);

            var ductSegment = c.activeSegment.InsertNew();
            ductSegment.TraceParams.ApplyFixedAngle(0);
            // ductSegment.RelSpeed = 5; // for debug
            ductSegment.Length = ductLength;

            var arcSegment = ductSegment.InsertNew();
            arcSegment.TraceParams.ApplyFixedAngle(arcAngle / arcLength);
            // arcSegment.RelSpeed = 0.05; // for debug
            arcSegment.Length = arcLength;

            return arcSegment;
        }

        bool CalcArcWithDuct(
            PathCollision c,
            Vector2d target,
            Vector2d shiftDir,
            out int frameIdx,
            out double arcLength,
            out double ductLength)
        {
            for (frameIdx = c.frames.Count - 1; frameIdx >= 0; frameIdx--)
            {
                var frame = c.frames[frameIdx];

                var pointB = frame.pos;
                var pointC = target + shiftDir * 0.5 * frame.width;

                if (Vector2d.Distance(pointB, c.position) < c.retraceRange && frameIdx > 0) continue;

                var arcAngle = -Vector2d.SignedAngle(frame.normal, normal);

                if (Vector2d.TryIntersect(pointB, pointC, frame.normal, normal, out var pointF, out var scalarB, 0.05))
                {
                    if (scalarB < 0) continue;

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

                            var arcAngleMax = (1 - c.activeSegment.TraceParams.AngleTenacity) * 180 * arcLength / (frame.width * Math.PI);

                            DebugOutput($"arcAngle: {arcAngle} arcAngleMax: {arcAngleMax}");

                            return arcAngle.Abs() <= arcAngleMax;
                        }
                    }
                }
            }

            arcLength = 0;
            ductLength = 0;

            return false;
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
    private List<TraceFrame> ExchangeFrameBuffer()
    {
        var buffer = _frameBuffer;
        _frameBuffer = new(50);
        return buffer;
    }
}
