using System;
using System.Collections.Generic;
using System.Linq;
using TerrainGraph.Util;
using static TerrainGraph.Flow.Path;
using static TerrainGraph.GridFunction;

namespace TerrainGraph.Flow;

public class PathTracer
{
    private const int MaxTraceFrames = 1_000_000;

    public double RadialThreshold = 0.5;
    public double CollisionMinValueDiff = 0.75;
    public double CollisionMinOffsetDiff = 0.5;
    public double CollisionCheckMargin = 0.5;
    public double CollisionMinValueDiffM = 5;
    public double CollisionMinOffsetDiffM = 5;
    public double CollisionMinParentDist = 2;
    public double MainGridSmoothLength = 1;
    public double WidthPatternResolution = 1;
    public double TraceLengthTolerance = 0.5;

    public bool StopWhenOutOfBounds = true;

    public readonly Vector2d GridInnerSize;
    public readonly Vector2d GridOuterSize;
    public readonly Vector2d GridMargin;

    public readonly double TraceInnerMargin;
    public readonly double TraceOuterMargin;

    internal readonly double[,] _mainGrid;
    internal readonly double[,] _sideGrid;
    internal readonly double[,] _valueGrid;
    internal readonly double[,] _offsetGrid;
    internal readonly double[,] _distanceGrid;
    internal readonly TraceTask[,] _taskGrid;

    #if DEBUG
    internal readonly double[,] _debugGrid;
    #endif

    public IGridFunction<double> MainGrid => BuildGridFunction(_mainGrid);
    public IGridFunction<double> SideGrid => BuildGridFunction(_sideGrid);
    public IGridFunction<double> ValueGrid => BuildGridFunction(_valueGrid);
    public IGridFunction<double> OffsetGrid => BuildGridFunction(_offsetGrid);
    public IGridFunction<double> DistanceGrid => BuildGridFunction(_distanceGrid, TraceOuterMargin);
    public IGridFunction<TraceTask> TaskGrid => BuildGridFunction(_taskGrid);

    #if DEBUG
    public IGridFunction<double> DebugGrid => BuildGridFunction(_debugGrid);
    #endif

    public readonly TraceCollisionHandler CollisionHandler;

    private readonly Dictionary<Segment, TraceResult> _traceResults = new();

    internal IReadOnlyDictionary<Segment, TraceResult> TraceResults => _traceResults;

    private readonly GridKernel _followGridKernel = GridKernel.Square(3, 3);

    private List<TraceFrame> _frameBuffer = new(50);

    private int _totalFramesCalculated;

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
        _sideGrid = new double[outerSizeX, outerSizeZ];
        _valueGrid = new double[outerSizeX, outerSizeZ];
        _offsetGrid = new double[outerSizeX, outerSizeZ];
        _distanceGrid = new double[outerSizeX, outerSizeZ];
        _taskGrid = new TraceTask[outerSizeX, outerSizeZ];

        #if DEBUG
        _debugGrid = new double[outerSizeX, outerSizeZ];
        #endif

        CollisionHandler = new TraceCollisionHandler(this);

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
        _totalFramesCalculated = 0;

        #if DEBUG
        DebugLog?.Clear();
        DebugLines?.Clear();
        #endif

        var collisions = new List<TraceCollision>();

