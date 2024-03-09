using System;
using System.Collections.Generic;
using TerrainGraph.Util;

namespace TerrainGraph.Flow;

[HotSwappable]
internal readonly struct TraceFrame
{
    /// <summary>
    /// The absolute position in the grid.
    /// </summary>
    public readonly Vector2d pos;

    /// <summary>
    /// The unit vector pointing in the current direction.
    /// </summary>
    public readonly Vector2d normal;

    /// <summary>
    /// The angle in degrees pointing in the current direction.
    /// Note: Positive angles rotate clockwise in x/z grid space.
    /// </summary>
    public readonly double angle;

    /// <summary>
    /// The path width at the current position.
    /// </summary>
    public readonly double width;

    /// <summary>
    /// The rate of value change at the current position.
    /// </summary>
    public readonly double speed;

    /// <summary>
    /// The output value at the current position.
    /// </summary>
    public readonly double value;

    /// <summary>
    /// The offset at the current position.
    /// </summary>
    public readonly double offset;

    /// <summary>
    /// The offset density at the current position.
    /// </summary>
    public readonly double density;

    /// <summary>
    /// The total distance traveled so far from the start of the segment.
    /// </summary>
    public readonly double dist;

    /// <summary>
    /// Multipliers based on the trace parameters at the current position.
    /// </summary>
    public readonly TraceFactors factors;

    /// <summary>
    /// The path width at the current position, with local multiplier applied.
    /// </summary>
    public double widthMul => width * factors.width.ScaleAround(1, factors.scalar);

    /// <summary>
    /// The rate of value change at the current position, with local multiplier applied.
    /// </summary>
    public double speedMul => speed * factors.speed.ScaleAround(1, factors.scalar);

    /// <summary>
    /// The offset density at the current position, with local multiplier applied.
    /// </summary>
    public double densityMul => density * factors.density.ScaleAround(1, factors.scalar);

    /// <summary>
    /// The unit vector perpendicular clockwise to the current direction.
    /// </summary>
    public Vector2d perpCW => normal.PerpCW;

    /// <summary>
    /// The unit vector perpendicular counter-clockwise to the current direction.
    /// </summary>
    public Vector2d perpCCW => normal.PerpCCW;

    /// <summary>
    /// Construct an origin frame at the given position.
    /// </summary>
    public TraceFrame(Vector2d pos)
    {
        this.width = 1;
        this.speed = 1;
        this.density = 1;
        this.pos = pos;
        this.normal = new Vector2d(1, 0);
        this.factors = new TraceFactors();
    }

    /// <summary>
    /// Construct the initial frame for tracing the given path segment.
    /// </summary>
    /// <param name="parent">Last frame of the preceding path segment</param>
    /// <param name="segment">Next path segment to trace</param>
    /// <param name="gridOffset">Offset applied when retrieving factors from external grids</param>
    /// <param name="distOffset">Offset applied to the initial trace distance</param>
    public TraceFrame(TraceFrame parent, Path.Segment segment, Vector2d gridOffset, double distOffset = 0)
    {
        this.angle = (parent.angle + segment.RelAngle).NormalizeDeg();
        this.width = parent.width * segment.RelWidth - distOffset * segment.TraceParams.WidthLoss;
        this.speed = parent.speed * segment.RelSpeed - distOffset * segment.TraceParams.SpeedLoss;
        this.value = parent.value + segment.RelValue + distOffset * (distOffset < 0 ? speed : parent.speed);
        this.offset = parent.offset + segment.RelOffset - segment.RelShift * parent.widthMul * parent.densityMul;
        this.normal = Vector2d.Direction(-angle);
        this.pos = parent.pos + segment.RelPosition + segment.RelShift * parent.perpCCW * parent.widthMul + distOffset * normal;
        this.factors = new TraceFactors(segment, pos - gridOffset, distOffset);
        this.density = parent.density * segment.RelDensity;
        this.dist = distOffset;
    }

    public TraceFrame(List<TraceResult> mergingSegments)
    {
        if (mergingSegments.Count == 0) return;

        foreach (var result in mergingSegments)
        {
            this.normal += result.finalFrame.normal;
            this.width += result.finalFrame.width;
            this.speed += result.finalFrame.speed;
            this.value += result.finalFrame.value;
            this.density += result.finalFrame.density;
        }

        var widthAvg = this.width / mergingSegments.Count;

        foreach (var result in mergingSegments)
        {
            var widthFactor = result.finalFrame.width / widthAvg;

            this.pos += result.finalFrame.pos * widthFactor;
            this.offset += result.finalFrame.offset * widthFactor;
        }

        this.pos /= mergingSegments.Count;
        this.normal /= mergingSegments.Count;
        this.speed /= mergingSegments.Count;
        this.value /= mergingSegments.Count;
        this.offset /= mergingSegments.Count;
        this.density /= mergingSegments.Count;

        var minDot = 0d;

        foreach (var result in mergingSegments)
        {
            minDot = Math.Min(minDot, Vector2d.Dot(result.finalFrame.pos - pos, normal));
        }

        // move result frame backwards up to the farthest import
        this.pos += this.normal * minDot;

        this.angle = -Vector2d.SignedAngle(Vector2d.AxisX, this.normal);
        this.factors = new TraceFactors();
    }

    private TraceFrame(
        Vector2d pos, Vector2d normal,
        double angle, double width, double speed,
        double density, double value, double offset,
        double dist, TraceFactors factors)
    {
        this.pos = pos;
        this.normal = normal;
        this.angle = angle;
        this.width = width;
        this.speed = speed;
        this.density = density;
        this.value = value;
        this.offset = offset;
        this.dist = dist;
        this.factors = factors;
    }

    /// <summary>
    /// Move the frame forward in its current direction, returning the result as a new frame.
    /// </summary>
    /// <param name="segment">Segment with parameters defining how the values of the frame should evolve</param>
    /// <param name="distDelta">Distance to move forward in the current direction</param>
    /// <param name="angleDelta">Total angle change to be applied continuously while advancing</param>
    /// <param name="extraValue">Additional value delta to be applied continuously while advancing</param>
    /// <param name="extraOffset">Additional offset delta to be applied continuously while advancing</param>
    /// <param name="gridOffset">Offset applied when retrieving factors from external grids</param>
    /// <param name="pivotPoint">Pivot point resulting from the angle change and distance</param>
    /// <param name="pivotOffset">Signed distance of the pivot point from the frame position</param>
    /// <param name="radial">If true, the path will advance along a circle arc, otherwise linearly</param>
    /// <returns></returns>
    public TraceFrame Advance(
        Path.Segment segment, double distDelta, double angleDelta, double extraValue, double extraOffset,
        Vector2d gridOffset, out Vector2d pivotPoint, out double pivotOffset, bool radial)
    {
        var newAngle = (angle + angleDelta).NormalizeDeg();
        var newNormal = Vector2d.Direction(-newAngle);

        Vector2d newPos;

        if (radial && angleDelta != 0)
        {
            pivotOffset = 180 * distDelta / (Math.PI * -angleDelta);
            pivotPoint = pos + perpCCW * pivotOffset;

            newPos = pivotPoint - newNormal.PerpCCW * pivotOffset;
        }
        else
        {
            pivotOffset = 0d;
            pivotPoint = pos;

            newPos = pos + distDelta * normal;
        }

        var newDist = dist + distDelta;
        var newValue = value + extraValue;
        var newOffset = offset + extraOffset;

        newValue += distDelta * (dist >= 0 ? speedMul : speed);

        return new TraceFrame(newPos, newNormal, newAngle,
            width - distDelta * segment.TraceParams.WidthLoss,
            speed - distDelta * segment.TraceParams.SpeedLoss,
            density - distDelta * segment.TraceParams.DensityLoss,
            newValue, newOffset, newDist,
            new TraceFactors(segment, pos - gridOffset, newDist)
        );
    }

    public bool PossiblyInBounds(Vector2d minI, Vector2d maxE)
    {
        var p1 = pos + perpCW * 0.5 * widthMul;
        var p2 = pos + perpCCW * 0.5 * widthMul;

        if (p1.x < minI.x && p2.x < minI.x) return false;
        if (p1.z < minI.z && p2.z < minI.z) return false;
        if (p1.x >= maxE.x && p2.x >= maxE.x) return false;
        if (p1.z >= maxE.z && p2.z >= maxE.z) return false;

        return true;
    }

    public bool PossiblyOutOfBounds(Vector2d minI, Vector2d maxE)
    {
        var p1 = pos + perpCW * 0.5 * widthMul;
        var p2 = pos + perpCCW * 0.5 * widthMul;

        return !p1.InBounds(minI, maxE) || !p2.InBounds(minI, maxE);
    }

    public override string ToString() =>
        $"{nameof(pos)}: {pos}, " +
        $"{nameof(angle)}: {angle}, " +
        $"{nameof(normal)}: {normal}, " +
        $"{nameof(width)}: {width}, " +
        $"{nameof(speed)}: {speed}, " +
        $"{nameof(value)}: {value}, " +
        $"{nameof(offset)}: {offset}, " +
        $"{nameof(density)}: {density}, " +
        $"{nameof(dist)}: {dist}, " +
        $"{nameof(factors)}: [{factors}]";
}
