using System;
using System.Collections.Generic;
using System.Linq;
using TerrainGraph.Util;
using static TerrainGraph.GridFunction;

namespace TerrainGraph;

public class Path
{
    public static readonly Path Empty = new();

    public IReadOnlyList<Segment> Segments => _segments;

    public IEnumerable<Segment> Roots => _segments.Where(b => b.IsRoot);
    public IEnumerable<Segment> Leaves => _segments.Where(b => b.IsLeaf);

    private readonly List<Segment> _segments;

    public Path()
    {
        _segments = new(10);
    }

    public Path(Path other)
    {
        _segments = new(other._segments.Count);

        foreach (var otherSegment in other._segments)
        {
            var segment = new Segment(this);
            segment.CopyFrom(otherSegment);
        }

        foreach (var otherSegment in other._segments)
        {
            var segment = _segments[otherSegment.Id];

            foreach (var id in otherSegment.BranchIds)
            {
                segment.Attach(_segments[id]);
            }
        }
    }

    public void Combine(Path other)
    {
        var segIdMap = new Segment[other._segments.Count];
        var segQueue = new Queue<Segment>();

        foreach (var otherSegment in other.Roots)
        {
            var ownSegment = Roots.FirstOrDefault(s => s.SelfEquals(otherSegment));

            if (ownSegment == null)
            {
                ownSegment = new Segment(this);
                ownSegment.CopyFrom(otherSegment);
            }

            segIdMap[otherSegment.Id] = ownSegment;
            segQueue.Enqueue(otherSegment);
        }

        while (segQueue.Count > 0)
        {
            var otherSegment = segQueue.Dequeue();
            var ownSegment = segIdMap[otherSegment.Id];

            foreach (var otherBranch in otherSegment.Branches)
            {
                var ownBranch = ownSegment.Branches.FirstOrDefault(s => s.SelfEquals(otherBranch));

                if (ownBranch == null)
                {
                    ownBranch = new Segment(this);
                    ownBranch.CopyFrom(otherBranch);
                }

                ownSegment.Attach(ownBranch);

                segIdMap[otherBranch.Id] = ownBranch;
                segQueue.Enqueue(otherBranch);
            }
        }
    }

    private Path EnsureMutable()
    {
        if (this == Empty) throw new InvalidOperationException();
        return this;
    }

    public class Segment
    {
        public readonly Path Path;
        public readonly int Id;

        public double Length;

        public double RelValue;
        public double RelOffset;
        public double RelShift;
        public double RelAngle;
        public double RelWidth = 1;
        public double RelSpeed = 1;
        public double RelDensity = 1;

        public Vector2d RelPosition;

        public SmoothDelta SmoothDelta;

        public TraceParams TraceParams;

        public int FullSteps => (int) Math.Floor(Length / TraceParams.StepSize.WithMin(1));

        public IEnumerable<Segment> Parents => _parents.Select(id => Path._segments[id]);
        public IEnumerable<Segment> Branches => _branches.Select(id => Path._segments[id]);

        public IReadOnlyList<int> ParentIds => _parents;
        public IReadOnlyList<int> BranchIds => _branches;

        public bool IsRoot => _parents.Count == 0;
        public bool IsLeaf => _branches.Count == 0;

        private readonly List<int> _parents = new(2);
        private readonly List<int> _branches = new(4);

        public Segment(Path path)
        {
            Path = path.EnsureMutable();
            Id = Path._segments.Count;
            Path._segments.Add(this);
        }

        public void CopyFrom(Segment other)
        {
            Length = other.Length;
            RelValue = other.RelValue;
            RelOffset = other.RelOffset;
            RelShift = other.RelShift;
            RelAngle = other.RelAngle;
            RelWidth = other.RelWidth;
            RelSpeed = other.RelSpeed;
            RelDensity = other.RelDensity;
            RelPosition = other.RelPosition;
            SmoothDelta = other.SmoothDelta;
            TraceParams = other.TraceParams;
        }

        public Segment ExtendWithParams(TraceParams traceParams, double length = 0)
        {
            if (Length == 0 || TraceParams.Equals(traceParams))
            {
                TraceParams = traceParams;
                Length += length.WithMin(0);
                return this;
            }

            var segment = InsertNew();
            segment.Length = length.WithMin(0);
            segment.TraceParams = traceParams;
            return segment;
        }

        public Segment AttachNew()
        {
            var segment = new Segment(Path)
            {
                TraceParams = TraceParams
            };

            Attach(segment);
            return segment;
        }

        public Segment InsertNew()
        {
            var segment = new Segment(Path)
            {
                TraceParams = TraceParams
            };

            foreach (var branch in Branches.ToList())
            {
                this.Detach(branch);
                segment.Attach(branch);
            }

            Attach(segment);
            return segment;
        }

        public void DetachAll()
        {
            foreach (var branch in Branches.ToList())
            {
                Detach(branch);
            }
        }

        public void Attach(Segment branch)
        {
            if (branch.Path != Path) throw new InvalidOperationException();
            this._branches.AddUnique(branch.Id);
            branch._parents.AddUnique(this.Id);
        }

        public void Detach(Segment branch)
        {
            if (branch.Path != Path) throw new InvalidOperationException();
            this._branches.Remove(branch.Id);
            branch._parents.Remove(this.Id);
        }

