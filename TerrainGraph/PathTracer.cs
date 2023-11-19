using System;
using TerrainGraph.Util;
using static TerrainGraph.Path;
using static TerrainGraph.GridFunction;

namespace TerrainGraph;

public class HotSwappableAttribute : Attribute {}

[HotSwappable]
public class PathTracer
{
    private const double RadialThreshold = 0.5;

    public readonly int GridSizeX;
    public readonly int GridSizeZ;
    public readonly int GridMargin;

    public readonly double StepSize;
    public readonly double TraceMargin;

    private readonly double[,] _mainGrid;
    private readonly double[,] _valueGrid;
    private readonly double[,] _offsetGrid;

    public IGridFunction<double> MainGrid => BuildGridFunction(_mainGrid);
    public IGridFunction<double> ValueGrid => BuildGridFunction(_valueGrid);
    public IGridFunction<double> OffsetGrid => BuildGridFunction(_offsetGrid);

    public static Action<string> DebugOutput = _ => {};

    public PathTracer(int innerSizeX, int innerSizeZ, int gridMargin, double stepSize, double traceMargin)
    {
        StepSize = stepSize.WithMin(1);
        TraceMargin = traceMargin.WithMin(0);
        GridMargin = gridMargin.WithMin(0);

        GridSizeX = innerSizeX + GridMargin * 2;
        GridSizeZ = innerSizeZ + GridMargin * 2;

        _mainGrid = new double[GridSizeX, GridSizeZ];
        _valueGrid = new double[GridSizeX, GridSizeZ];
        _offsetGrid = new double[GridSizeX, GridSizeZ];
    }

    public void Trace(Path path)
    {
        var gridSize = new Vector2d(GridSizeX, GridSizeZ);
        var gridMargin = new Vector2d(GridMargin, GridMargin);

        foreach (var origin in path.Origins)
        {
            var baseFrame = new TraceFrame(
                origin.Position * gridSize + gridMargin,
                origin.BaseAngle,
                origin.BaseWidth,
                origin.BaseSpeed,
                origin.BaseValue
            );

            foreach (var branch in origin.Branches)
            {
                Trace(branch, baseFrame);
            }
        }
    }

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
        /// The path speed at the current position.
        /// </summary>
        public readonly double speed;

        /// <summary>
        /// The output value at the current position.
        /// </summary>
        public readonly double value;

        /// <summary>
        /// The total distance traveled so far from the start of the segment.
        /// </summary>
        public readonly double dist;

        public Vector2d perpCW => normal.PerpCW;
        public Vector2d perpCCW => normal.PerpCCW;

        public TraceFrame(Vector2d pos, double angle, double width, double speed, double value, double dist = 0) :
            this(pos, Vector2d.Direction(-angle), angle.NormalizeDeg(), width, speed, value, dist) {}

        private TraceFrame(Vector2d pos, Vector2d normal, double angle, double width, double speed, double value, double dist)
        {
            this.pos = pos;
            this.normal = normal;
            this.angle = angle;
            this.width = width;
            this.speed = speed;
            this.dist = dist;
            this.value = value;
        }

        public TraceFrame Advance(
            double distDelta, double angleDelta, double widthDelta, double speedDelta,
            double valueDelta, out Vector2d pivotPoint, out double pivotOffset)
        {
            var newAngle = (angle + angleDelta).NormalizeDeg();
            var newNormal = Vector2d.Direction(-newAngle);

            if (Math.Abs(angleDelta) >= RadialThreshold)
            {
                pivotOffset = 180 * distDelta / (Math.PI * -angleDelta);
                pivotPoint = pos + perpCCW * pivotOffset;

                return new TraceFrame(
                    pivotPoint - newNormal.PerpCCW * pivotOffset, newNormal, newAngle,
                    width + widthDelta, speed + speedDelta,
                    value + valueDelta, dist + distDelta
                );
            }

            pivotOffset = 0d;
            pivotPoint = pos;

            return new TraceFrame(
                pos + distDelta * normal, newNormal, newAngle,
                width + widthDelta, speed + speedDelta,
                value + valueDelta, dist + distDelta
            );
        }

