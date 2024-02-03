using System;
using System.Collections.Generic;
using System.Linq;
using TerrainGraph.Util;
using static TerrainGraph.Flow.Path;
using static TerrainGraph.GridFunction;

namespace TerrainGraph.Flow;

[HotSwappable]
public class PathTracer
{
    private const int MaxTraceFrames = 1_000_000;

    public double RadialThreshold = 0.5;
    public double CollisionAdjMinDist = 5;

    public bool StopWhenOutOfBounds = true;

    public readonly Vector2d GridInnerSize;
    public readonly Vector2d GridOuterSize;
    public readonly Vector2d GridMargin;

    public readonly double TraceInnerMargin;
    public readonly double TraceOuterMargin;

    private readonly double[,] _mainGrid;
    private readonly double[,] _valueGrid;
    private readonly double[,] _offsetGrid;
    private readonly double[,] _distanceGrid;
    private readonly double[,] _debugGrid;
    private readonly Segment[,] _segmentGrid;

    public IGridFunction<double> MainGrid => BuildGridFunction(_mainGrid);
    public IGridFunction<double> ValueGrid => BuildGridFunction(_valueGrid);
    public IGridFunction<double> OffsetGrid => BuildGridFunction(_offsetGrid);
    public IGridFunction<double> DistanceGrid => BuildGridFunction(_distanceGrid);
    public IGridFunction<double> DebugGrid => BuildGridFunction(_debugGrid);

    public readonly TraceCollisionHandler CollisionHandler;

    private readonly GridKernel _followGridKernel = GridKernel.Square(3, 5);
    private readonly GridKernel _avoidGridKernel = GridKernel.Shield(2, 5, 3);

    private readonly IGridFunction<double> _overlapAvoidanceGrid = Zero;

    private List<TraceFrame> _frameBuffer = new(50);

    private int _totalFramesCalculated;

    // TODO this is bad for performance, strings still get constructed
    public static readonly Action<string> DebugOff = _ => {};

    public static Action<string> DebugOutput = DebugOff;

    public List<TraceDebugLine> DebugLines;

    public PathTracer(
        int innerSizeX, int innerSizeZ, int gridMargin,
        double traceInnerMargin, double traceOuterMargin)
    {
        TraceInnerMargin = traceInnerMargin.WithMin(0);
        TraceOuterMargin = traceOuterMargin.WithMin(TraceInnerMargin);

        gridMargin = gridMargin.WithMin(0);

        innerSizeX = innerSizeX.WithMin(0);
        innerSizeZ = innerSizeZ.WithMin(0);

        GridMargin = new Vector2d(gridMargin, gridMargin);

        var outerSizeX = innerSizeX + gridMargin * 2;
        var outerSizeZ = innerSizeZ + gridMargin * 2;

        GridInnerSize = new Vector2d(innerSizeX, innerSizeZ);
        GridOuterSize = new Vector2d(outerSizeX, outerSizeZ);

        _mainGrid = new double[outerSizeX, outerSizeZ];
        _valueGrid = new double[outerSizeX, outerSizeZ];
        _offsetGrid = new double[outerSizeX, outerSizeZ];
        _distanceGrid = new double[outerSizeX, outerSizeZ];
        _debugGrid = new double[outerSizeX, outerSizeZ];
        _segmentGrid = new Segment[outerSizeX, outerSizeZ];

        CollisionHandler = new TraceCollisionHandler(this);

        if (TraceOuterMargin > 0)
        {
            _overlapAvoidanceGrid = new ScaleWithBias(
                new Cache<double>(_distanceGrid, TraceOuterMargin), 1 / TraceOuterMargin, -1
            );
        }

        for (int x = 0; x < outerSizeX; x++)
        {
            for (int z = 0; z < outerSizeZ; z++)
            {
                _distanceGrid[x, z] = TraceOuterMargin;
            }
        }
    }

