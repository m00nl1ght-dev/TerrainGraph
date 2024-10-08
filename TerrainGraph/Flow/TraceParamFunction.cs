using TerrainGraph.Util;

#pragma warning disable CS0659

namespace TerrainGraph.Flow;

public abstract class TraceParamFunction
{
    public abstract double ValueFor(PathTracer tracer, TraceTask task, Vector2d pos, double dist, double stability);

    public class FromGrid : TraceParamFunction
    {
        private readonly IGridFunction<double> Grid;

        public FromGrid(IGridFunction<double> grid)
        {
            Grid = grid;
        }

        public override double ValueFor(PathTracer tracer, TraceTask task, Vector2d pos, double dist, double stability)
        {
            return Grid.ValueAt(pos);
        }

        protected bool Equals(FromGrid other)
        {
            return Grid.Equals(other.Grid);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((FromGrid) obj);
        }

        public override string ToString()
        {
            return Grid.ToString();
        }
    }
}
