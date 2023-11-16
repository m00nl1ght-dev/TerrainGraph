namespace TerrainGraph;

public class PathTracer
{
    public readonly int GridSizeX;
    public readonly int GridSizeZ;
    
    public readonly double[,] MainGrid;
    public readonly double[,] DepthGrid;
    public readonly double[,] OffsetGrid;

    public PathTracer(int gridSizeX, int gridSizeZ)
    {
        GridSizeX = gridSizeX;
        GridSizeZ = gridSizeZ;
        
        MainGrid = new double[gridSizeX, gridSizeZ];
        DepthGrid = new double[gridSizeX, gridSizeZ];
        OffsetGrid = new double[gridSizeX, gridSizeZ];
    }

    public void Trace(Path path)
    {
        // TODO
    }
}
