namespace TerrainGraph;

public interface ICurveFunction<out T>
{
    public abstract T ValueAt(double x);
}