    /// <summary>
    /// Attempt to trace the given path, trying again in case of a collision.
    /// </summary>
    /// <param name="path">Path to trace</param>
    /// <param name="maxAttempts">Limit for the number of trace attempts</param>
    /// <returns>True if an attempt was successful, otherwise false</returns>
    public bool Trace(Path path, int maxAttempts = 50)
    {
        Preprocess(path);

        _totalFramesCalculated = 0;

        DebugLines?.Clear();

        var simulatedCollisions = new List<TraceCollision>();
        var occuredCollisions = new List<TraceCollision>();

        for (int attempt = 0; attempt < maxAttempts - 1; attempt++)
        {
            DebugOutput($"### ATTEMPT {attempt} ###");

            TryTrace(path, occuredCollisions);
            if (occuredCollisions.Count == 0) return true;
            Clear();

            simulatedCollisions.AddRange(occuredCollisions);
            occuredCollisions.Clear();

            var debugOutput = DebugOutput;
            DebugOutput = DebugOff;

            DebugOutput($"### SIM FOR ATTEMPT {attempt} ###");

            TryTrace(path, occuredCollisions, simulatedCollisions);
            if (occuredCollisions.Count == 0) return true;
            Clear();

            DebugOutput = debugOutput;

            CollisionHandler.HandleFirstCollision(simulatedCollisions);

            DebugLines?.Add(new TraceDebugLine(
                this, new Vector2d(7, 5 + attempt), 3, 0,
                $"Attempt {attempt} had {simulatedCollisions.Count} collisions")
            );

            simulatedCollisions.Clear();
            occuredCollisions.Clear();
        }

        DebugOutput($"### FINAL ATTEMPT ###");

        TryTrace(path, occuredCollisions);

        DebugLines?.Add(new TraceDebugLine(
            this, new Vector2d(7, 4 + maxAttempts), 1, 0,
            $"Final attempt had {occuredCollisions.Count} collisions")
        );

        return occuredCollisions.Count == 0;
    }

    public void Clear()
    {
        for (int x = 0; x < GridOuterSize.x; x++)
        {
            for (int z = 0; z < GridOuterSize.z; z++)
            {
                _mainGrid[x, z] = 0;
                _valueGrid[x, z] = 0;
                _offsetGrid[x, z] = 0;
                _distanceGrid[x, z] = TraceOuterMargin;
                _debugGrid[x, z] = 0;
                _segmentGrid[x, z] = null;
            }
        }
    }

    /// <summary>
    /// Adjust stability values within the given path for smoother splitting and merging.
    /// </summary>
    private void Preprocess(Path path)
    {
        foreach (var segment in path.Segments.ToList())
        {
            if (segment.BranchCount > 1)
            {
                foreach (var branch in segment.Branches)
                {
                    var rangeBranch = branch.TraceParams.ArcStableRange;
                    branch.ApplyLocalStabilityAtTail(rangeBranch / 2, rangeBranch / 2);
                }

                var rangeMain = segment.TraceParams.ArcStableRange;
                segment.ApplyLocalStabilityAtHead(0, rangeMain);
            }

            if (segment.ParentCount > 1)
            {
                foreach (var parent in segment.Parents)
                {
                    var rangeParent = parent.TraceParams.ArcStableRange;
                    parent.ApplyLocalStabilityAtHead(rangeParent / 2, rangeParent / 2);
                }

                var rangeMain = segment.TraceParams.ArcStableRange;
                segment.ApplyLocalStabilityAtTail(0, rangeMain / 2);
            }
        }
    }

