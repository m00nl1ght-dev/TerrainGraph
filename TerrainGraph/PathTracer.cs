using System;
using TerrainGraph.Util;

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

    public readonly double[,] MainGrid;
    public readonly double[,] DepthGrid;
    public readonly double[,] OffsetGrid;

    public static Action<string> DebugOutput = _ => {};

    public PathTracer(int gridSizeX, int gridSizeZ, int gridMargin, double stepSize, double traceMargin)
    {
        GridSizeX = gridSizeX;
        GridSizeZ = gridSizeZ;

        StepSize = stepSize.WithMin(1);
        TraceMargin = traceMargin.WithMin(0);
        GridMargin = gridMargin.WithMin(0);

        MainGrid = new double[gridSizeX, gridSizeZ];
        DepthGrid = new double[gridSizeX, gridSizeZ];
        OffsetGrid = new double[gridSizeX, gridSizeZ];
    }

    public void Trace(Path path)
    {
        foreach (var origin in path.Origins)
        {
            foreach (var branch in origin.Branches)
            {
                Trace(
                    branch,
                    new Vector2d(origin.PosX * GridSizeX, origin.PosZ * GridSizeZ),
                    0, 0, origin.BaseWidth, origin.BaseSpeed
                );
            }
        }
    }

    private void Trace(
        Path.Segment segment, Vector2d startPos,
        double baseAngle, double baseDepth,
        double baseWidth, double baseSpeed)
    {
        var length = segment.Length;
        var extParams = segment.ExtendParams;

        var initialWidth = baseWidth * segment.RelWidth;
        var initialSpeed = baseSpeed * segment.RelSpeed;
        var initialAngle = (baseAngle + segment.RelAngle).NormalizeDeg();

        var distA = 0d;
        var widthA = initialWidth;
        var speedA = initialSpeed;
        var angleA = initialAngle;
        var vecA = Vector2d.Direction(angleA);
        var posA = startPos;

        DebugOutput($"Trace start at {posA} with length {length}");

        while (distA < length)
        {
            var distDelta = Math.Min(StepSize, length - distA);

            var angleDelta = 0d;

            if (extParams.SwerveGrid != null)
            {
                angleDelta += extParams.SwerveGrid.ValueAt(posA.x, posA.z);
            }

            if (extParams.AbsFollowGrid != null || extParams.RelFollowGrid != null)
            {
                // TODO
            }

            angleDelta = distDelta * angleDelta.NormalizeDeg();

            // TODO check that angle delta not too big for current width

            var radial = Math.Abs(angleDelta) >= RadialThreshold;
            var pivotOffset = radial ? 180 * distDelta / (Math.PI * angleDelta) : 0d;
            var pivotPoint = radial ? posA + vecA.PerpCCW * pivotOffset : Vector2d.Zero;

            var distB = distA + distDelta;
            var widthB = widthA - distDelta * extParams.WidthLoss;
            var speedB = speedA - distDelta * extParams.SpeedLoss;
            var angleB = (angleA + angleDelta).NormalizeDeg();

            var vecB = Vector2d.Direction(angleB);
            var posB = radial ? pivotPoint - vecB.PerpCCW * pivotOffset : posA + distDelta * vecA;

            var boundA = widthA * extParams.WidthGrid?.ValueAt(posA.x, posA.z) ?? 1;
            var boundB = widthB * extParams.WidthGrid?.ValueAt(posB.x, posB.z) ?? 1;
            var boundAm = boundA + TraceMargin;
            var boundBm = boundB + TraceMargin;

            var p1 = posA + vecA.PerpCCW * boundAm;
            var p2 = posA + vecA.PerpCW * boundAm;
            var p3 = posB + vecB.PerpCCW * boundBm;
            var p4 = posB + vecB.PerpCW * boundBm;

            var xMin = (int) Math.Max(Math.Floor(Math.Min(Math.Min(p1.x, p2.x), Math.Min(p3.x, p4.x))), 0);
            var zMin = (int) Math.Max(Math.Floor(Math.Min(Math.Min(p1.z, p2.z), Math.Min(p3.z, p4.z))), 0);
            var xMax = (int) Math.Min(Math.Ceiling(Math.Max(Math.Max(p1.x, p2.x), Math.Max(p3.x, p4.x))), GridSizeX - 1);
            var zMax = (int) Math.Min(Math.Ceiling(Math.Max(Math.Max(p1.z, p2.z), Math.Max(p3.z, p4.z))), GridSizeZ - 1);

            for (int x = xMin; x <= xMax; x++)
            {
                for (int z = zMin; z <= zMax; z++)
                {
                    var posAbs = new Vector2d(x, z);
                    var posRelA = posAbs - posA;
                    var posRelB = posAbs - posB;

                    var dotA = Vector2d.Dot(vecA, posRelA);
                    var dotB = Vector2d.Dot(vecB, posRelB);

                    if (dotA >= 0 && dotB < 0)
                    {
                        if (radial)
                        {
                            var posRelP = posAbs - pivotPoint;
                            var offset = posRelP.Magnitude - Math.Abs(pivotOffset);
                            var offsetAbs = Math.Abs(offset);

                            if (offsetAbs <= boundAm || offsetAbs <= boundBm)
                            {
                                var progress = Vector2d.Angle(posA - pivotPoint, posRelP) / Math.Abs(angleDelta);
                                var bound = boundA + (boundB - boundA) * progress;

                                if (offsetAbs <= bound + TraceMargin)
                                {
                                    DepthGrid[x, z] = baseDepth + distA + distDelta * progress;
                                    OffsetGrid[x, z] = offset * Math.Sign(angleDelta);

                                    if (offsetAbs <= bound)
                                    {
                                        MainGrid[x, z] = bound;
                                    }
                                }
                            }
                        }
                        else
                        {
                            var progress = dotA / distDelta;
                            var bound = boundA + (boundB - boundA) * progress;
                            var offset = -Vector2d.PerpDot(vecA, posRelA);
                            var offsetAbs = Math.Abs(offset);

                            if (offsetAbs <= bound + TraceMargin)
                            {
                                DepthGrid[x, z] = baseDepth + distA + distDelta * progress;
                                OffsetGrid[x, z] = offset;

                                if (offsetAbs <= bound)
                                {
                                    MainGrid[x, z] = bound;
                                }
                            }
                        }
                    }
                }
            }

            distA = distB;
            posA = posB;
            angleA = angleB;
            widthA = widthB;
            speedA = speedB;
            vecA = vecB;
        }

        foreach (var branch in segment.Branches)
        {
            Trace(
                branch,
                posA, angleA,
                baseDepth + distA,
                widthA, speedA
            );
        }
    }
}
