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

    [ThreadStatic]
    private static Queue<Segment> _ts_queue;

    [ThreadStatic]
    private static Queue<double> _ts_values;

    [ThreadStatic]
    private static List<Segment> _ts_visited;

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

        _ts_queue ??= new Queue<Segment>(8);
        _ts_visited ??= new List<Segment>(8);

        try
        {
            foreach (var otherSegment in other.Roots)
            {
                var ownSegment = Roots.FirstOrDefault(s => s.SelfEquals(otherSegment));

                if (ownSegment == null)
                {
                    ownSegment = new Segment(this);
                    ownSegment.CopyFrom(otherSegment);
                }

                segIdMap[otherSegment.Id] = ownSegment;
                _ts_queue.Enqueue(otherSegment);
            }

            while (_ts_queue.Count > 0)
            {
                var otherSegment = _ts_queue.Dequeue();
                var ownSegment = segIdMap[otherSegment.Id];

                if (_ts_visited.AddUnique(otherSegment))
                {
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
                        _ts_queue.Enqueue(otherBranch);
                    }
                }
            }
        }
        finally
        {
            _ts_queue.Clear();
            _ts_visited.Clear();
        }
    }

    private Path EnsureMutable()
    {
        if (this == Empty) throw new InvalidOperationException();
        return this;
    }

    [HotSwappable]
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

        public double LocalStabilityAtTail = -9999;
        public double LocalStabilityAtHead = -9999;

        public Vector2d RelPosition;

        public SmoothDelta SmoothDelta;

        public TraceParams TraceParams;

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
            LocalStabilityAtTail = other.LocalStabilityAtTail;
            LocalStabilityAtHead = other.LocalStabilityAtHead;
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

        public void DetachAll(bool discard = false)
        {
            foreach (var branch in Branches.ToList())
            {
                Detach(branch);
                if (discard) branch.Discard();
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

        public void Discard()
        {
            this.RelWidth = 0;
            this.Length = 0;
        }

        public void ApplyLocalStabilityAtTail(double constantRange, double linearRange)
        {
            ApplyLocalStability(constantRange, linearRange, false);
        }

        public void ApplyLocalStabilityAtHead(double constantRange, double linearRange)
        {
            ApplyLocalStability(constantRange, linearRange, true);
        }

        private void ApplyLocalStability(double constantRange, double linearRange, bool backwards)
        {
            constantRange = constantRange.WithMin(0);
            linearRange = linearRange.WithMin(1);

            var totalRange = constantRange + linearRange;
            var initialValue = 1 + constantRange / linearRange;

            _ts_queue ??= new Queue<Segment>(8);
            _ts_visited ??= new List<Segment>(8);
            _ts_values ??= new Queue<double>(8);

            try
            {
                _ts_queue.Enqueue(this);
                _ts_values.Enqueue(0);

                while (_ts_queue.Count > 0)
                {
                    var segment = _ts_queue.Dequeue();
                    var distance = _ts_values.Dequeue();

                    if (_ts_visited.AddUnique(segment))
                    {
                        var a = (1 - distance / totalRange) * initialValue;

                        distance += segment.Length;

                        var b = (1 - distance / totalRange) * initialValue;

                        if (b > 0)
                        {
                            foreach (var next in backwards ? segment.Parents : segment.Branches)
                            {
                                _ts_queue.Enqueue(next);
                                _ts_values.Enqueue(distance);
                            }
                        }

                        var newTail = backwards ? b : a;
                        var newHead = backwards ? a : b;

                        var oldTail = segment.LocalStabilityAtTail;
                        var oldHead = segment.LocalStabilityAtHead;

                        if (newTail >= oldTail && newHead >= oldHead)
                        {
                            segment.LocalStabilityAtTail = newTail;
                            segment.LocalStabilityAtHead = newHead;
                        }
                        else if (newTail > oldTail || newHead > oldHead)
                        {
                            var maxTail = Math.Max(oldTail, newTail);
                            var minTail = Math.Min(oldTail, newTail);
                            var maxHead = Math.Max(oldHead, newHead);
                            var minHead = Math.Min(oldHead, newHead);

                            var p = (maxTail - minTail) / (maxTail - minHead - minTail + maxHead);

                            if (p is >= 0 and <= 1)
                            {
                                var inserted = segment.InsertNew();

                                inserted.Length = (1 - p) * segment.Length;
                                segment.Length -= inserted.Length;

                                segment.LocalStabilityAtTail = maxTail;
                                segment.LocalStabilityAtHead = maxTail + (minHead - maxTail) * p;
                                inserted.LocalStabilityAtTail = segment.LocalStabilityAtHead;
                                inserted.LocalStabilityAtHead = maxHead;
                            }
                        }
                    }
                }
            }
            finally
            {
                _ts_queue.Clear();
                _ts_visited.Clear();
                _ts_values.Clear();
            }
        }

        public bool IsParentOf(Segment other, bool includeSelf)
        {
            return AnyBranchesMatch(s => s == other, includeSelf);
        }

        public bool IsDirectParentOf(Segment other, bool includeSelf)
        {
            return (includeSelf && other == this) || (Path == other.Path && _branches.Contains(other.Id));
        }

        public bool IsBranchOf(Segment other, bool includeSelf)
        {
            return AnyParentsMatch(s => s == other, includeSelf);
        }

        public bool IsDirectBranchOf(Segment other, bool includeSelf)
        {
            return (includeSelf && other == this) || (Path == other.Path && _parents.Contains(other.Id));
        }

        public bool IsDirectSiblingOf(Segment other, bool includeSelf)
        {
            return other.Path == Path && _parents.Any(other._parents.Contains) && (other != this || includeSelf);
        }

        public bool AnyBranchesMatch(Predicate<Segment> condition, bool includeSelf)
        {
            _ts_queue ??= new Queue<Segment>(8);
            _ts_visited ??= new List<Segment>(8);

            try
            {
                _ts_queue.Enqueue(this);

                while (_ts_queue.Count > 0)
                {
                    var segment = _ts_queue.Dequeue();

                    if (_ts_visited.AddUnique(segment))
                    {
                        if ((includeSelf || segment != this) && condition(segment)) return true;
                        foreach (var branch in segment.Branches) _ts_queue.Enqueue(branch);
                    }
                }
            }
            finally
            {
                _ts_queue.Clear();
                _ts_visited.Clear();
            }

            return false;
        }

        public bool AnyParentsMatch(Predicate<Segment> condition, bool includeSelf)
        {
            _ts_queue ??= new Queue<Segment>(8);
            _ts_visited ??= new List<Segment>(8);

            try
            {
                _ts_queue.Enqueue(this);

                while (_ts_queue.Count > 0)
                {
                    var segment = _ts_queue.Dequeue();

                    if (_ts_visited.AddUnique(segment))
                    {
                        if ((includeSelf || segment != this) && condition(segment)) return true;
                        foreach (var parent in segment.Parents) _ts_queue.Enqueue(parent);
                    }
                }
            }
            finally
            {
                _ts_queue.Clear();
                _ts_visited.Clear();
            }

            return false;
        }

        public List<Segment> ConnectedSegments(
            bool fwd = true, bool bwd = true,
            Predicate<Segment> entryCondition = null,
            Predicate<Segment> exitCondition = null)
        {
            _ts_queue ??= new Queue<Segment>(8);

            var connected = new List<Segment>();

            try
            {
                _ts_queue.Enqueue(this);

                while (_ts_queue.Count > 0)
                {
                    var segment = _ts_queue.Dequeue();

                    if (entryCondition == null || entryCondition(segment))
                    {
                        if (connected.AddUnique(segment))
                        {
                            if (exitCondition == null || exitCondition(segment))
                            {
                                if (fwd) foreach (var branch in segment.Branches) _ts_queue.Enqueue(branch);
                                if (bwd) foreach (var parent in segment.Parents) _ts_queue.Enqueue(parent);
                            }
                        }
                    }
                }
            }
            finally
            {
                _ts_queue.Clear();
            }

            return connected;
        }

        public int FullStepsCount(bool allowSingle)
        {
            var stepSize = TraceParams.StepSize.WithMin(1);
            if (Length < stepSize && allowSingle) return 1;
            return (int) Math.Floor(Length / stepSize);
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
        public readonly int StepsPadding;

        public SmoothDelta(double valueDelta, double offsetDelta, int stepsTotal, int stepsStart, int stepsPadding)
        {
            ValueDelta = valueDelta;
            OffsetDelta = offsetDelta;
            StepsTotal = stepsTotal;
            StepsStart = stepsStart;
            StepsPadding = stepsPadding;
        }

        public bool Equals(SmoothDelta other) =>
            ValueDelta.Equals(other.ValueDelta) &&
            OffsetDelta.Equals(other.OffsetDelta) &&
            StepsTotal.Equals(other.StepsTotal) &&
            StepsStart.Equals(other.StepsStart) &&
            StepsPadding.Equals(other.StepsPadding);
    }

    [HotSwappable]
    public struct TraceParams
    {
        public double StepSize;
        public double WidthLoss;
        public double SpeedLoss;
        public double DensityLoss;
        public double AngleTenacity;
        public double AvoidOverlap;
        public double ArcRetraceRange;
        public double ArcStableRange;

        public IGridFunction<double> AbsFollowGrid;
        public IGridFunction<double> RelFollowGrid;
        public IGridFunction<double> SwerveGrid;
        public IGridFunction<double> WidthGrid;
        public IGridFunction<double> SpeedGrid;
        public IGridFunction<double> DensityGrid;

        public void ApplyFixedAngle(double angleDelta, bool stable)
        {
            AvoidOverlap = 0;
            ArcRetraceRange = 0;
            ArcStableRange = 0;
            AngleTenacity = 0;

            AbsFollowGrid = null;
            RelFollowGrid = null;

            SwerveGrid = Of(angleDelta);

            if (stable)
            {
                WidthLoss = 0;
                SpeedLoss = 0;
                DensityLoss = 0;
                SpeedGrid = null;
            }
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
                ArcStableRange = t.Lerp(a.ArcStableRange, b.ArcStableRange),
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
            ArcStableRange.Equals(other.ArcStableRange) &&
            Equals(AbsFollowGrid, other.AbsFollowGrid) &&
            Equals(RelFollowGrid, other.RelFollowGrid) &&
            Equals(SwerveGrid, other.SwerveGrid) &&
            Equals(WidthGrid, other.WidthGrid) &&
            Equals(SpeedGrid, other.SpeedGrid) &&
            Equals(DensityGrid, other.DensityGrid);
    }
}
