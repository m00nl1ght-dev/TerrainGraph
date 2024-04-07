using System.Collections.Generic;

namespace TerrainGraph.Util;

public class GridKernel
{
    public readonly int Size;
    public readonly double Extend;

    public int PointCount => Offsets.Count;

    public readonly IReadOnlyList<Vector2d> Offsets;
    public readonly IReadOnlyList<Vector2d> Directions;

    public readonly IReadOnlyList<double> Distances;
    public readonly IReadOnlyList<double> Angles;

    public static GridKernel DiscreteCircle(int radius, double extend)
    {
        var offsets = new List<Vector2d>();

        int x = 0;
        int y = radius;
        int d = 2 - 2 * radius;

        while (y >= 0)
        {
            if (x > radius) x = radius;
            else if (x < -radius) x = -radius;

            if (y > radius) y = radius;
            else if (y < -radius) y = -radius;

            offsets.Add(new Vector2d(x * extend, y * extend));
            offsets.Add(new Vector2d(x * extend, -y * extend));
            offsets.Add(new Vector2d(-x * extend, y * extend));
            offsets.Add(new Vector2d(-x * extend, -y * extend));

            if (d < 0 && 2 * (d + y) - 1 <= 0)
            {
                d += 2 * ++x + 1;
                continue;
            }

            if (d > 0 && 2 * (d - x) - 1 > 0)
            {
                d += 1 - 2 * --y;
                continue;
            }

            d += 2 * (++x - y--);
        }

        return new GridKernel(radius, extend, offsets.ToArray());
    }

    public static GridKernel Square(int size, double extend)
    {
        var offsets = new Vector2d[(1 + 2 * size) * (1 + 2 * size) - 1];

        var index = 0;

        for (int x = -size; x <= size; x++)
        {
            for (int z = -size; z <= size; z++)
            {
                if (x != 0 || z != 0)
                {
                    offsets[index] = new Vector2d(x * extend, z * extend);
                    index++;
                }
            }
        }

        return new GridKernel(size, extend, offsets);
    }

    public static GridKernel Shield(int size, double extend, double spacing)
    {
        var offsets = new Vector2d[1 + 2 * size];

        var index = 0;

        for (int z = -size; z <= size; z++)
        {
            offsets[index] = new Vector2d(extend, z * spacing);
            index++;
        }

        return new GridKernel(size, extend, offsets);
    }

    private GridKernel(int size, double extend, IReadOnlyList<Vector2d> offsets)
    {
        var directions = new Vector2d[offsets.Count];
        var distances = new double[offsets.Count];
        var angles = new double[offsets.Count];

        for (int i = 0; i < offsets.Count; i++)
        {
            directions[i] = offsets[i].Normalized;
            distances[i] = offsets[i].Magnitude;
            angles[i] = -Vector2d.SignedAngle(Vector2d.AxisX, directions[i]);
        }

        Size = size;
        Extend = extend;
        Offsets = offsets;
        Directions = directions;
        Distances = distances;
        Angles = angles;
    }

    public Vector2d CalculateAt(Vector2d axisX, Vector2d axisZ, IGridFunction<double> grid, Vector2d pos)
    {
        var result = Vector2d.Zero;

        var valHere = grid.ValueAt(pos);

        for (var i = 0; i < Offsets.Count; i++)
        {
            var offset = Offsets[i];
            var direction = Directions[i];

            var offsetT = offset.x * axisX + offset.z * axisZ;
            var directionT = direction.x * axisX + direction.z * axisZ;

            var posThere = pos + offsetT;
            var valThere = grid.ValueAt(posThere);

            result -= directionT * (valThere - valHere);
        }

        return result / PointCount;
    }
}