    /// <summary>
    /// Attempt to trace the given path once.
    /// </summary>
    /// <param name="path">Path to trace</param>
    /// <param name="occuredCollisions">Collisions that occur while tracing will be added to this list</param>
    /// <param name="simulatedCollisions">List of collisions to be simulated, may be null if there are none</param>
    private void TryTrace(Path path, List<TraceCollision> occuredCollisions, List<TraceCollision> simulatedCollisions = null)
    {
        var taskQueue = new Queue<TraceTask>();
        var taskResults = new Dictionary<Segment, TraceResult>();
        var originFrame = new TraceFrame(GridMargin);

        foreach (var rootSegment in path.Roots)
        {
            if (rootSegment.RelWidth > 0)
            {
                Enqueue(rootSegment, originFrame, false);
            }
        }

        while (taskQueue.Count > 0)
        {
            var task = taskQueue.Dequeue();

            var result = TryTrace(task);

            taskResults[task.segment] = result;

            if (result.collision != null)
            {
                DebugOutput($"Collision happened: {result.collision}");
                occuredCollisions.Add(result.collision);
            }
            else if (result.finalFrame.width > 0)
            {
                var endInBounds = result.finalFrame.PossiblyInBounds(Vector2d.Zero, GridOuterSize);

                if (endInBounds || !result.everInBounds || !StopWhenOutOfBounds)
                {
                    foreach (var branch in task.segment.Branches)
                    {
                        if (branch.ParentCount <= 1)
                        {
                            Enqueue(branch, result.finalFrame, result.everInBounds);
                        }
                        else
                        {
                            if (branch.Parents.Any(p => !taskResults.ContainsKey(p))) continue;

                            var parentResults = branch.Parents.Select(p => taskResults[p]).ToList();
                            var mergedFrame = new TraceFrame(parentResults);

                            DebugOutput($"Merged frames {string.Join(" + ", branch.Parents.Select(b => b.Id))} into {branch.Id}");

                            Enqueue(branch, mergedFrame, parentResults.Any(r => r.everInBounds));
                        }
                    }
                }
                else
                {
                    DebugOutput($"End of segment {task.segment.Id} is out of bounds, no need to trace further");
                }
            }
        }

        return;

        void Enqueue(Segment branch, TraceFrame baseFrame, bool everInBounds)
        {
            if (taskResults.ContainsKey(branch) || taskQueue.Any(t => t.segment == branch)) return;

            var collisionList = simulatedCollisions?.Where(c => c.segmentB == branch).ToList();

            if (collisionList is { Count: 0 }) collisionList = null;

            var marginHead = branch.IsLeaf ? TraceInnerMargin : 0;
            var marginTail = branch.IsRoot ? TraceInnerMargin : 0;

            taskQueue.Enqueue(new TraceTask(branch, baseFrame, collisionList, marginHead, marginTail, everInBounds));
        }
    }

