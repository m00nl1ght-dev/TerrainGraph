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
            var baseFrame = new TraceFrame(origin, gridSize, gridMargin);

            foreach (var branch in origin.Branches)
            {
                Trace(branch, null, baseFrame);
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

        /// <summary>
        /// The unit vector perpendicular clockwise to the current direction.
        /// </summary>
        public Vector2d perpCW => normal.PerpCW;

        /// <summary>
        /// The unit vector perpendicular counter-clockwise to the current direction.
        /// </summary>
        public Vector2d perpCCW => normal.PerpCCW;

        public TraceFrame(Origin origin, Vector2d gridSize, Vector2d gridMargin)
        {
            this.angle = origin.BaseAngle;
            this.width = origin.BaseWidth;
            this.speed = origin.BaseSpeed;
            this.value = origin.BaseValue;
            this.normal = Vector2d.Direction(-angle);
            this.pos = origin.Position * gridSize + gridMargin;
        }

        public TraceFrame(TraceFrame parent, Segment segment, double distOffset)
        {
            this.angle = (parent.angle + segment.RelAngle).NormalizeDeg();
            this.width = parent.width * segment.RelWidth - distOffset * segment.ExtendParams.WidthLoss;
            this.speed = parent.speed * segment.RelSpeed - distOffset * segment.ExtendParams.SpeedLoss;
            this.value = parent.value - speed * distOffset; // TODO not accurate because of SpeedGrid
            this.normal = Vector2d.Direction(-angle);
            this.pos = parent.pos + distOffset * normal;
            this.dist = distOffset;
        }

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

        public TraceFrame Advance(ExtendParams extParams, double distDelta, double angleDelta, out Vector2d pivotPoint, out double pivotOffset)
        {
            var newAngle = (angle + angleDelta).NormalizeDeg();
            var newNormal = Vector2d.Direction(-newAngle);

            var newWidth = width - distDelta * extParams.WidthLoss;
            var newSpeed = speed - distDelta * extParams.SpeedLoss;
            var newValue = value + distDelta * speed; // TODO not accurate because of SpeedGrid
            var newDist = dist + distDelta;

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

            return new TraceFrame(newPos, newNormal, newAngle, newWidth, newSpeed, newValue, newDist);
        }

        public override string ToString() =>
            $"{nameof(pos)}: {pos}, " +
            $"{nameof(angle)}: {angle}, " +
            $"{nameof(width)}: {width}, " +
            $"{nameof(speed)}: {speed}, " +
            $"{nameof(value)}: {value}, " +
            $"{nameof(dist)}: {dist}";
    }

    private void Trace(Segment segment, Segment parent, TraceFrame baseFrame)
    {
        var length = segment.Length;
        var extParams = segment.ExtendParams;

        var marginTail = parent == null ? TraceMargin : 0;
        var marginHead = segment.Branches.Count == 0 ? TraceMargin : 0;

        var initialFrame = new TraceFrame(baseFrame, segment, -marginTail);

        DebugOutput($"Trace start with initial frame [{initialFrame}] and length {length}");

        var a = initialFrame;

        while (a.dist < length + marginHead)
        {
            var distDelta = a.dist < 0 ? -a.dist : StepSize;
            var angleDelta = a.dist < 0 ? 0 : CalculateAngleDelta(a, extParams, distDelta);

            var b = a.Advance(extParams, distDelta, angleDelta, out var pivotPoint, out var pivotOffset);

            var speed = a.speed;

            if (extParams.SpeedGrid != null)
            {
                speed *= extParams.SpeedGrid.ValueAt(a.pos.x, a.pos.z);
            }

            var extendA = a.width / 2;
            var extendB = b.width / 2;

            if (extParams.WidthGrid != null)
            {
                extendA *= extParams.WidthGrid.ValueAt(a.pos.x, a.pos.z);
                extendB *= extParams.WidthGrid.ValueAt(b.pos.x, b.pos.z);
            }

            var boundA = extendA + TraceMargin;
            var boundB = extendB + TraceMargin;

            if (extendA <= 0 && a.dist >= 0)
            {
                length = Math.Min(length, b.dist);
            }

            var boundP1 = a.pos + a.perpCCW * boundA;
            var boundP2 = a.pos + a.perpCW * boundA;
            var boundP3 = b.pos + b.perpCCW * boundB;
            var boundP4 = b.pos + b.perpCW * boundB;

            var boundMin = Vector2d.Min(Vector2d.Min(boundP1, boundP2), Vector2d.Min(boundP3, boundP4));
            var boundMax = Vector2d.Max(Vector2d.Max(boundP1, boundP2), Vector2d.Max(boundP3, boundP4));

            var xMin = (int) Math.Max(Math.Floor(boundMin.x), 0);
            var zMin = (int) Math.Max(Math.Floor(boundMin.z), 0);

            var xMax = (int) Math.Min(Math.Ceiling(boundMax.x), GridSizeX - 1);
            var zMax = (int) Math.Min(Math.Ceiling(boundMax.z), GridSizeZ - 1);

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

                        double progress = 0;

                        if (pivotOffset > 0)
                        {
                            var pivotVec = pos - pivotPoint;

                            offset = Math.Sign(-angleDelta) * (pivotVec.Magnitude - Math.Abs(pivotOffset));
                            offsetAbs = Math.Abs(offset);

                            if (offsetAbs <= boundA || offsetAbs <= boundB)
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

                        if (offsetAbs <= extend + TraceMargin)
                        {
                            var dist = a.dist + distDelta * progress;
                            var value = a.value + distDelta * progress * speed;

                            _valueGrid[x, z] = value;
                            _offsetGrid[x, z] = offset;

                            if (offsetAbs <= extend && dist >= 0 && dist <= length)
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
            Trace(branch, segment, a); // TODO better placement and params
        }
    }

    private static double CalculateAngleDelta(TraceFrame frame, ExtendParams extParams, double distDelta)
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
