using System;
using System.Collections.Generic;
using System.Linq;
using TerrainGraph.Util;
using static TerrainGraph.Flow.TraceParamFunction;
using static TerrainGraph.GridFunction;

namespace TerrainGraph.Flow;

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
                            ownSegment.Attach(ownBranch);
                        }

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

        public Vector2d RelPosition;

        public IReadOnlyList<SmoothDelta> ExtraDelta;

        public TraceParams TraceParams;

        public int AdjustmentCount = 0;

        public double InitialAngleDeltaMin;

        public IEnumerable<Segment> Parents => _parents.Select(id => Path._segments[id]);
        public IEnumerable<Segment> Branches => _branches.Select(id => Path._segments[id]);

        public IReadOnlyList<int> ParentIds => _parents;
        public IReadOnlyList<int> BranchIds => _branches;

        public int ParentCount => _parents.Count;
        public int BranchCount => _branches.Count;

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
            InitialAngleDeltaMin = other.InitialAngleDeltaMin;
            RelPosition = other.RelPosition;
            ExtraDelta = other.ExtraDelta;
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

                if (discard && branch.ParentCount == 0)
                {
                    branch.Discard();
                }
            }
        }

        public void Attach(Segment branch)
        {
            if (branch.Path != Path) throw new InvalidOperationException();
            this._branches.AddUnique(branch.Id);
            this._branches.Sort((a, b) => Path._segments[a].RelShift.CompareTo(Path._segments[b].RelShift));
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

            foreach (var branch in Branches.ToList())
            {
                Detach(branch);

                if (branch.ParentCount == 0)
                {
                    branch.Discard();
                }
            }
        }

        public void ApplyDelta(SmoothDelta delta)
        {
            ExtraDelta = ExtraDelta != null ? [..ExtraDelta, delta] : [delta];
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

        public List<Segment> LinearParents()
        {
            return ConnectedSegments(false, true,
                s => s.BranchCount == 1 || s == this,
                s => s.ParentCount == 1
            );
        }

        public IEnumerable<Segment> CoParents()
        {
            return Branches.SelectMany(b => b.Parents).Where(b => b != this);
        }

        public IEnumerable<Segment> Siblings()
        {
            return Parents.SelectMany(b => b.Branches).Where(b => b != this);
        }

        public int FullStepsCount(bool allowSingle)
        {
            var stepSize = TraceParams.StepSize.WithMin(1);
            if (Length < stepSize && Length >= 1 && allowSingle) return 1;
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
            InitialAngleDeltaMin.Equals(other.InitialAngleDeltaMin) &&
            Equals(ExtraDelta, other.ExtraDelta) &&
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

        public override string ToString() =>
            $"{nameof(ValueDelta)}: {ValueDelta}, " +
            $"{nameof(OffsetDelta)}: {OffsetDelta}, " +
            $"{nameof(StepsTotal)}: {StepsTotal}, " +
            $"{nameof(StepsStart)}: {StepsStart}, " +
            $"{nameof(StepsPadding)}: {StepsPadding}";
    }

    public readonly struct DiversionPoint
    {
        public readonly Vector2d Position;
        public readonly Vector2d Diversion;
        public readonly double Range;

        public DiversionPoint(Vector2d position, Vector2d diversion, double range)
        {
            Position = position;
            Diversion = diversion;
            Range = range;
        }

        public override string ToString() =>
            $"{nameof(Position)}: {Position}, " +
            $"{nameof(Diversion)}: {Diversion}, " +
            $"{nameof(Range)}: {Range}";
    }

    [HotSwappable]
    public struct TraceParams
    {
        public double StepSize;
        public double WidthLoss;
        public double SpeedLoss;
        public double DensityLoss;
        public double AngleTenacity;
        public double SplitTenacity;
        public double AngleLimitAbs;
        public double ArcRetraceFactor;
        public double ArcRetraceRange;
        public double ArcStableRange;
        public double MergeResultTrim;
        public double SplitTurnLock;

        public bool StaticAngleTenacity;
        public bool AdjustmentPriority;
        public bool ResultUnstable;
        public bool PreventMerge;

        public Vector2d? Target;

        public TraceParamFunction Cost;
        public TraceParamFunction Swerve;
        public TraceParamFunction ExtentLeft;
        public TraceParamFunction ExtentRight;
        public TraceParamFunction Speed;
        public TraceParamFunction DensityLeft;
        public TraceParamFunction DensityRight;

        public IReadOnlyCollection<DiversionPoint> DiversionPoints;

        /// <summary>
        /// Calculate the maximum expected extent multiplier at the given position, used for angle limit calculations.
        /// Never lower than 1 because of local stability that could potentially be applied later.
        /// </summary>
        public double MaxExtentFactor(PathTracer tracer, TraceTask task, Vector2d pos, double dist)
        {
            return Math.Max(
                ExtentLeft?.ValueFor(tracer, task, pos, dist, 0) ?? 1,
                ExtentRight?.ValueFor(tracer, task, pos, dist, 0) ?? 1
            ).WithMin(1);
        }

        public void ApplyFixedAngle(double angleDelta, bool stable)
        {
            Swerve = new FromGrid(Of((double) Math.Sign(angleDelta)));
            AngleLimitAbs = angleDelta.Abs();

            AngleTenacity = 0;
            SplitTenacity = 0;

            ResultUnstable = false;
            DiversionPoints = null;
            Cost = null;
            Target = null;

            if (stable)
            {
                WidthLoss = 0;
                SpeedLoss = 0;
                DensityLoss = 0;
                Speed = null;
            }
        }

        public void AddDiversionPoint(DiversionPoint point)
        {
            DiversionPoints = DiversionPoints != null ? [..DiversionPoints, point] : [point];
        }

        public bool Equals(TraceParams other) =>
            StepSize.Equals(other.StepSize) &&
            WidthLoss.Equals(other.WidthLoss) &&
            SpeedLoss.Equals(other.SpeedLoss) &&
            DensityLoss.Equals(other.DensityLoss) &&
            AngleTenacity.Equals(other.AngleTenacity) &&
            SplitTenacity.Equals(other.SplitTenacity) &&
            AngleLimitAbs.Equals(other.AngleLimitAbs) &&
            ArcRetraceFactor.Equals(other.ArcRetraceFactor) &&
            ArcRetraceRange.Equals(other.ArcRetraceRange) &&
            ArcStableRange.Equals(other.ArcStableRange) &&
            MergeResultTrim.Equals(other.MergeResultTrim) &&
            SplitTurnLock.Equals(other.SplitTurnLock) &&
            StaticAngleTenacity == other.StaticAngleTenacity &&
            AdjustmentPriority == other.AdjustmentPriority &&
            Equals(Cost, other.Cost) &&
            Equals(Swerve, other.Swerve) &&
            Equals(ExtentLeft, other.ExtentLeft) &&
            Equals(ExtentRight, other.ExtentRight) &&
            Equals(Speed, other.Speed) &&
            Equals(DensityLeft, other.DensityLeft) &&
            Equals(DensityRight, other.DensityRight) &&
            Equals(DiversionPoints, other.DiversionPoints) &&
            Target == other.Target;

        public override string ToString() =>
            $"{nameof(StepSize)}: {StepSize}, " +
            $"{nameof(WidthLoss)}: {WidthLoss:F2}, " +
            $"{nameof(SpeedLoss)}: {SpeedLoss:F2}, " +
            $"{nameof(DensityLoss)}: {DensityLoss:F2}, " +
            $"{nameof(AngleTenacity)}: {AngleTenacity:F2}, " +
            $"{nameof(SplitTenacity)}: {SplitTenacity:F2}, " +
            $"{nameof(AngleLimitAbs)}: {AngleLimitAbs:F2}, " +
            $"{nameof(ArcRetraceFactor)}: {ArcRetraceFactor:F2}, " +
            $"{nameof(ArcRetraceRange)}: {ArcRetraceRange:F2}, " +
            $"{nameof(ArcStableRange)}: {ArcStableRange:F2}, " +
            $"{nameof(MergeResultTrim)}: {MergeResultTrim:F2}, " +
            $"{nameof(SplitTurnLock)}: {SplitTurnLock:F2}, " +
            $"{nameof(StaticAngleTenacity)}: {StaticAngleTenacity}, " +
            $"{nameof(AdjustmentPriority)}: {AdjustmentPriority}, " +
            $"{nameof(Cost)}: {Cost}, " +
            $"{nameof(Swerve)}: {Swerve}, " +
            $"{nameof(ExtentLeft)}: {ExtentLeft}, " +
            $"{nameof(ExtentRight)}: {ExtentRight}, " +
            $"{nameof(Speed)}: {Speed}, " +
            $"{nameof(DensityLeft)}: {DensityLeft}, " +
            $"{nameof(DensityRight)}: {DensityRight}, " +
            $"{nameof(DiversionPoints)}: {DiversionPoints?.Count ?? 0}, " +
            $"{nameof(Target)}: {Target}";
    }
}