        public override string ToString() =>
            $"{nameof(pos)}: {pos}, " +
            $"{nameof(angle)}: {angle}, " +
            $"{nameof(width)}: {width}, " +
            $"{nameof(speed)}: {speed}, " +
            $"{nameof(value)}: {value}, " +
            $"{nameof(dist)}: {dist}";
    }

    private void Trace(Segment segment, TraceFrame baseFrame)
    {
        var length = segment.Length;
        var extParams = segment.ExtendParams;

        var initialFrame = new TraceFrame(
            baseFrame.pos,
            baseFrame.angle + segment.RelAngle,
            baseFrame.width * segment.RelWidth,
            baseFrame.speed * segment.RelSpeed,
            baseFrame.value
        );

        DebugOutput($"Trace start with initial frame [{initialFrame}] and length {length}");

        var a = initialFrame;

        while (a.dist < length)
        {
            var distDelta = Math.Min(StepSize, length - a.dist);
            var angleDelta = CalculateAngleDelta(ref a, extParams, distDelta);

            var b = a.Advance(
                distDelta, angleDelta,
                distDelta * -extParams.WidthLoss,
                distDelta * -extParams.SpeedLoss,
                distDelta * a.speed,
                out var pivotPoint,
                out var pivotOffset
            );

            var speedA = a.speed;
            var speedB = b.speed;

            if (extParams.SpeedGrid != null)
            {
                speedA *= extParams.SpeedGrid.ValueAt(a.pos.x, a.pos.z);
                speedB *= extParams.SpeedGrid.ValueAt(b.pos.x, b.pos.z);
            }

            var extendA = a.width;
            var extendB = b.width;

            if (extParams.WidthGrid != null)
            {
                extendA *= extParams.WidthGrid.ValueAt(a.pos.x, a.pos.z);
                extendB *= extParams.WidthGrid.ValueAt(b.pos.x, b.pos.z);
            }

            if (extendA < 1) return;
            if (extendB < 0) extendB = 0;

            var extendAm = extendA + TraceMargin;
            var extendBm = extendB + TraceMargin;

            var p1 = a.pos + a.perpCCW * extendAm;
            var p2 = a.pos + a.perpCW * extendAm;
            var p3 = b.pos + b.perpCCW * extendBm;
            var p4 = b.pos + b.perpCW * extendBm;

            var xMin = (int) Math.Max(Math.Floor(Math.Min(Math.Min(p1.x, p2.x), Math.Min(p3.x, p4.x))), 0);
            var zMin = (int) Math.Max(Math.Floor(Math.Min(Math.Min(p1.z, p2.z), Math.Min(p3.z, p4.z))), 0);
            var xMax = (int) Math.Min(Math.Ceiling(Math.Max(Math.Max(p1.x, p2.x), Math.Max(p3.x, p4.x))), GridSizeX - 1);
            var zMax = (int) Math.Min(Math.Ceiling(Math.Max(Math.Max(p1.z, p2.z), Math.Max(p3.z, p4.z))), GridSizeZ - 1);

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
                        double progress;

                        if (pivotOffset > 0)
                        {
                            var vec = pos - pivotPoint;

                            offset = Math.Sign(-angleDelta) * (vec.Magnitude - Math.Abs(pivotOffset));
                            offsetAbs = Math.Abs(offset);

                            var inside = offsetAbs <= extendAm || offsetAbs <= extendBm;

                            progress = inside ? Vector2d.Angle(a.pos - pivotPoint, vec) / Math.Abs(angleDelta) : 0d;
                        }
                        else
                        {
                            offset = -Vector2d.PerpDot(a.normal, pos - a.pos);
                            offsetAbs = Math.Abs(offset);
                            progress = dotA / distDelta;
                        }

                        var extend = extendA + (extendB - extendA) * progress;

                        if (offsetAbs <= extend + TraceMargin)
                        {
                            var speed = speedA + (speedB - speedA) * progress;
                            var value = a.value + distDelta * progress * speed;

                            _valueGrid[x, z] = value;
                            _offsetGrid[x, z] = offset;

                            if (offsetAbs <= extend)
                            {
                                _mainGrid[x, z] = extend;
                            }
                        }
                    }
                }
            }

            a = b;
        }

        foreach (var branch in segment.Branches)
        {
            Trace(branch, a); // TODO better placement and params
        }
    }

    private static double CalculateAngleDelta(ref TraceFrame frame, ExtendParams extParams, double distDelta)
    {
        var angleDelta = 0d;

        if (extParams.SwerveGrid != null)
        {
            angleDelta += extParams.SwerveGrid.ValueAt(frame.pos.x, frame.pos.z);
        }

        if (extParams.AbsFollowGrid != null || extParams.RelFollowGrid != null)
        {
            // TODO follow calc
        }

        return (distDelta * angleDelta).NormalizeDeg();
    }

    private IGridFunction<double> BuildGridFunction(double[,] grid)
    {
        if (GridMargin == 0) return new Cache<double>(grid);
        return new Transform<double>(new Cache<double>(grid), -GridMargin, -GridMargin, 1, 1);
    }
}
