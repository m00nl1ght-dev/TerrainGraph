using System;
using TerrainGraph.Util;

namespace TerrainGraph;

public class HotSwappableAttribute : Attribute {}

[HotSwappable]
public class PathTracer
{
    private const double RadialThreshold = 0.5;
    private const double MaxAngleDelta = 45;

    public readonly int GridSizeX;
    public readonly int GridSizeZ;

    public readonly double StepSize;

    public readonly double[,] MainGrid;
    public readonly double[,] DepthGrid;
    public readonly double[,] OffsetGrid;

    public static Action<string> dbg = s => {};

    public PathTracer(int gridSizeX, int gridSizeZ, double stepSize)
    {
        GridSizeX = gridSizeX;
        GridSizeZ = gridSizeZ;

        StepSize = stepSize.WithMin(1);

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
                    0, 0, origin.BaseWidth
                );
            }
        }
    }

    private void Trace(
        Path.Segment segment, Vector2d startPos,
        double baseAngle, double baseDepth, double baseWidth)
    {
        var opts = segment.ExtendParams;
        var length = segment.Length;

        var distA = 0d;
        var widthA = baseWidth * segment.RelWidth;
        var angleA = (baseAngle + segment.RelAngle).NormalizeDeg();
        var vecA = Vector2d.Direction(angleA);
        var posA = startPos;

        dbg($"Trace start at {posA} with length {length}");

        while (distA < length)
        {
            var distDelta = Math.Min(StepSize, length - distA);
            var angleDelta = CalculateAngleDelta(posA, distA, angleA, opts) * distDelta;

            var radial = Math.Abs(angleDelta) >= RadialThreshold;
            var pivotOffset = radial ? 180 * distDelta / (Math.PI * angleDelta) : 0d;
            var pivotPoint = radial ? posA + vecA.PerpCCW * pivotOffset : Vector2d.Zero;

            var distB = distA + distDelta;
            var widthB = widthA - distDelta * opts.WidthLoss;
            var angleB = (angleA + angleDelta).NormalizeDeg();

            var vecB = Vector2d.Direction(angleB);
            var posB = radial ? pivotPoint - vecB.PerpCCW * pivotOffset : posA + distDelta * vecA;

            var p1 = posA + vecA.PerpCCW * widthA;
            var p2 = posA + vecA.PerpCW * widthA;
            var p3 = posB + vecB.PerpCCW * widthB;
            var p4 = posB + vecB.PerpCW * widthB;

            var xMin = (int) Math.Max(Math.Floor(Math.Min(Math.Min(p1.x, p2.x), Math.Min(p3.x, p4.x))), 0);
            var zMin = (int) Math.Max(Math.Floor(Math.Min(Math.Min(p1.z, p2.z), Math.Min(p3.z, p4.z))), 0);
            var xMax = (int) Math.Min(Math.Ceiling(Math.Max(Math.Max(p1.x, p2.x), Math.Max(p3.x, p4.x))), GridSizeX - 1);
            var zMax = (int) Math.Min(Math.Ceiling(Math.Max(Math.Max(p1.z, p2.z), Math.Max(p3.z, p4.z))), GridSizeZ - 1);

            dbg($"Trace step at {posA} with dist {distA} and angle delta {angleDelta}");

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

                            if (offsetAbs <= widthA || offsetAbs <= widthB)
                            {
                                var progress = Vector2d.Angle(posA - pivotPoint, posRelP) / Math.Abs(angleDelta);
                                var width = widthA + (widthB - widthA) * progress;

                                if (offsetAbs <= width)
                                {
                                    MainGrid[x, z] = width;
                                    DepthGrid[x, z] = baseDepth + distA + distDelta * progress;
                                    OffsetGrid[x, z] = offset * Math.Sign(angleDelta);
                                }
                            }
                        }
                        else
                        {
                            var progress = dotA / distDelta;
                            var offset = -Vector2d.PerpDot(vecA, posRelA);
                            var width = widthA + (widthB - widthA) * progress;

                            if (Math.Abs(offset) <= width)
                            {
                                MainGrid[x, z] = width;
                                DepthGrid[x, z] = baseDepth + distA + distDelta * progress;
                                OffsetGrid[x, z] = offset;
                            }
                        }
                    }
                }
            }

            distA = distB;
            posA = posB;
            angleA = angleB;
            widthA = widthB;
            vecA = vecB;
        }

        foreach (var branch in segment.Branches)
        {
            Trace(
                branch,
                posA, angleA,
                baseDepth + distA, widthA
            );
        }
    }

    private double CalculateAngleDelta(Vector2d pos, double dist, double currentAngle, Path.ExtendParams opts)
    {
        var delta = 0d;

        if (opts.SwerveGrid != null)
        {
            delta += opts.SwerveGrid.ValueAt(pos.x, pos.z);
        }

        if (opts.AvoidGrid != null)
        {
            // TODO
        }

        return delta.NormalizeDeg().InRange(-MaxAngleDelta, MaxAngleDelta);
    }
}