    /// <summary>
    /// Attempt to trace a single path segment, with the parameters defined by the given task.
    /// </summary>
    /// <exception cref="Exception">Thrown if MaxTraceFrames is exceeded during this task</exception>
    private TraceResult TryTrace(TraceTask task)
    {
        var length = task.segment.Length;
        var extParams = task.segment.TraceParams;

        var stepSize = task.segment.TraceParams.StepSize.WithMin(1);

        var initialFrame = new TraceFrame(task.baseFrame, task.segment, GridMargin, -task.marginTail);

        DebugOutput($"Segment {task.segment.Id} with length {length:F2} started with initial frame [{initialFrame}] and params {task.segment.TraceParams}");

        var everFullyInBounds = task.everInBounds || !initialFrame.PossiblyOutOfBounds(Vector2d.Zero, GridOuterSize);

        if (length <= 0) return new TraceResult(initialFrame, everFullyInBounds);

        var a = initialFrame;

        _frameBuffer.Clear();

        while (a.dist < length + task.marginHead)
        {
            _frameBuffer.Add(a);

            double distDelta = 0d;
            double angleDelta = 0d;
            double extraValue = 0d;
            double extraOffset = 0d;

            if (a.dist >= 0)
            {
                distDelta = Math.Min(stepSize, length + task.marginHead - a.dist);

                var followVec = Vector2d.Zero;

                if (extParams.AbsFollowGrid != null || extParams.RelFollowGrid != null)
                {
                    followVec = _followGridKernel.CalculateAt(
                        new(1, 0), new(0, 1),
                        extParams.AbsFollowGrid,
                        extParams.RelFollowGrid,
                        a.pos - GridMargin,
                        a.pos - initialFrame.pos,
                        initialFrame.angle - 90
                    );
                }

                if (extParams.AvoidOverlap > 0)
                {
                    followVec += extParams.AvoidOverlap * _avoidGridKernel.CalculateAt(
                        a.normal, a.perpCW,
                        _overlapAvoidanceGrid, null,
                        a.pos, Vector2d.Zero, 0
                    );
                }

                if (extParams.DiversionPoints != null)
                {
                    foreach (var avoidPoint in extParams.DiversionPoints)
                    {
                        var distance = Vector2d.Distance(avoidPoint.Position, a.pos);

                        if (distance < avoidPoint.Range)
                        {
                            followVec += avoidPoint.Diversion * (1 - distance / avoidPoint.Range);
                        }
                    }
                }

                if (followVec != Vector2d.Zero)
                {
                    angleDelta -= Vector2d.SignedAngle(a.normal, a.normal + followVec);
                }

                if (extParams.SwerveGrid != null)
                {
                    angleDelta += extParams.SwerveGrid.ValueAt(a.pos - GridMargin);
                }

                var maxAngleDelta = (1 - extParams.AngleTenacity) * 180 * distDelta / (a.width * Math.PI);
                angleDelta = (distDelta * angleDelta).NormalizeDeg().InRange(-maxAngleDelta, maxAngleDelta);

                if (task.segment.SmoothDelta != null)
                {
                    var smoothDelta = task.segment.SmoothDelta;

                    if (smoothDelta.StepsTotal <= 0)
                    {
                        extraValue += smoothDelta.ValueDelta;
                        extraOffset += smoothDelta.OffsetDelta;
                    }
                    else
                    {
                        var stepsDone = (int) Math.Floor(a.dist / stepSize);
                        var stepsTotal = (int) Math.Floor(length / stepSize);

                        var pointer = stepsDone;
                        var factor = 1d;

                        if (stepsDone == stepsTotal - 1)
                        {
                            factor = stepSize / (stepSize + length % stepSize);
                        }
                        else if (stepsDone == stepsTotal && stepsTotal > 0)
                        {
                            pointer = stepsTotal - 1;
                            factor = length % stepSize / (stepSize + length % stepSize);
                        }

                        var n = smoothDelta.StepsTotal;
                        var x = smoothDelta.StepsStart + pointer;

                        if (n > smoothDelta.StepsPadding * 2)
                        {
                            n -= smoothDelta.StepsPadding * 2;
                            x -= smoothDelta.StepsPadding;
                        }

                        var value = x < 0 || x >= n ? 0 : MathUtil.LinearDist(n, x);

                        extraValue += smoothDelta.ValueDelta * value * factor;
                        extraOffset += smoothDelta.OffsetDelta * value * factor;
                    }
                }
            }
            else
            {
                distDelta -= a.dist;
            }

            var b = a.Advance(
                task.segment, distDelta, angleDelta,
                extraValue, extraOffset, GridMargin,
                out var pivotPoint, out var pivotOffset,
                Math.Abs(angleDelta) >= RadialThreshold
            );

            var extendA = a.widthMul / 2;
            var extendB = b.widthMul / 2;

            var boundIa = extendA + TraceInnerMargin;
            var boundIb = extendB + TraceInnerMargin;
            var boundOa = extendA + TraceOuterMargin;
            var boundOb = extendB + TraceOuterMargin;

            if (extendA < 1 && a.dist >= 0)
            {
                DebugOutput($"Extend is less than 1 at {a.pos} for segment {task.segment.Id}");
                length = Math.Min(length, b.dist);
            }

            if (everFullyInBounds)
            {
                if (StopWhenOutOfBounds && !b.PossiblyInBounds(Vector2d.Zero, GridOuterSize))
                {
                    DebugOutput($"Trace frame at {b.pos} for segment {task.segment.Id} is now out of bounds");
                    length = Math.Min(length, b.dist);
                }
            }
            else if (!b.PossiblyOutOfBounds(Vector2d.Zero, GridOuterSize))
            {
                everFullyInBounds = true;
            }

            var boundP1 = a.pos + a.perpCCW * boundOa;
            var boundP2 = a.pos + a.perpCW * boundOa;
            var boundP3 = b.pos + b.perpCCW * boundOb;
            var boundP4 = b.pos + b.perpCW * boundOb;

            var boundMin = Vector2d.Min(Vector2d.Min(boundP1, boundP2), Vector2d.Min(boundP3, boundP4));
            var boundMax = Vector2d.Max(Vector2d.Max(boundP1, boundP2), Vector2d.Max(boundP3, boundP4));

            var xMax = (int) Math.Min(Math.Ceiling(boundMax.x), GridOuterSize.x - 1);
            var zMax = (int) Math.Min(Math.Ceiling(boundMax.z), GridOuterSize.z - 1);

            var xMin = (int) Math.Max(Math.Floor(boundMin.x), 0);
            var zMin = (int) Math.Max(Math.Floor(boundMin.z), 0);

            for (int x = xMin; x <= xMax; x++)
            {
                for (int z = zMin; z <= zMax; z++)
                {
                    var pos = new Vector2d(x, z);

                    var dotA = Vector2d.Dot(a.normal, pos - a.pos);
                    var dotB = Vector2d.Dot(b.normal, pos - b.pos);

                    if (dotA >= 0 && dotB < 0)
                    {
                        double shift;
                        double shiftAbs;

                        double progress = 0.5;

                        if (pivotOffset != 0)
                        {
                            var pivotVec = pos - pivotPoint;

                            shift = Math.Sign(-angleDelta) * (pivotVec.Magnitude - Math.Abs(pivotOffset));
                            shiftAbs = Math.Abs(shift);

                            if (shiftAbs <= boundIa || shiftAbs <= boundIb)
                            {
                                progress = Vector2d.Angle(a.pos - pivotPoint, pivotVec) / Math.Abs(angleDelta);
                            }
                        }
                        else
                        {
                            shift = -Vector2d.PerpDot(a.normal, pos - a.pos);
                            shiftAbs = Math.Abs(shift);

                            progress = dotA / distDelta;
                        }

                        var extend = progress.Lerp(extendA, extendB);

                        if (shiftAbs <= extend + TraceOuterMargin)
                        {
                            var preDist = _distanceGrid[x, z];
                            var nowDist = shiftAbs - extend;

                            if (nowDist < preDist)
                            {
                                _distanceGrid[x, z] = nowDist;
                            }

                            if (shiftAbs <= extend + TraceInnerMargin)
                            {
                                var dist = a.dist + distDelta * progress;

                                var value = progress.Lerp(a.value, b.value);
                                var offset = progress.Lerp(a.offset, b.offset);
                                var density = progress.Lerp(a.densityMul, b.densityMul);

                                if (nowDist < preDist)
                                {
                                    _valueGrid[x, z] = value;
                                    _offsetGrid[x, z] = offset + shift * density;
                                }

                                if (shiftAbs <= extend && dist >= 0 && dist <= length)
                                {
                                    if (_mainGrid[x, z] > 0)
                                    {
                                        var collided = _segmentGrid[x, z];

                                        if (CanCollide(task.segment, collided, dist))
                                        {
                                            return new TraceResult(a, everFullyInBounds, new TraceCollision
                                            {
                                                segmentA = task.segment,
                                                segmentB = collided,
                                                framesA = ExchangeFrameBuffer(),
                                                position = pos
                                            });
                                        }

                                        DebugOutput($"Ignoring collision {task.segment.Id} vs {collided.Id} at {pos}");
                                    }

                                    if (task.simulated != null)
                                    {
                                        foreach (var simulated in task.simulated)
                                        {
                                            if (simulated.position == pos && simulated.framesB == null)
                                            {
                                                simulated.framesB = ExchangeFrameBuffer(true);
                                            }
                                        }
                                    }

                                    _segmentGrid[x, z] = task.segment;
                                    _debugGrid[x, z] = task.segment.Id;
                                    _mainGrid[x, z] = extend;
                                }
                            }
                        }
                    }
                }
            }

            a = b;

            if (++_totalFramesCalculated > MaxTraceFrames)
            {
                throw new Exception("PathTracer exceeded frame limit");
            }
        }

        DebugOutput($"Segment {task.segment.Id} finished with final frame [{a}]");

        return new TraceResult(a, everFullyInBounds);
    }

    private bool CanCollide(Segment active, Segment passive, double dist)
    {
        if (dist < CollisionAdjMinDist)
        {
            if (active.IsBranchOf(passive, false)) return false;
            if (active.IsDirectSiblingOf(passive, false)) return false;
        }

        if (active.Length - dist < CollisionAdjMinDist)
        {
            if (active.CoParents().Any(p => p.IsBranchOf(passive, true))) return false;
        }

        return true;
    }

    /// <summary>
    /// Wrap the given raw grid array in a GridFunction, transforming values into map space.
    /// </summary>
    private IGridFunction<double> BuildGridFunction(double[,] grid)
    {
        if (GridMargin == Vector2d.Zero) return new Cache<double>(grid);
        return new Transform<double>(new Cache<double>(grid), -GridMargin.x, -GridMargin.z, 1, 1);
    }

    /// <summary>
    /// Create and set a new trace frame buffer, returning the old one.
    /// </summary>
    private List<TraceFrame> ExchangeFrameBuffer(bool copy = false)
    {
        var buffer = _frameBuffer;
        _frameBuffer = copy ? [..buffer] : new(50);
        return buffer;
    }
}
