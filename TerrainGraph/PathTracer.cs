using System;
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

        foreach (var origin in path.Origins)
        {
            var baseFrame = new TraceFrame(origin, GridInnerSize, GridMargin);

            foreach (var branch in origin.Branches)
            {
                Trace(branch, null, baseFrame);
            }
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
        /// The offset density at the current position.
        /// </summary>
        public readonly double density;

        /// <summary>
        /// The total distance traveled so far from the start of the segment.
        /// </summary>
        public readonly double dist;

        /// <summary>
        /// The unit vector perpendicular clockwise to the current direction.
        /// </summary>
        public Vector2d perpCW => normal.PerpCW;

        /// <summary>
        /// The unit vector perpendicular counter-clockwise to the current direction.
        /// </summary>
        public Vector2d perpCCW => normal.PerpCCW;

        public TraceFrame(Origin origin, Vector2d gridScalar, Vector2d gridOffset)
        {
            this.angle = origin.BaseAngle;
            this.width = origin.BaseWidth;
            this.speed = origin.BaseSpeed;
            this.value = origin.BaseValue;
            this.density = origin.BaseDensity;
            this.normal = Vector2d.Direction(-angle);
            this.pos = origin.Position * gridScalar + gridOffset;
        }

        public TraceFrame(TraceFrame parent, Segment segment, double distOffset)
        {
            this.angle = (parent.angle + segment.RelAngle).NormalizeDeg();
            this.width = parent.width * segment.RelWidth - distOffset * segment.ExtendParams.WidthLoss;
            this.speed = parent.speed * segment.RelSpeed - distOffset * segment.ExtendParams.SpeedLoss;
            this.value = parent.value + (distOffset < 0 ? speed : parent.speed) * distOffset;
            this.density = parent.density;
            this.normal = Vector2d.Direction(-angle);
            this.pos = parent.pos + distOffset * normal;
            this.dist = distOffset;
        }

        private TraceFrame(
            Vector2d pos, Vector2d normal, double angle, double width,
            double speed, double value, double density, double dist)
        {
            this.pos = pos;
            this.normal = normal;
            this.angle = angle;
            this.width = width;
            this.speed = speed;
            this.value = value;
            this.density = density;
            this.dist = dist;
        }

        public TraceFrame Advance(
            ExtendParams extParams, double distDelta, double angleDelta,
            double valueDelta, out Vector2d pivotPoint, out double pivotOffset)
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
                value + valueDelta,
                density - distDelta * extParams.DensityLoss,
                dist + distDelta
            );
        }

        public override string ToString() =>
            $"{nameof(pos)}: {pos}, " +
            $"{nameof(angle)}: {angle}, " +
            $"{nameof(width)}: {width}, " +
            $"{nameof(speed)}: {speed}, " +
            $"{nameof(value)}: {value}, " +
            $"{nameof(density)}: {density}, " +
            $"{nameof(dist)}: {dist}";
    }

    private void Trace(Segment segment, Segment parent, TraceFrame baseFrame)
    {
        var length = segment.Length;
        var extParams = segment.ExtendParams;

        var marginTail = parent == null ? TraceOuterMargin : 0;
        var marginHead = segment.Branches.Count == 0 ? TraceOuterMargin : 0;

        var initialFrame = new TraceFrame(baseFrame, segment, -marginTail);

        DebugOutput($"Trace start with initial frame [{initialFrame}] and length {length}");

        var a = initialFrame;

        while (a.dist < length + marginHead)
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

                var maxAngleDelta = extParams.MaxTurnRate * 180 * distDelta / (a.width * Math.PI);
                angleDelta = (distDelta * angleDelta).NormalizeDeg().InRange(-maxAngleDelta, maxAngleDelta);
            }
            else
            {
                angleDelta = 0;
                distDelta = -a.dist;
            }

            var valueDelta = a.speed * distDelta;

            if (extParams.SpeedGrid != null && a.dist >= 0)
            {
                valueDelta *= extParams.SpeedGrid.ValueAt(a.pos - GridMargin);
            }

            var b = a.Advance(extParams, distDelta, angleDelta, valueDelta, out var pivotPoint, out var pivotOffset);

            var extendA = a.width / 2;
            var extendB = b.width / 2;

            if (extParams.WidthGrid != null)
            {
                extendA *= extParams.WidthGrid.ValueAt(a.pos - GridMargin);
                extendB *= extParams.WidthGrid.ValueAt(b.pos - GridMargin);
            }

            var densityA = a.density;
            var densityB = b.density;

            if (extParams.DensityGrid != null)
            {
                densityA *= extParams.DensityGrid.ValueAt(a.pos - GridMargin);
                densityB *= extParams.DensityGrid.ValueAt(b.pos - GridMargin);
            }

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
                        double offset;
                        double offsetAbs;

                        double progress = 0.5;

                        if (pivotOffset != 0)
                        {
                            var pivotVec = pos - pivotPoint;

                            offset = Math.Sign(-angleDelta) * (pivotVec.Magnitude - Math.Abs(pivotOffset));
                            offsetAbs = Math.Abs(offset);

                            if (offsetAbs <= boundIa || offsetAbs <= boundIb)
                            {
                                progress = Vector2d.Angle(a.pos - pivotPoint, pivotVec) / Math.Abs(angleDelta);
                            }
                        }
                        else
                        {
                            offset = -Vector2d.PerpDot(a.normal, pos - a.pos);
                            offsetAbs = Math.Abs(offset);

                            progress = dotA / distDelta;
                        }

                        var extend = extendA + (extendB - extendA) * progress;

                        if (offsetAbs <= extend + TraceOuterMargin)
                        {
                            _distanceGrid[x, z] = Math.Min(_distanceGrid[x, z], offsetAbs - extend);

                            if (offsetAbs <= extend + TraceInnerMargin)
                            {
                                var dist = a.dist + distDelta * progress;
                                var value = a.value + valueDelta * progress;

                                var density = densityA + (densityB - densityA) * progress;

                                _valueGrid[x, z] = value;
                                _offsetGrid[x, z] = offset * density;

                                if (offsetAbs <= extend && dist >= 0 && dist <= length)
                                {
                                    _mainGrid[x, z] = extend;
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

        foreach (var branch in segment.Branches)
        {
            Trace(branch, segment, a); // TODO better placement and params
        }
    }

    private IGridFunction<double> BuildGridFunction(double[,] grid)
    {
        if (GridMargin == Vector2d.Zero) return new Cache<double>(grid);
        return new Transform<double>(new Cache<double>(grid), -GridMargin.x, -GridMargin.z, 1, 1);
    }
}
