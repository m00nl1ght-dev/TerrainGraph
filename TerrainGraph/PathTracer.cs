using System;
using System.Collections.Generic;
using TerrainGraph.Util;
using static TerrainGraph.Path;
using static TerrainGraph.GridFunction;

namespace TerrainGraph;

public class HotSwappableAttribute : Attribute {}

[HotSwappable]
public class PathTracer
{
    private const int MaxTraceIterations = 1_000_000;

    private const double RadialThreshold = 0.5;

    public readonly Vector2d GridInnerSize;
    public readonly Vector2d GridOuterSize;
    public readonly Vector2d GridMargin;

    public readonly double StepSize;
    public readonly double TraceInnerMargin;
    public readonly double TraceOuterMargin;

    private readonly double[,] _mainGrid;
    private readonly double[,] _valueGrid;
    private readonly double[,] _offsetGrid;
    private readonly double[,] _distanceGrid;

    public IGridFunction<double> MainGrid => BuildGridFunction(_mainGrid);
    public IGridFunction<double> ValueGrid => BuildGridFunction(_valueGrid);
    public IGridFunction<double> OffsetGrid => BuildGridFunction(_offsetGrid);
    public IGridFunction<double> DistanceGrid => BuildGridFunction(_distanceGrid);

    private readonly GridKernel _followGridKernel = GridKernel.Square(3, 5);
    private readonly GridKernel _avoidGridKernel = GridKernel.Shield(2, 5, 3);

    private readonly IGridFunction<double> _overlapAvoidanceGrid = Zero;

    private int _totalTraceIterations;

    public static Action<string> DebugOutput = _ => {};

    public PathTracer(
        int innerSizeX, int innerSizeZ, int gridMargin,
        double stepSize, double traceInnerMargin, double traceOuterMargin)
    {
        StepSize = stepSize.WithMin(1);

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

    public void Trace(Path path)
    {
        _totalTraceIterations = 0;

        var taskQueue = new Queue<TraceTask>();

        foreach (var origin in path.Origins)
        {
            var baseFrame = new TraceFrame(origin, GridInnerSize, GridMargin);

            foreach (var branch in origin.Branches)
            {
                taskQueue.Enqueue(new TraceTask(branch, baseFrame, branch.IsLeaf ? TraceOuterMargin : 0, TraceOuterMargin));
            }
        }

        while (taskQueue.Count > 0)
        {
            var task = taskQueue.Dequeue();

            var headFrame = Trace(task);

            if (headFrame.width > 0)
            {
                foreach (var branch in task.segment.Branches)
                {
                    taskQueue.Enqueue(new TraceTask(branch, headFrame, branch.IsLeaf ? TraceOuterMargin : 0, 0));
                }
            }
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
        /// The additional path length to trace at the head end of the segment.
        /// </summary>
        public readonly double marginHead;

        /// <summary>
        /// The additional path length to trace at the tail end of the segment.
        /// </summary>
        public readonly double marginTail;

        public TraceTask(Segment segment, TraceFrame baseFrame, double marginHead, double marginTail)
        {
            this.segment = segment;
            this.baseFrame = baseFrame;
            this.marginHead = marginHead;
            this.marginTail = marginTail;
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
        /// Multipliers based on the extend parameters at the current position.
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
            this.pos = origin.Position * gridScalar + gridOffset;
            this.factors = new LocalFactors();
            this.normal = Vector2d.Direction(-angle);
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
            this.width = parent.width * segment.RelWidth - distOffset * segment.ExtendParams.WidthLoss;
            this.speed = parent.speed * segment.RelSpeed - distOffset * segment.ExtendParams.SpeedLoss;
            this.value = parent.value + (distOffset < 0 ? speed : parent.speed) * distOffset;
            this.offset = parent.offset - parent.width * parent.factors.width * parent.factors.density * segment.RelOffset;
            this.pos = parent.pos + parent.perpCCW * parent.width * parent.factors.width * segment.RelOffset + distOffset * normal;
            this.factors = new LocalFactors(segment.ExtendParams, pos - gridOffset);
            this.normal = Vector2d.Direction(-angle);
            this.density = parent.density;
            this.dist = distOffset;
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
            ExtendParams extParams, double distDelta, double angleDelta,
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

        public LocalFactors(ExtendParams extendParams, Vector2d pos)
        {
            width = extendParams.WidthGrid?.ValueAt(pos) ?? 1;
            speed = extendParams.SpeedGrid?.ValueAt(pos) ?? 1;
            density = extendParams.DensityGrid?.ValueAt(pos) ?? 1;
        }

        public override string ToString() =>
            $"{nameof(width)}: {width}, " +
            $"{nameof(speed)}: {speed}, " +
            $"{nameof(density)}: {density}";
    }

    private TraceFrame Trace(TraceTask task)
    {
        var length = task.segment.Length;
        var extParams = task.segment.ExtendParams;

        var initialFrame = new TraceFrame(task.baseFrame, task.segment, GridMargin, -task.marginTail);

        DebugOutput($"Trace start with initial frame [{initialFrame}] and length {length}");

        var a = initialFrame;

        while (a.dist < length + task.marginHead)
        {
            double distDelta;
            double angleDelta;

            if (a.dist >= 0)
            {
                angleDelta = 0;
                distDelta = StepSize;

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
                    angleDelta = -Vector2d.SignedAngle(a.normal, a.normal + followVec);
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
                angleDelta = 0;
                distDelta = -a.dist;
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
                            _distanceGrid[x, z] = Math.Min(_distanceGrid[x, z], shiftAbs - extend);

                            if (shiftAbs <= extend + TraceInnerMargin)
                            {
                                var dist = a.dist + distDelta * progress;

                                var value = progress.Lerp(a.value, b.value);
                                var offset = progress.Lerp(a.offset, b.offset);
                                var density = progress.Lerp(a.densityMul, b.densityMul);

                                if (_mainGrid[x, z] == 0)
                                {
                                    _valueGrid[x, z] = value;
                                    _offsetGrid[x, z] = offset + shift * density;

                                    if (shiftAbs <= extend && dist >= 0 && dist <= length)
                                    {
                                        _mainGrid[x, z] = extend;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            a = b;

            if (++_totalTraceIterations > MaxTraceIterations)
            {
                throw new Exception("Exceeded max PathTracer iteration count");
            }
        }

        return a;
    }

    private IGridFunction<double> BuildGridFunction(double[,] grid)
    {
        if (GridMargin == Vector2d.Zero) return new Cache<double>(grid);
        return new Transform<double>(new Cache<double>(grid), -GridMargin.x, -GridMargin.z, 1, 1);
    }
}