        public bool IsSupportOf(Segment other)
        {
            return other == this || Branches.Any(b => b.IsSupportOf(other));
        }

        public List<Segment> ConnectedSegments()
        {
            var queue = new Queue<Segment>(8);
            var visited = new List<Segment>(8);

            queue.Enqueue(this);

            while (queue.Count > 0)
            {
                var segment = queue.Dequeue();

                if (visited.AddUnique(segment))
                {
                    foreach (var branch in segment.Branches) queue.Enqueue(branch);
                    foreach (var parent in segment.Parents) queue.Enqueue(parent);
                }
            }

            return visited;
        }

        public bool SelfEquals(Segment other) =>
            Length.Equals(other.Length) &&
            RelValue.Equals(other.RelValue) &&
            RelOffset.Equals(other.RelOffset) &&
            RelShift.Equals(other.RelShift) &&
            RelAngle.Equals(other.RelAngle) &&
            RelWidth.Equals(other.RelWidth) &&
            RelSpeed.Equals(other.RelSpeed) &&
            RelDensity.Equals(other.RelDensity) &&
            RelPosition.Equals(other.RelPosition) &&
            Equals(SmoothDelta, other.SmoothDelta) &&
            TraceParams.Equals(other.TraceParams);
    }

    public class SmoothDelta
    {
        public readonly double ValueDelta;
        public readonly double OffsetDelta;

        public readonly int StepsTotal;
        public readonly int StepsStart;

        public SmoothDelta(double valueDelta, double offsetDelta, int stepsTotal, int stepsStart)
        {
            ValueDelta = valueDelta;
            OffsetDelta = offsetDelta;
            StepsTotal = stepsTotal;
            StepsStart = stepsStart;
        }

        public bool Equals(SmoothDelta other) =>
            ValueDelta.Equals(other.ValueDelta) &&
            OffsetDelta.Equals(other.OffsetDelta) &&
            StepsTotal.Equals(other.StepsTotal) &&
            StepsStart.Equals(other.StepsStart);
    }

    public struct TraceParams
    {
        public double StepSize;
        public double WidthLoss;
        public double SpeedLoss;
        public double DensityLoss;
        public double AngleTenacity;
        public double AvoidOverlap;
        public double ArcRetraceRange;

        public IGridFunction<double> AbsFollowGrid;
        public IGridFunction<double> RelFollowGrid;
        public IGridFunction<double> SwerveGrid;
        public IGridFunction<double> WidthGrid;
        public IGridFunction<double> SpeedGrid;
        public IGridFunction<double> DensityGrid;

        public void ApplyFixedAngle(double angleDelta)
        {
            AngleTenacity = 0;
            AvoidOverlap = 0;
            ArcRetraceRange = 0;
            AbsFollowGrid = null;
            RelFollowGrid = null;
            SwerveGrid = Of(angleDelta);
        }

        public static TraceParams Merge(TraceParams a, TraceParams b, double t = 0.5)
        {
            return new TraceParams
            {
                StepSize = t.Lerp(a.StepSize, b.StepSize),
                WidthLoss = t.Lerp(a.WidthLoss, b.WidthLoss),
                SpeedLoss = t.Lerp(a.SpeedLoss, b.SpeedLoss),
                DensityLoss = t.Lerp(a.DensityLoss, b.DensityLoss),
                AngleTenacity = t.Lerp(a.AngleTenacity, b.AngleTenacity),
                AvoidOverlap = t.Lerp(a.AvoidOverlap, b.AvoidOverlap),
                ArcRetraceRange = t.Lerp(a.ArcRetraceRange, b.ArcRetraceRange),
                AbsFollowGrid = Lerp.Of(a.AbsFollowGrid, b.AbsFollowGrid, t),
                RelFollowGrid = Lerp.Of(a.RelFollowGrid, b.RelFollowGrid, t),
                SwerveGrid = Lerp.Of(a.SwerveGrid, b.SwerveGrid, t),
                WidthGrid = Lerp.Of(a.WidthGrid, b.WidthGrid, t),
                SpeedGrid = Lerp.Of(a.SpeedGrid, b.SpeedGrid, t),
                DensityGrid = Lerp.Of(a.DensityGrid, b.DensityGrid, t)
            };
        }

        public bool Equals(TraceParams other) =>
            StepSize.Equals(other.StepSize) &&
            WidthLoss.Equals(other.WidthLoss) &&
            SpeedLoss.Equals(other.SpeedLoss) &&
            DensityLoss.Equals(other.DensityLoss) &&
            AngleTenacity.Equals(other.AngleTenacity) &&
            AvoidOverlap.Equals(other.AvoidOverlap) &&
            ArcRetraceRange.Equals(other.ArcRetraceRange) &&
            Equals(AbsFollowGrid, other.AbsFollowGrid) &&
            Equals(RelFollowGrid, other.RelFollowGrid) &&
            Equals(SwerveGrid, other.SwerveGrid) &&
            Equals(WidthGrid, other.WidthGrid) &&
            Equals(SpeedGrid, other.SpeedGrid) &&
            Equals(DensityGrid, other.DensityGrid);
    }
}
