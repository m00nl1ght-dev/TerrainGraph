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
    public double CollisionMinValueDiff = 0.75;
    public double CollisionMinOffsetDiff = 0.5;
    public double CollisionCheckMargin = 0.5;
    public double CollisionMinValueDiffM = 5;
    public double CollisionMinOffsetDiffM = 5;
    public double CollisionMinParentDist = 2;
    public double MainGridSmoothLength = 1; // TODO tweak

    public bool StopWhenOutOfBounds = true;

    public readonly Vector2d GridInnerSize;
    public readonly Vector2d GridOuterSize;
    public readonly Vector2d GridMargin;

    public readonly double TraceInnerMargin;
    public readonly double TraceOuterMargin;

    private readonly double[,] _mainGrid;
    private readonly double[,] _sideGrid;
    private readonly double[,] _valueGrid;
    private readonly double[,] _offsetGrid;
    private readonly double[,] _distanceGrid;
    private readonly TraceTask[,] _taskGrid;

    #if DEBUG
    private readonly double[,] _debugGrid;
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
        Preprocess(path);

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
    /// <param name="simulatedCollisions">List of collisions to be simulated, may be null if there are none</param>
    private void TryTrace(Path path, List<TraceCollision> simulatedCollisions = null)
    {
        var taskQueue = new Queue<TraceTask>();
        var originFrame = new TraceFrame(GridMargin);

        foreach (var rootSegment in path.Roots)
        {
            if (rootSegment.RelWidth > 0)
            {
                Enqueue(rootSegment, originFrame, null, 0, false);
            }
        }

        while (taskQueue.Count > 0)
        {
            var task = taskQueue.Dequeue();

            var result = TryTrace(task);

            _traceResults[task.segment] = result;

            if (result.collision == null && result.finalFrame.width > 0)
            {
                var endInBounds = result.finalFrame.PossiblyInBounds(Vector2d.Zero, GridOuterSize);

                if (endInBounds || !result.everInBounds || !StopWhenOutOfBounds)
                {
                    foreach (var branch in task.segment.Branches.OrderByDescending(b => b.RelWidth))
                    {
                        if (branch.ParentCount <= 1)
                        {
                            var distFromRoot = task.distFromRoot + result.finalFrame.dist;
                            var branchParent = task.segment.Branches.Count() == 1 ? task.branchParent : null;

                            Enqueue(branch, result.finalFrame, branchParent, distFromRoot, result.everInBounds);
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

                            Enqueue(branch, mergedFrame, null, maxDistFromRoot, parentResults.Any(r => r.everInBounds));
                        }
                    }
                }
                else
                {
                    #if DEBUG
                    DebugOutput($"End of segment {task.segment.Id} is out of bounds, no need to trace further");
                    #endif
                }
            }
        }

        return;

        void Enqueue(Segment branch, TraceFrame baseFrame, TraceTask branchParent, double distFromRoot, bool everInBounds)
        {
            if (_traceResults.ContainsKey(branch) || taskQueue.Any(t => t.segment == branch)) return;

            var collisionList = simulatedCollisions?.Where(c => c.taskB.segment == branch).ToList();

            if (collisionList is { Count: 0 }) collisionList = null;

            var marginHead = branch.IsLeaf ? TraceInnerMargin : 0;
            var marginTail = branch.IsRoot ? TraceInnerMargin : 0;

            taskQueue.Enqueue(new TraceTask(
                branch, baseFrame, branchParent, collisionList, marginHead, marginTail, distFromRoot, everInBounds
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
        var turnLockLeft = task.TurnLockLeft;
        var turnLockRight = task.TurnLockRight;

        var stepSize = task.segment.TraceParams.StepSize.WithMin(1);

        var initialFrame = new TraceFrame(this, task, -task.marginTail);

        var nextSplit = FindNextSplit(task, MainGridSmoothLength.WithMin(1) * task.WidthAt(length));
        var lastMerge = FindLastMerge(task, MainGridSmoothLength.WithMin(1) * task.WidthAt(0));

        #if DEBUG
        DebugOutput($"Segment {task.segment.Id} with length {length:F2} started with initial frame [{initialFrame}] and params {task.segment.TraceParams}");
        #endif

        var everFullyInBounds = task.everInBounds || !initialFrame.PossiblyOutOfBounds(Vector2d.Zero, GridOuterSize);

        if (length <= 0) return new TraceResult(initialFrame, initialFrame, everFullyInBounds);

        List<PathFinder.Node> pathNodes = null;

        if (extParams.Target != null)
        {
            var target = extParams.Target.Value + GridMargin;

            (double, double) AngleLimitFunc(Vector2d pos, double dist)
            {
                var cdist = dist.WithMax(task.segment.Length);
                var width = extParams.StaticAngleTenacity ? initialFrame.width : initialFrame.width - cdist * extParams.WidthLoss;
                var limit = task.AngleLimitAt(dist, width * extParams.MaxExtentFactor(this, task, pos - GridMargin, dist));
                if (extParams.AngleLimitAbs > 0 && limit > extParams.AngleLimitAbs) limit = extParams.AngleLimitAbs;
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
                pathFinder.LocalPathCost = (pos, dist) => extParams.Cost.ValueFor(this, task, pos - GridMargin, dist);
            }

            if (extParams.Swerve != null)
            {
                pathFinder.DirectionBias = (pos, dist) => extParams.Swerve.ValueFor(this, task, pos - GridMargin, dist);
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

                    #if DEBUG
                    DebugLine(new TraceDebugLine(this, node.Parent.Position, node.Position, 1, 0, $"{angleDelta / distDelta:F2}"));
                    #endif
                }
                else
                {
                    distDelta = Math.Min(stepSize, length + task.marginHead - a.dist);

                    var followVec = Vector2d.Zero;
                    var newDist = a.dist + distDelta;

                    if (extParams.Cost != null)
                    {
                        followVec = _followGridKernel.CalculateFlowVecAt(
                            new(1, 0), new(0, 1), a.pos, pos => extParams.Cost.ValueFor(this, task, pos - GridMargin, newDist)
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

                    var widthForTenacity = extParams.StaticAngleTenacity ? initialFrame.width : a.width;
                    widthForTenacity *= extParams.MaxExtentFactor(this, task, a.pos - GridMargin, a.dist);

                    var angleLimit = task.AngleLimitAt(a.dist, widthForTenacity);
                    if (extParams.AngleLimitAbs > 0 && angleLimit > extParams.AngleLimitAbs) angleLimit = extParams.AngleLimitAbs;

                    if (extParams.Swerve != null)
                    {
                        angleDelta += angleLimit * extParams.Swerve.ValueFor(this, task, a.pos - GridMargin, a.dist);
                    }

                    if (a.dist < turnLockRight && angleDelta > 0) angleDelta = 0;
                    if (a.dist < turnLockLeft && angleDelta < 0) angleDelta = 0;

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

            var b = a.Advance(
                this, task, distDelta,
                angleDelta, extraValue, extraOffset,
                out var pivotPoint, out var pivotOffset,
                Math.Abs(angleDelta) >= RadialThreshold
            );

            var extentLeftA = a.extentLeftMul;
            var extentRightA = a.extentRightMul;
            var extentLeftB = b.extentLeftMul;
            var extentRightB = b.extentRightMul;

            var extentMax = Math.Max(Math.Max(extentLeftA, extentRightA), Math.Max(extentLeftB, extentRightB));

            if (extentLeftA + extentRightA < 1 && a.dist >= 0)
            {
                length = Math.Min(length, b.dist);

                #if DEBUG
                DebugOutput($"Extend is less than 1 at {a.pos} for segment {task.segment.Id}");
                #endif
            }

            if (everFullyInBounds)
            {
                if (StopWhenOutOfBounds && !b.PossiblyInBounds(Vector2d.Zero, GridOuterSize))
                {
                    length = Math.Min(length, b.dist);

                    #if DEBUG
                    DebugOutput($"Trace frame at {b.pos} for segment {task.segment.Id} is now out of bounds");
                    #endif
                }
            }
            else if (!b.PossiblyOutOfBounds(Vector2d.Zero, GridOuterSize))
            {
                everFullyInBounds = true;
            }

            var boundP1 = a.pos + a.perpCCW * (extentLeftA + TraceOuterMargin);
            var boundP2 = a.pos + a.perpCW * (extentRightA + TraceOuterMargin);
            var boundP3 = b.pos + b.perpCCW * (extentLeftB + TraceOuterMargin);
            var boundP4 = b.pos + b.perpCW * (extentRightB + TraceOuterMargin);

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

                            if (shiftAbs <= extentMax + TraceInnerMargin)
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

                        var extent = shift < 0 ?
                            progress.Lerp(extentLeftA, extentLeftB) :
                            progress.Lerp(extentRightA, extentRightB);

                        var nowDist = shiftAbs - extent;

                        if (nowDist <= TraceOuterMargin)
                        {
                            var preDist = _distanceGrid[x, z];

                            if (nowDist < preDist)
                            {
                                _distanceGrid[x, z] = nowDist;
                            }

                            if (nowDist <= TraceInnerMargin)
                            {
                                var dist = a.dist + distDelta * progress;

                                var value = progress.Lerp(a.value, b.value);
                                var densityA = shift < 0 ? a.densityLeftMul : a.densityRightMul;
                                var densityB = shift < 0 ? b.densityLeftMul : b.densityRightMul;
                                var density = progress.Lerp(densityA, densityB);
                                var offset = progress.Lerp(a.offset, b.offset) + shift * density;

                                if (nowDist <= CollisionCheckMargin && dist >= 0 && dist <= length)
                                {
                                    if (_mainGrid[x, z] > 0)
                                    {
                                        var valueDiff = Math.Abs(value - _valueGrid[x, z]);
                                        var offsetDiff = Math.Abs(offset - _offsetGrid[x, z]);

                                        var valueThr = nowDist <= 0 ? CollisionMinValueDiff : CollisionMinValueDiffM;
                                        var offsetThr = nowDist <= 0 ? CollisionMinOffsetDiff : CollisionMinOffsetDiffM;

                                        var otherTask = _taskGrid[x, z];

                                        if (valueDiff >= valueThr || offsetDiff >= offsetThr)
                                        {
                                            if (dist >= CollisionMinParentDist || !task.segment.Parents.Contains(otherTask.segment))
                                            {
                                                #if DEBUG
                                                DebugOutput($"Collision {task.segment.Id} vs {otherTask.segment.Id} at {pos} dist {dist} with value diff {valueDiff:F2} and offset diff {offsetDiff:F2}");
                                                DebugLine(new TraceDebugLine(this, new(x, z), 1));
                                                #endif

                                                return new TraceResult(initialFrame, a, everFullyInBounds, new TraceCollision
                                                {
                                                    taskA = task,
                                                    taskB = otherTask,
                                                    framesA = ExchangeFrameBuffer(),
                                                    position = pos
                                                });
                                            }
                                        }

                                        #if DEBUG
                                        DebugOutput($"Ignoring collision {task.segment.Id} vs {otherTask.segment.Id} at {pos} with value diff {valueDiff:F2} and offset diff {offsetDiff:F2}");
                                        DebugLine(new TraceDebugLine(this, new(x, z), 4, 0, $"{dist:F2}"));
                                        #endif
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

                                    _taskGrid[x, z] = task;

                                    if (nowDist <= 0)
                                    {
                                        _mainGrid[x, z] = progress.Lerp(a.width, b.width);
                                        _sideGrid[x, z] = shift;

                                        #if DEBUG
                                        _debugGrid[x, z] = task.segment.Id;
                                        #endif

                                        if (nextSplit.relWidths != null)
                                        {
                                            var splitDistance = nextSplit.distance + (length - dist);
                                            if (splitDistance <= MainGridSmoothLength.WithMin(1) * nextSplit.baseWidth)
                                            {
                                                ApplySmoothBranching(x, z, shift, extent, splitDistance, nextSplit);
                                            }
                                        }

                                        if (lastMerge.relWidths != null)
                                        {
                                            var mergeDistance = lastMerge.distance + dist;
                                            if (mergeDistance <= MainGridSmoothLength.WithMin(1) * nextSplit.baseWidth)
                                            {
                                                ApplySmoothBranching(x, z, shift, extent, mergeDistance, lastMerge);
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

        return new TraceResult(initialFrame, a, everFullyInBounds);
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

            var coneLength = info.baseWidth * (relWidthA + relWidthB); // TODO tweak
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

    private ForkInfo FindNextSplit(TraceTask task, double maxDistance)
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
                    return default;
                }
            }
            else if (current.BranchCount > 1)
            {
                var relWidths = current.Branches.Select(s => s.RelWidth).ToArray();
                return new ForkInfo(distance, width, relWidths);
            }
        }

        return default;
    }

    private ForkInfo FindLastMerge(TraceTask task, double maxDistance)
    {
        if (task.branchParent.segment.ParentCount <= 1) return default;

        var baseWidth = task.branchParent.WidthAt(0);
        var distance = task.distFromRoot - task.branchParent.distFromRoot;

        if (distance > maxDistance || baseWidth <= 0) return default;

        var relWidths = task.branchParent.segment.Parents
            .Select(s => _traceResults[s])
            .Where(r => r != null)
            .Select(r => r.finalFrame.width / baseWidth)
            .ToArray();

        return new ForkInfo(distance, baseWidth, relWidths);
    }

    private readonly struct ForkInfo
    {
        public readonly double distance;
        public readonly double baseWidth;
        public readonly double[] relWidths;

        public ForkInfo(double distance, double baseWidth, double[] relWidths)
        {
            this.distance = distance;
            this.baseWidth = baseWidth;
            this.relWidths = relWidths;
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
