using static TerrainGraph.GridFunction;

namespace TerrainGraph.Util;

[HotSwappable]
public class GridKernel
{
    public readonly int PointCount;

    private readonly Vector2d[] _directions;
    private readonly Vector2d[] _offsets;

    public static GridKernel Square(int size, double extend)
    {
        var kernel = new GridKernel((1 + 2 * size) * (1 + 2 * size) - 1);

        var directions = kernel._directions;
        var offsets = kernel._offsets;

        var index = 0;

        for (int x = -size; x <= size; x++)
        {
            for (int z = -size; z <= size; z++)
            {
                if (x != 0 || z != 0)
                {
                    var offset = new Vector2d(x * extend, z * extend);

                    directions[index] = offset.Normalized;
                    offsets[index] = offset;

                    index++;
                }
            }
        }

        return kernel;
    }

    public static GridKernel Shield(int size, double extend, double spacing)
    {
        var kernel = new GridKernel(1 + 2 * size);

        var directions = kernel._directions;
        var offsets = kernel._offsets;

        var index = 0;

        for (int z = -size; z <= size; z++)
        {
            var offset = new Vector2d(extend, z * spacing);

            directions[index] = offset.Normalized;
            offsets[index] = offset;

            index++;
        }

        return kernel;
    }

    private GridKernel(int pointCount)
    {
        PointCount = pointCount;

        _directions = new Vector2d[PointCount];
        _offsets = new Vector2d[PointCount];
    }

    public Vector2d CalculateAt(
        Vector2d axisX, Vector2d axisZ,
        IGridFunction<double> absFunc, IGridFunction<double> relFunc,
        Vector2d absPos, Vector2d relPos, double relAngle)
    {
        var result = Vector2d.Zero;

        var valHere = 0d;

        if (absFunc != null) valHere += absFunc.ValueAt(absPos);
        if (relFunc != null) valHere += Rotate<double>.Calculate(relFunc, relPos.x, relPos.z, 0, 0, relAngle);

        for (var i = 0; i < _offsets.Length; i++)
        {
            var offset = _offsets[i];
            var direction = _directions[i];

            var offsetT = offset.x * axisX + offset.z * axisZ;
            var directionT = direction.x * axisX + direction.z * axisZ;

            var absThere = absPos + offsetT;
            var relThere = relPos + offsetT;

            var valThere = 0d;

            if (absFunc != null) valThere += absFunc.ValueAt(absThere);
            if (relFunc != null) valThere += Rotate<double>.Calculate(relFunc, relThere.x, relThere.z, 0, 0, relAngle);

            result += directionT * (valThere - valHere);
        }

        return result / PointCount;
    }
}
