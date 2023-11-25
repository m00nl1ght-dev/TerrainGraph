using static TerrainGraph.GridFunction;

namespace TerrainGraph.Util;

public class GridKernel
{
    public readonly int PointCount;

    private readonly Vector2d[] _directions;
    private readonly Vector2d[] _offsets;

    public GridKernel(int kernelSize, double kernelExtend)
    {
        PointCount = (1 + 2 * kernelSize) * (1 + 2 * kernelSize) - 1;

        _directions = new Vector2d[PointCount];
        _offsets = new Vector2d[PointCount];

        var index = 0;

        for (int x = -kernelSize; x <= kernelSize; x++)
        {
            for (int z = -kernelSize; z <= kernelSize; z++)
            {
                if (x != 0 || z != 0)
                {
                    var offset = new Vector2d(x * kernelExtend, z * kernelExtend);

                    _directions[index] = offset.Normalized;
                    _offsets[index] = offset;

                    index++;
                }
            }
        }
    }

    public Vector2d CalculateAt(
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

            var absThere = absPos + offset;
            var relThere = relPos + offset;

            var valThere = 0d;

            if (absFunc != null) valThere += absFunc.ValueAt(absThere);
            if (relFunc != null) valThere += Rotate<double>.Calculate(relFunc, relThere.x, relThere.z, 0, 0, relAngle);

            result += direction * (valThere - valHere);
        }

        return result / PointCount;
    }
}
