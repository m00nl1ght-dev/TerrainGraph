namespace TerrainGraph.Util;

public interface IRandom
{
    public int Next();

    public int Next(int min, int max);

    public double NextDouble();

    public double NextDouble(double min, double max);

    public void Reinitialise(int seed);
}