        for (int attempt = 0;; attempt++)
        {
            #if DEBUG
            DebugOutput($"### ATTEMPT {attempt + 1} ###");
            #endif

            TryTrace(path);

            collisions.AddRange(_traceResults.Values.Select(r => r.collision).Where(c => c != null));

            if (collisions.Count == 0) return true;
            if (attempt + 1 >= maxAttempts) return false;

            Clear();

            #if DEBUG
            var debugLog = DebugLog;
            DebugLog = null;
            #endif

            TryTrace(path, collisions);

            #if DEBUG
            DebugLog = debugLog;
            DebugLine(new TraceDebugLine(this, new Vector2d(7, 5 + attempt), 3, 0, $"A {attempt + 1} C {collisions.Count}"));
            #endif

            CollisionHandler.HandleBestCollision(collisions);

            collisions.Clear();

            Clear();
        }
    }

    /// <summary>
    /// Clear all internal grids.
    /// </summary>
    public void Clear()
    {
        _traceResults.Clear();

        for (int x = 0; x < GridOuterSize.x; x++)
        {
            for (int z = 0; z < GridOuterSize.z; z++)
            {
                _mainGrid[x, z] = 0;
                _sideGrid[x, z] = 0;
                _valueGrid[x, z] = 0;
                _offsetGrid[x, z] = 0;
                _distanceGrid[x, z] = TraceOuterMargin;
                _taskGrid[x, z] = null;

                #if DEBUG
                _debugGrid[x, z] = 0;
                #endif
            }
        }
    }

    /// <summary>
    /// Attempt to trace the given path once.
    /// </summary>
    /// <param name="path">Path to trace</param>
    /// <param name="simulatedCollisions">List of collisions to be simulated, may be null if there are none</param>
    private void TryTrace(Path path, List<TraceCollision> simulatedCollisions = null)
    {
        var taskQueue = new Queue<TraceTask>();
        var originFrame = new TraceFrame(GridMargin);

        foreach (var rootSegment in path.Roots)
        {
            if (rootSegment.RelWidth > 0)
            {
                Enqueue(rootSegment, originFrame, null, 0, 0, false);
            }
        }

        while (taskQueue.Count > 0)
        {
            var task = taskQueue.Dequeue();

            var result = TryTrace(task);

            _traceResults[task.segment] = result;

            if (result.collision == null && !result.traceEnd && result.finalFrame.width > 0)
            {
                foreach (var branch in task.segment.Branches.OrderByDescending(b => b.RelWidth))
                {
                    if (branch.ParentCount <= 1)
                    {
                        var distFromRoot = task.distFromRoot + result.finalFrame.dist;
                        var widthBuildup = task.segment.BranchCount > 1 ? 0 : result.widthBuildup;
                        var branchParent = task.segment.Branches.Count() == 1 ? task.branchParent : null;

                        Enqueue(branch, result.finalFrame, branchParent, distFromRoot, widthBuildup, result.everInBounds);
                    }
                    else
                    {
                        if (branch.Parents.Any(p => !_traceResults.ContainsKey(p))) continue;

                        var parentResults = branch.Parents.Select(p => _traceResults[p]).ToList();

                        if (parentResults.Any(r => r.collision != null)) continue;

                        var maxDistFromRoot = task.distFromRoot + parentResults.Max(r => r.finalFrame.dist);

                        var mergedFrame = new TraceFrame(parentResults);

                        #if DEBUG
                        DebugOutput($"Merged frames {string.Join(" + ", branch.Parents.Select(b => b.Id))} into {branch.Id}");
                        #endif

                        Enqueue(branch, mergedFrame, null, maxDistFromRoot, 0, parentResults.Any(r => r.everInBounds));
                    }
                }
            }
        }

        return;

        void Enqueue(Segment branch, TraceFrame baseFrame, TraceTask branchParent, double distFromRoot, double widthBuildup, bool everInBounds)
        {
            if (_traceResults.ContainsKey(branch) || taskQueue.Any(t => t.segment == branch)) return;

            var collisionList = simulatedCollisions?.Where(c => c.taskB.segment == branch).ToList();

            if (collisionList is { Count: 0 }) collisionList = null;

            var marginHead = branch.IsLeaf ? 2 * TraceInnerMargin : 0;
            var marginTail = branch.IsRoot ? TraceInnerMargin : 0;

            taskQueue.Enqueue(new TraceTask(
                branch, baseFrame, branchParent, collisionList, marginHead, marginTail, distFromRoot, widthBuildup, everInBounds
            ));
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
        var turnLockLeft = task.TurnLockLeft(true);
        var turnLockRight = task.TurnLockRight(true);
        var widthBuildup = task.widthBuildup;
        var stepSize = extParams.StepSize.WithMin(1);

        var initialFrame = new TraceFrame(this, task, -task.marginTail);

        var nextFork = FindNextFork(task, Math.Max(extParams.ArcStableRange, MainGridSmoothLength.WithMin(1) * task.WidthAt(length)));
        var lastFork = FindLastFork(task, Math.Max(extParams.ArcStableRange, MainGridSmoothLength.WithMin(1) * task.WidthAt(0)));

        double StabilityAtDist(double dist)
        {
            var fromLast = 0d;
            var fromNext = 0d;

            var range = extParams.ArcStableRange;

            var distLast = lastFork.distance + dist;
            var distNext = nextFork.distance + task.segment.Length - dist;

            if (lastFork.type != ForkInfo.Type.None && distLast < range)
            {
                if (lastFork.type == ForkInfo.Type.Split)
                    fromLast = distLast <= range / 2 ? 1 : 1 - (2 * distLast - range) / range;
                else if (distLast < range)
                    fromLast = distLast <= 0 ? 1 : 1 - distLast / range;
            }

            if (nextFork.type != ForkInfo.Type.None && distNext < range)
            {
                if (nextFork.type == ForkInfo.Type.Merge)
                    fromNext = distNext <= range / 2 ? 1 : 1 - (2 * distNext - range) / range;
                else if (distNext < range)
                    fromNext = distNext <= 0 ? 1 : 1 - distNext / range;
            }

            return Math.Max(fromLast, fromNext);
        }

        #if DEBUG
        DebugOutput($"Segment {task.segment.Id} with length {length:F2} started with initial frame [{initialFrame}] and params {task.segment.TraceParams}");
        #endif

        var everFullyInBounds = task.everInBounds || !initialFrame.PossiblyOutOfBounds(Vector2d.Zero, GridOuterSize, widthBuildup);

        if (length <= 0) return new TraceResult(initialFrame, initialFrame, widthBuildup, everFullyInBounds, false);

        List<PathFinder.Node> pathNodes = null;

        var traceEnd = false;

        if (extParams.Target != null)
        {
            var target = extParams.Target.Value + GridMargin;

            (double, double) AngleLimitFunc(Vector2d pos, double dist)
            {
                var cdist = dist.WithMax(task.segment.Length);
                var width = extParams.StaticAngleTenacity ? initialFrame.width : initialFrame.width - cdist * extParams.WidthLoss;
                var limit = task.AngleLimitAt(dist, (width * extParams.MaxExtentFactor(this, task, pos - GridMargin, dist)).WithMin(1));

                if (extParams.AngleLimitAbs > 0 && limit > extParams.AngleLimitAbs)
                    limit = extParams.AngleLimitAbs;

                if (dist <= 0)
                {
                    if (task.segment.InitialAngleDeltaMin < 0)
                        return (limit, task.segment.InitialAngleDeltaMin.WithMin(-limit));
                    if (task.segment.InitialAngleDeltaMin > 0)
                        return (-task.segment.InitialAngleDeltaMin.WithMax(limit), limit);
                }

                return (dist < turnLockLeft ? 0 : limit, dist < turnLockRight ? 0 : limit);
            }

            var angleLimitBase = MathUtil.AngleLimit(initialFrame.width, extParams.AngleTenacity);
            if (extParams.AngleLimitAbs > 0 && angleLimitBase > extParams.AngleLimitAbs) angleLimitBase = extParams.AngleLimitAbs;

            var arcCount = angleLimitBase switch { < 2d => 2, < 5d => 3, _ => 4 };

            var pathFinder = new PathFinder(this, new PathFinder.ArcKernel(arcCount, stepSize * angleLimitBase, (int) stepSize))
            {
                AngleDeltaLimits = AngleLimitFunc,
                ObstacleThreshold = 100d,
                TargetAcceptRadius = stepSize,
                FullStepDistance = stepSize,
                QtClosedLoc = 0.5d * stepSize,
                QtOpenLoc = 0.5d * stepSize,
                PlanarTargetFallback = 3d * stepSize,
                IterationLimit = 10000
            };

            if (extParams.Cost != null)
            {
                pathFinder.LocalPathCost = (pos, dist) => extParams.Cost.ValueFor(this, task, pos - GridMargin, dist, StabilityAtDist(dist));
            }

            if (extParams.Swerve != null)
            {
                pathFinder.DirectionBias = (pos, dist) => extParams.Swerve.ValueFor(this, task, pos - GridMargin, dist, StabilityAtDist(dist));
            }

            for (int i = 0; i <= 3; i++)
            {
                pathFinder.HeuristicDistanceWeight = 1f + (float) Math.Pow(2, i);
                pathNodes = pathFinder.FindPath(initialFrame.pos, initialFrame.normal, target);
                if (pathNodes != null) break;
            }

            if (pathNodes == null)
            {
                task.segment.TraceParams.Target = extParams.Target = null;

                #if DEBUG
                throw new Exception($"Failed to find path from {initialFrame.pos} to {target}");
                #endif
            }
        }

        var a = initialFrame;

        var patternRes = WidthPatternResolution.WithMax(stepSize);
        var patternSteps = (int) Math.Ceiling(stepSize / patternRes) + 1;

        var extentLeftCache = new double[patternSteps];
        var extentRightCache = new double[patternSteps];
        var densityLeftCache = new double[patternSteps];
        var densityRightCache = new double[patternSteps];

        _frameBuffer.Clear();

        while (a.dist < length + task.marginHead)
        {
            double distDelta = 0d;
            double angleDelta = 0d;
            double extraValue = 0d;
            double extraOffset = 0d;

            if (a.dist >= 0)
            {
                _frameBuffer.Add(a);

                if (pathNodes != null)
                {
                    var node = pathNodes[_frameBuffer.Count];

                    angleDelta = -Vector2d.SignedAngle(a.normal, node.Direction);

                    var rad = angleDelta.Abs().ToRad();
                    var chord = Vector2d.Distance(a.pos, node.Position);

                    distDelta = angleDelta.Abs() < RadialThreshold ? chord : rad * 0.5 * chord / Math.Sin(0.5 * rad);

                    var distDeltaMax = length + task.marginHead - a.dist;
                    if (distDelta > distDeltaMax && distDeltaMax > 0)
                    {
                        angleDelta *= distDeltaMax / distDelta;
                        distDelta = distDeltaMax;
                    }

                    if (_frameBuffer.Count + 1 >= pathNodes.Count)
                    {
                        length = a.dist + distDelta;
                        pathNodes = null;
                    }
                }
                else
                {
                    distDelta = Math.Min(stepSize, length + task.marginHead - a.dist);

                    var followVec = Vector2d.Zero;
                    var newDist = a.dist + distDelta;

                    if (extParams.Cost != null)
                    {
                        followVec = _followGridKernel.CalculateFlowVecAt(
                            new(1, 0), new(0, 1), a.pos, pos => extParams.Cost.ValueFor(this, task, pos - GridMargin, newDist, StabilityAtDist(newDist))
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
                        angleDelta -= Vector2d.SignedAngle(a.normal, a.normal + followVec * distDelta) / distDelta;

                        #if DEBUG
                        DebugLine(new TraceDebugLine(this, a.pos, a.pos + a.normal, 4, 0, $"{angleDelta:F2}"));
                        DebugLine(new TraceDebugLine(this, a.pos + a.normal, a.pos + a.normal + followVec * distDelta, 5));
                        #endif
                    }

                    var widthForTenacity = extParams.StaticAngleTenacity ? initialFrame.width : a.width;
                    widthForTenacity *= extParams.MaxExtentFactor(this, task, a.pos - GridMargin, a.dist);

                    var angleLimit = task.AngleLimitAt(a.dist, widthForTenacity);
                    if (extParams.AngleLimitAbs > 0 && angleLimit > extParams.AngleLimitAbs) angleLimit = extParams.AngleLimitAbs;

                    if (extParams.Swerve != null)
                    {
                        angleDelta += angleLimit * extParams.Swerve.ValueFor(this, task, a.pos - GridMargin, a.dist, StabilityAtDist(a.dist));
                    }

                    if (a.dist < turnLockRight && angleDelta > 0) angleDelta = 0;
                    if (a.dist < turnLockLeft && angleDelta < 0) angleDelta = 0;

                    if (a.dist <= 0)
                    {
                        if (task.segment.InitialAngleDeltaMin < 0)
                            angleDelta = angleDelta.WithMax(task.segment.InitialAngleDeltaMin);
                        else if (task.segment.InitialAngleDeltaMin > 0)
                            angleDelta = angleDelta.WithMin(task.segment.InitialAngleDeltaMin);
                    }

                    angleDelta = (distDelta * angleDelta.InRange(-angleLimit, angleLimit)).NormalizeDeg();
                }

                if (task.segment.ExtraDelta != null)
                {
                    foreach (var smoothDelta in task.segment.ExtraDelta)
                    {
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
            }
            else
            {
                distDelta -= a.dist;
            }

            var radial = Math.Abs(angleDelta) >= RadialThreshold;

            var b = a.Advance(
                this, task, distDelta,
                angleDelta, extraValue, extraOffset,
                out var pivotPoint, out var pivotOffset, radial
            );

            #if DEBUG
            DebugLine(new TraceDebugLine(this, a.pos, b.pos, 1, 0, $"{angleDelta / distDelta:F2}"));
            #endif

            var pointStabilityA = 0d;
            var pointStabilityB = 0d;

            if (extParams.StabilityPoints != null)
            {
                foreach (var point in extParams.StabilityPoints)
                {
                    var distA = Vector2d.Distance(point.Position, a.pos);
                    var distB = Vector2d.Distance(point.Position, b.pos);

                    if (distA < point.Range) pointStabilityA = pointStabilityA.WithMin(1 - distA / point.Range);
                    if (distB < point.Range) pointStabilityB = pointStabilityB.WithMin(1 - distB / point.Range);
                }
            }

            var extentMax = 0d;

            for (int i = 0; i < patternSteps; i++)
            {
                var dist = a.dist + i * patternRes;
                var progress = i * patternRes / distDelta;

                var stability = StabilityAtDist(dist).WithMin(progress.Lerp(pointStabilityA, pointStabilityB));

                extentLeftCache[i] = extentRightCache[i] = progress.Lerp(a.width, b.width) / 2;
                densityLeftCache[i] = densityRightCache[i] = progress.Lerp(a.density, b.density);

                var pos = i == 0 || distDelta <= 0 ? a.pos : a.AdvancePos(i * patternRes, angleDelta * progress, radial);

                var extraExtent = widthBuildup / 2 * (1 - stability);
                if (extraExtent != 0)
                {
                    densityLeftCache[i] *= extentLeftCache[i] / (extentLeftCache[i] + extraExtent);
                    densityRightCache[i] *= extentRightCache[i] / (extentRightCache[i] + extraExtent);

                    extentLeftCache[i] += extraExtent;
                    extentRightCache[i] += extraExtent;
                }

                if (extParams.ExtentLeft != null)
                    extentLeftCache[i] *= extParams.ExtentLeft.ValueFor(this, task, pos, dist, stability);
                if (extParams.ExtentRight != null)
                    extentRightCache[i] *= extParams.ExtentRight.ValueFor(this, task, pos, dist, stability);
                if (extParams.DensityLeft != null)
                    densityLeftCache[i] *= extParams.DensityLeft.ValueFor(this, task, pos, dist, stability);
                if (extParams.DensityRight != null)
                    densityRightCache[i] *= extParams.DensityRight.ValueFor(this, task, pos, dist, stability);

                if (extentLeftCache[i] > extentMax) extentMax = extentLeftCache[i];
                if (extentRightCache[i] > extentMax) extentMax = extentRightCache[i];

                if (extParams.WidthBuildup != null)
                {
                    widthBuildup += extParams.WidthBuildup.ValueAt(pos) * patternRes.WithMax(length - dist);
                    widthBuildup = widthBuildup.WithMax(a.width);
                }
            }

            if (extentLeftCache[0] + extentRightCache[0] < 1 && a.dist >= 0)
            {
                length = Math.Min(length, b.dist);
                traceEnd = true;

                #if DEBUG
                DebugOutput($"Extend is less than 1 at {a.pos} for segment {task.segment.Id}");
                #endif
            }

            if (everFullyInBounds)
            {
                if (StopWhenOutOfBounds && !traceEnd && !b.PossiblyInBounds(Vector2d.Zero, GridOuterSize, widthBuildup))
                {
                    length = Math.Min(length, b.dist);
                    traceEnd = true;

                    #if DEBUG
                    DebugOutput($"Trace frame at {b.pos} for segment {task.segment.Id} is now out of bounds");
                    #endif
                }
            }
            else if (!b.PossiblyOutOfBounds(Vector2d.Zero, GridOuterSize, widthBuildup))
            {
                everFullyInBounds = true;
            }

            if (extParams.EndCondition != null && !traceEnd && CheckEndCondition(ref b, widthBuildup, extParams.EndCondition))
            {
                if (lastFork.type != ForkInfo.Type.Split || lastFork.distance + b.dist > MainGridSmoothLength * a.width)
                {
                    if (!task.segment.AnyBranchesMatch(s => s.ParentCount > 1, false))
                    {
                        length = Math.Min(length, b.dist);
                        traceEnd = true;

                        #if DEBUG
                        DebugOutput($"End condition fulfilled at {b.pos} for segment {task.segment.Id}");
                        #endif
                    }
                }
            }

            var boundP1 = a.pos + a.perpCCW * (extentMax + TraceOuterMargin);
            var boundP2 = a.pos + a.perpCW * (extentMax + TraceOuterMargin);
            var boundP3 = b.pos + b.perpCCW * (extentMax + TraceOuterMargin);
            var boundP4 = b.pos + b.perpCW * (extentMax + TraceOuterMargin);

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

                    if (dotA < 0 || dotB >= 0) continue;

                    var shift = pivotOffset != 0
                        ? Math.Sign(-angleDelta) * ((pos - pivotPoint).Magnitude - Math.Abs(pivotOffset))
                        : -Vector2d.PerpDot(a.normal, pos - a.pos);

                    var shiftAbs = Math.Abs(shift);

                    if (shiftAbs > extentMax + TraceOuterMargin) continue;

                    var progress = pivotOffset != 0
                        ? Vector2d.Angle(a.pos - pivotPoint, pos - pivotPoint) / Math.Abs(angleDelta)
                        : dotA / distDelta;

                    var patternStep = (int) Math.Floor(progress * distDelta / patternRes);
                    var patternLerp = (progress * distDelta) % patternRes / patternRes;

                    if (patternStep >= patternSteps - 1)
                    {
                        patternStep = patternSteps - 2;
                        patternLerp = 1d;
                    }

                    var extent = shift < 0
                        ? patternLerp.Lerp(extentLeftCache[patternStep], extentLeftCache[patternStep + 1])
                        : patternLerp.Lerp(extentRightCache[patternStep], extentRightCache[patternStep + 1]);

                    var nowDist = shiftAbs - extent;

                    if (nowDist > TraceOuterMargin) continue;

                    var dist = a.dist + distDelta * progress;

                    if (dist > length)
                    {
                        nowDist += dist - length;
                        if (nowDist > TraceOuterMargin) continue;
                    }
                    else if (dist < 0)
                    {
                        nowDist -= dist;
                        if (nowDist > TraceOuterMargin) continue;
                    }

                    var preDist = _distanceGrid[x, z];
                    var preTask = _taskGrid[x, z];

                    if (nowDist < preDist)
                    {
                        _distanceGrid[x, z] = nowDist;
                        _taskGrid[x, z] = task;
                    }

                    if (nowDist > TraceInnerMargin) continue;

                    var density = shift < 0
                        ? patternLerp.Lerp(densityLeftCache[patternStep], densityLeftCache[patternStep + 1])
                        : patternLerp.Lerp(densityRightCache[patternStep], densityRightCache[patternStep + 1]);

                    var value = progress.Lerp(a.value, b.value);
                    var offset = progress.Lerp(a.offset, b.offset) + shift * density;

                    if (nowDist <= CollisionCheckMargin && dist >= 0 && dist <= length + TraceLengthTolerance)
                    {
                        if (_mainGrid[x, z] > 0)
                        {
                            var valueDiff = Math.Abs(value - _valueGrid[x, z]);
                            var offsetDiff = Math.Abs(offset - _offsetGrid[x, z]);

                            var valueThr = nowDist <= 0 ? CollisionMinValueDiff : CollisionMinValueDiffM;
                            var offsetThr = nowDist <= 0 ? CollisionMinOffsetDiff : CollisionMinOffsetDiffM;

                            if (valueDiff >= valueThr || offsetDiff >= offsetThr)
                            {
                                if (dist >= CollisionMinParentDist || !task.segment.Parents.Contains(preTask.segment))
                                {
                                    #if DEBUG
                                    DebugOutput($"Collision {task.segment.Id} vs {preTask.segment.Id} at {pos} dist {dist} with value diff {valueDiff:F2} and offset diff {offsetDiff:F2}");
                                    DebugLine(new TraceDebugLine(this, new(x, z), 1));
                                    #endif

                                    var frames = ExchangeFrameBuffer();
                                    frames.Add(b);

                                    return new TraceResult(initialFrame, a, widthBuildup, everFullyInBounds, false, new TraceCollision
                                    {
                                        taskA = task,
                                        taskB = preTask,
                                        framesA = frames,
                                        progressA = progress,
                                        shiftA = shift,
                                        position = pos
                                    });
                                }
                            }

                            #if DEBUG
                            DebugOutput($"Ignoring collision {task.segment.Id} vs {preTask.segment.Id} at {pos} with value diff {valueDiff:F2} and offset diff {offsetDiff:F2}");
                            DebugLine(new TraceDebugLine(this, new(x, z), 4, 0, $"{dist:F2}"));
                            #endif
                        }

                        if (task.simulated != null)
                        {
                            foreach (var simulated in task.simulated)
                            {
                                if (simulated.position == pos && simulated.framesB == null)
                                {
                                    var frames = ExchangeFrameBuffer(true);
                                    frames.Add(b);

                                    simulated.framesB = frames;
                                    simulated.progressB = progress;
                                    simulated.shiftB = shift;
                                }
                            }
                        }

                        if (nowDist <= 0)
                        {
                            _mainGrid[x, z] = progress.Lerp(a.width, b.width);
                            _sideGrid[x, z] = shift;

                            #if DEBUG
                            _debugGrid[x, z] = StabilityAtDist(dist);
                            #endif

                            if (nextFork.type == ForkInfo.Type.Split)
                            {
                                _mainGrid[x, z] = _mainGrid[x, z].WithMax(task.WidthAt(0));

                                var splitDistance = nextFork.distance + (length - dist);
                                if (splitDistance <= MainGridSmoothLength.WithMin(1) * nextFork.baseWidth)
                                {
                                    ApplySmoothBranching(x, z, shift, extent, splitDistance, nextFork);
                                }
                            }

                            if (lastFork.type == ForkInfo.Type.Merge)
                            {
                                var mergeDistance = lastFork.distance + dist;
                                if (mergeDistance <= MainGridSmoothLength.WithMin(1) * lastFork.baseWidth)
                                {
                                    ApplySmoothBranching(x, z, shift, extent, mergeDistance, lastFork);
                                }
                            }
                        }
                    }

                    if (nowDist < preDist)
                    {
                        _valueGrid[x, z] = value;
                        _offsetGrid[x, z] = offset;
                    }
                }
            }

            a = b;

            if (++_totalFramesCalculated > MaxTraceFrames)
            {
                throw new Exception("PathTracer exceeded frame limit");
            }
        }

        #if DEBUG
        DebugOutput($"Segment {task.segment.Id} finished with final frame [{a}]");
        #endif

        return new TraceResult(initialFrame, a, widthBuildup, everFullyInBounds, traceEnd);
    }

    private bool CheckEndCondition(ref TraceFrame frame, double extraWidth, IGridFunction<double> widthMask)
    {
        if (widthMask.ValueAt(frame.pos - GridMargin) < frame.width + extraWidth) return false;
        if (widthMask.ValueAt(frame.pos - GridMargin + frame.normal * frame.width) < frame.width + extraWidth) return false;
        for (int extent = 1; extent <= Math.Ceiling(frame.width / 2 * frame.emLeft + extraWidth / 2); extent++)
            if (widthMask.ValueAt(frame.pos - GridMargin + frame.perpCCW * extent) < frame.width + extraWidth) return false;
        for (int extent = 1; extent <= Math.Ceiling(frame.width / 2 * frame.emRight + extraWidth / 2); extent++)
            if (widthMask.ValueAt(frame.pos - GridMargin + frame.perpCW * extent) < frame.width + extraWidth) return false;
        return true;
    }

    private void ApplySmoothBranching(int x, int z, double shift, double extent, double distance, ForkInfo info)
    {
        double relWidthA = 0d, relWidthB = 0d;
        double relPos = -shift / info.baseWidth + 0.5d;

        for (int i = 1; i < info.relWidths.Length; i++)
        {
            relWidthA = info.relWidths[i - 1];
            relWidthB = info.relWidths[i];

            relPos -= relWidthA;

            if (relPos < -relWidthA / 2) break;
            if (relPos > relWidthB / 2) continue;

            var coneLength = info.baseWidth * (relWidthA + relWidthB);
            if (distance <= coneLength)
            {
                var lerp = relPos.Abs() * info.baseWidth + extent * (distance / coneLength);
                _distanceGrid[x, z] = Math.Max(_distanceGrid[x, z], -lerp);

                #if DEBUG
                if ((distance - coneLength).Abs() <= 1d) DebugLine(new TraceDebugLine(this, new Vector2d(x, z)));
                #endif
            }

            break;
        }

        var relWidthC = relPos < 0 ? relWidthA : relWidthB;
        var shiftC = relPos < 0 ? relPos + relWidthA / 2 : relPos - relWidthB / 2;
        var fracSmooth = distance / (MainGridSmoothLength * info.baseWidth);
        _mainGrid[x, z] = fracSmooth.LerpClamped(info.baseWidth * relWidthC, _mainGrid[x, z]);
        _sideGrid[x, z] = fracSmooth.LerpClamped(-shiftC * info.baseWidth, _sideGrid[x, z]);
    }

    private ForkInfo FindNextFork(TraceTask task, double maxDistance)
    {
        var distance = 0d;
        var width = task.WidthAt(task.segment.Length);
        var current = task.segment;

        while (current.BranchCount > 0 && distance <= maxDistance)
        {
            if (current.BranchCount == 1)
            {
                current = current.Branches.First();

                if (current.ParentCount == 1)
                {
                    distance += current.Length;
                    width *= current.RelWidth;
                    width -= current.TraceParams.WidthLoss * current.Length;
                }
                else
                {
                    return new ForkInfo(ForkInfo.Type.Merge, distance, width, null);
                }
            }
            else if (current.BranchCount > 1)
            {
                var relWidths = current.Branches.Select(s => s.RelWidth).ToArray();
                return new ForkInfo(ForkInfo.Type.Split, distance, width, relWidths);
            }
        }

        return default;
    }

    private ForkInfo FindLastFork(TraceTask task, double maxDistance)
    {
        var distance = task.distFromRoot - task.branchParent.distFromRoot;
        if (distance > maxDistance) return default;

        var baseWidth = task.branchParent.WidthAt(0);
        if (baseWidth <= 0) return default;

        if (task.branchParent.segment.ParentCount > 1)
        {
            var relWidths = task.branchParent.segment.Parents
                .Select(s => _traceResults[s])
                .Where(r => r != null)
                .Select(r => r.finalFrame.width / baseWidth)
                .ToArray();

            return new ForkInfo(ForkInfo.Type.Merge, distance, baseWidth, relWidths);
        }

        if (task.branchParent.segment.Siblings().Any())
        {
            return new ForkInfo(ForkInfo.Type.Split, distance, baseWidth, null);
        }

        return default;
    }

    private double DistanceToLastSplit(TraceTask task, double maxDistance)
    {
        var distance = task.distFromRoot - task.branchParent.distFromRoot;
        return task.branchParent.segment.Siblings().Any() && distance <= maxDistance ? distance : -1;
    }

    private readonly struct ForkInfo
    {
        public readonly Type type;
        public readonly double distance;
        public readonly double baseWidth;
        public readonly double[] relWidths;

        public ForkInfo(Type type, double distance, double baseWidth, double[] relWidths)
        {
            this.type = type;
            this.distance = distance;
            this.baseWidth = baseWidth;
            this.relWidths = relWidths;
        }

        public enum Type
        {
            None, Split, Merge
        }
    }

    /// <summary>
    /// Wrap the given raw grid array in a GridFunction, transforming values into map space.
    /// </summary>
    private IGridFunction<T> BuildGridFunction<T>(T[,] grid, T fallback = default)
    {
        if (GridMargin == Vector2d.Zero) return new Cache<T>(grid);
        return new Transform<T>(new Cache<T>(grid, fallback), -GridMargin.x, -GridMargin.z);
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

    #if DEBUG

    public static List<string> DebugLog = [];
    public static List<TraceDebugLine> DebugLines = [];

    internal static void DebugOutput(string debug)
    {
        DebugLog?.Add($"[{DateTime.Now:hh:mm:ss.ff}] {debug}");
    }

    internal static void DebugLine(TraceDebugLine debugLine)
    {
        if (DebugLines != null && (DebugLines.Count < 50000 || debugLine.Color > 2))
        {
            DebugLines.Add(debugLine);
        }
    }

    #endif
}
