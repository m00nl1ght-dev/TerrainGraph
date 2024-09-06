using System;
using System.Collections.Generic;
using TerrainGraph.Util;

namespace TerrainGraph.Flow;

public readonly struct TraceFrame
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
    /// Local multiplier for the extent to the left at the current position.
    /// </summary>
    public readonly double emLeft = 1;

    /// <summary>
    /// Local multiplier for the extent to the right at the current position.
    /// </summary>
    public readonly double emRight = 1;

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
    }

    /// <summary>
    /// Construct the initial frame for the given trace task.
    /// </summary>
    /// <param name="tracer">Path tracer that has the current task</param>
    /// <param name="task">Task for the next path segment to trace</param>
    /// <param name="distOffset">Offset applied to the initial trace distance</param>
    public TraceFrame(PathTracer tracer, TraceTask task, double distOffset = 0)
    {
        var b = task.baseFrame;
        var s = task.segment;

        this.angle = (b.angle + s.RelAngle).NormalizeDeg();
        this.width = task.WidthAt(distOffset);
        this.density = task.DensityAt(distOffset);
        this.speed = task.SpeedAt(distOffset);
        this.value = b.value + s.RelValue + distOffset * (distOffset < 0 ? speed : b.speed);
        this.offset = b.offset + s.RelOffset - s.RelShift * b.width * b.density;
        this.normal = Vector2d.Direction(-angle);
        this.pos = b.pos + s.RelPosition + s.RelShift * b.perpCCW * b.width + distOffset * normal;
        this.emLeft = task.segment.TraceParams.ExtentLeft?.ValueFor(tracer, task, pos, distOffset, 0) ?? 1;
        this.emRight = task.segment.TraceParams.ExtentRight?.ValueFor(tracer, task, pos, distOffset, 0) ?? 1;
        this.dist = distOffset;
    }

    internal TraceFrame(List<TraceResult> mergingSegments)
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
    }

    private TraceFrame(
        Vector2d pos, Vector2d normal,
        double angle, double width, double speed,
        double density, double value, double offset,
        double dist, double emLeft, double emRight)
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
        this.emLeft = emLeft;
        this.emRight = emRight;
    }

    /// <summary>
    /// Move the frame forward in its current direction, returning the result as a new frame.
    /// </summary>
    /// <param name="tracer">Path tracer that has the current task</param>
    /// <param name="task">Task with parameters defining how the values of the frame should evolve</param>
    /// <param name="distDelta">Distance to move forward in the current direction</param>
    /// <param name="angleDelta">Total angle change to be applied continuously while advancing</param>
    /// <param name="extraValue">Additional value delta to be applied continuously while advancing</param>
    /// <param name="extraOffset">Additional offset delta to be applied continuously while advancing</param>
    /// <param name="pivotPoint">Pivot point resulting from the angle change and distance</param>
    /// <param name="pivotOffset">Signed distance of the pivot point from the frame position</param>
    /// <param name="radial">If true, the path will advance along a circle arc, otherwise linearly</param>
    /// <returns></returns>
    public TraceFrame Advance(
        PathTracer tracer, TraceTask task, double distDelta, double angleDelta, double extraValue,
        double extraOffset, out Vector2d pivotPoint, out double pivotOffset, bool radial)
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

        var mLeft = task.segment.TraceParams.ExtentLeft?.ValueFor(tracer, task, newPos, newDist, 0) ?? 1;
        var mRight = task.segment.TraceParams.ExtentRight?.ValueFor(tracer, task, newPos, newDist, 0) ?? 1;
        var mSpeed = task.segment.TraceParams.Speed?.ValueFor(tracer, task, newPos, newDist, 0) ?? 1;

        newValue += distDelta * (dist >= 0 ? speed * mSpeed : speed);

        return new TraceFrame(newPos, newNormal, newAngle,
            width - distDelta * task.segment.TraceParams.WidthLoss,
            speed - distDelta * task.segment.TraceParams.SpeedLoss,
            density - distDelta * task.segment.TraceParams.DensityLoss,
            newValue, newOffset, newDist, mLeft, mRight
        );
    }

    /// <summary>
    /// Move the frame forward in its current direction, returning the resulting position.
    /// </summary>
    /// <param name="distDelta">Distance to move forward in the current direction</param>
    /// <param name="angleDelta">Total angle change to be applied continuously while advancing</param>
    /// <param name="radial">If true, the path will advance along a circle arc, otherwise linearly</param>
    /// <returns></returns>
    public Vector2d AdvancePos(double distDelta, double angleDelta, bool radial)
    {
        var newAngle = (angle + angleDelta).NormalizeDeg();
        var newNormal = Vector2d.Direction(-newAngle);

        if (radial && angleDelta != 0)
        {
            var pivotOffset = 180 * distDelta / (Math.PI * -angleDelta);
            var pivotPoint = pos + perpCCW * pivotOffset;

            return pivotPoint - newNormal.PerpCCW * pivotOffset;
        }

        return pos + distDelta * normal;
    }

    public bool PossiblyInBounds(Vector2d minI, Vector2d maxE, double extraWidth)
    {
        var p1 = pos + perpCW * (width / 2 * emRight + extraWidth / 2);
        var p2 = pos + perpCCW * (width / 2 * emLeft + extraWidth / 2);

        if (p1.x < minI.x && p2.x < minI.x) return false;
        if (p1.z < minI.z && p2.z < minI.z) return false;
        if (p1.x >= maxE.x && p2.x >= maxE.x) return false;
        if (p1.z >= maxE.z && p2.z >= maxE.z) return false;

        return true;
    }

    public bool PossiblyOutOfBounds(Vector2d minI, Vector2d maxE, double extraWidth)
    {
        var p1 = pos + perpCW * (width / 2 * emRight + extraWidth / 2);
        var p2 = pos + perpCCW * (width / 2 * emLeft + extraWidth / 2);

        return !p1.InBounds(minI, maxE) || !p2.InBounds(minI, maxE);
    }

    public override string ToString() =>
        $"{nameof(pos)}: {pos}, " +
        $"{nameof(angle)}: {angle:F2}, " +
        $"{nameof(normal)}: {normal}, " +
        $"{nameof(width)}: {width:F2}, " +
        $"{nameof(speed)}: {speed:F2}, " +
        $"{nameof(value)}: {value:F2}, " +
        $"{nameof(offset)}: {offset:F2}, " +
        $"{nameof(density)}: {density:F2}, " +
        $"{nameof(emLeft)}: {emLeft:F2}, " +
        $"{nameof(emRight)}: {emRight:F2}, " +
        $"{nameof(dist)}: {dist:F2}";
}
