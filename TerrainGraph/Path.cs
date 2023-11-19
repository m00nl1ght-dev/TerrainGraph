using System;
using System.Collections.Generic;
using System.Linq;
using TerrainGraph.Util;

namespace TerrainGraph;

public class Path
{
    public const double MaxWidth = 200;

    public static readonly Path Empty = new();

    public IReadOnlyList<Origin> Origins => _origins;

    public IEnumerable<Segment> Leaves() => _origins.SelectMany(b => b.Leaves());

    private readonly List<Origin> _origins;

    public Path()
    {
        _origins = new(4);
    }

    public Path(Path other)
    {
        _origins = other._origins.Select(o => new Origin(o)).ToList();
    }

    public Origin AddOrigin(Vector2d position, double baseValue, double baseAngle, double baseWidth, double baseSpeed)
    {
        if (this == Empty) throw new InvalidOperationException();

        var origin = new Origin()
        {
            Position = position,
            BaseValue = baseValue,
            BaseAngle = baseAngle,
            BaseWidth = baseWidth,
            BaseSpeed = baseSpeed
        };

        _origins.Add(origin);

        return origin;
    }

    public void Combine(Path other)
    {
        if (this == Empty) throw new InvalidOperationException();

        foreach (var origin in other._origins)
        {
            var matching = _origins.FirstOrDefault(o => o.SelfEquals(origin));

            if (matching != null)
            {
                matching.Combine(origin);
            }
            else
            {
                _origins.Add(new Origin(origin));
            }
        }
    }

    public class Origin
    {
        public Vector2d Position;

        public double BaseValue;
        public double BaseAngle;
        public double BaseWidth = 1;
        public double BaseSpeed = 1;

        public IReadOnlyList<Segment> Branches => _branches;

        private readonly List<Segment> _branches;

        public Origin()
        {
            _branches = new(4);
        }

        public Origin(Origin other)
        {
            Position = other.Position;
            BaseValue = other.BaseValue;
            BaseAngle = other.BaseAngle;
            BaseWidth = other.BaseWidth;
            BaseSpeed = other.BaseSpeed;
            _branches = other._branches.Select(b => new Segment(b)).ToList();
        }

        public Segment AttachNewBranch(double relAngle = 0, double relWidth = 1, double relSpeed = 1, double length = 0)
        {
            var branch = new Segment()
            {
                RelAngle = relAngle.NormalizeDeg(),
                RelWidth = relWidth.WithMin(0),
                RelSpeed = relSpeed,
                Length = length.WithMin(0)
            };

            _branches.Add(branch);

            return branch;
        }

        public IEnumerable<Segment> Leaves()
        {
            return _branches?.SelectMany(b => b.Leaves()) ?? Enumerable.Empty<Segment>();
        }

        public void Combine(Origin other)
        {
            foreach (var branch in other._branches)
            {
                var matching = _branches.FirstOrDefault(b => b.SelfEquals(branch));

                if (matching != null)
                {
                    matching.Combine(branch);
                }
                else
                {
                    _branches.Add(new Segment(branch));
                }
            }
        }

        public bool SelfEquals(Origin other) =>
            Position.Equals(other.Position) &&
            BaseValue.Equals(other.BaseValue) &&
            BaseAngle.Equals(other.BaseAngle) &&
            BaseWidth.Equals(other.BaseWidth) &&
            BaseSpeed.Equals(other.BaseSpeed);
    }

    public class Segment
    {
        public double RelAngle;
        public double RelWidth = 1;
        public double RelSpeed = 1;
        public double Length;

        public ExtendParams ExtendParams;

        public IReadOnlyList<Segment> Branches => _branches;

        private readonly List<Segment> _branches;

        public Segment()
        {
            _branches = new(4);
        }

        public Segment(Segment other)
        {
            RelAngle = other.RelAngle;
            RelWidth = other.RelWidth;
            RelSpeed = other.RelSpeed;
            Length = other.Length;
            ExtendParams = other.ExtendParams;
            _branches = other._branches.Select(b => new Segment(b)).ToList();
        }

        public Segment ExtendWithParams(ExtendParams extendParams, double length = 0)
        {
            if (Length == 0 || ExtendParams.Equals(extendParams))
            {
                ExtendParams = extendParams;
                Length += length.WithMin(0);
                return this;
            }

            var segment = new Segment
            {
                Length = length.WithMin(0),
                ExtendParams = extendParams
            };

            segment._branches.AddRange(_branches);

            _branches.Clear();
            _branches.Add(segment);

            return segment;
        }

        public Segment AttachNewBranch(double relAngle = 0, double relWidth = 1, double relSpeed = 1, double length = 0)
        {
            var branch = new Segment()
            {
                RelAngle = relAngle.NormalizeDeg(),
                RelWidth = relWidth.WithMin(0),
                RelSpeed = relSpeed,
                Length = length.WithMin(0)
            };

            _branches.Add(branch);

            return branch;
        }

        public IEnumerable<Segment> Leaves()
        {
            if (_branches.NullOrEmpty())
            {
                yield return this;
            }
            else
            {
                foreach (var segment in _branches)
                {
                    foreach (var leaf in segment.Leaves())
                    {
                        yield return leaf;
                    }
                }
            }
        }

        public void Combine(Segment other)
        {
            foreach (var branch in other._branches)
            {
                var matching = _branches.FirstOrDefault(b => b.SelfEquals(branch));

                if (matching != null)
                {
                    matching.Combine(branch);
                }
                else
                {
                    _branches.Add(new Segment(branch));
                }
            }
        }

        public bool SelfEquals(Segment other) =>
            RelAngle.Equals(other.RelAngle) &&
            RelWidth.Equals(other.RelWidth) &&
            RelSpeed.Equals(other.RelSpeed) &&
            Length.Equals(other.Length) &&
            ExtendParams.Equals(other.ExtendParams);
    }

    public struct ExtendParams
    {
        public double WidthLoss;
        public double SpeedLoss;

        public IGridFunction<double> AbsFollowGrid;
        public IGridFunction<double> RelFollowGrid;
        public IGridFunction<double> SwerveGrid;
        public IGridFunction<double> WidthGrid;
        public IGridFunction<double> SpeedGrid;

        public bool Equals(ExtendParams other) =>
            WidthLoss.Equals(other.WidthLoss) &&
            SpeedLoss.Equals(other.SpeedLoss) &&
            ReferenceEquals(AbsFollowGrid, other.AbsFollowGrid) &&
            ReferenceEquals(RelFollowGrid, other.RelFollowGrid) &&
            ReferenceEquals(SwerveGrid, other.SwerveGrid) &&
            ReferenceEquals(WidthGrid, other.WidthGrid) &&
            ReferenceEquals(SpeedGrid, other.SpeedGrid);
    }
}
