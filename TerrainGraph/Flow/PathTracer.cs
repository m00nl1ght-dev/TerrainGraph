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
    public double SplitAngleLockLength = 5;
    public double CollisionMinValueDiff = 0.75;
    public double CollisionMinOffsetDiff = 0.5;
    public double CollisionCheckMargin = 0.5;
    public double CollisionMinValueDiffM = 5;
    public double CollisionMinOffsetDiffM = 5;

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
    private readonly Segment[,] _segmentGrid;

    #if DEBUG
    private readonly double[,] _debugGrid;
    #endif

    public IGridFunction<double> MainGrid => BuildGridFunction(_mainGrid);
    public IGridFunction<double> ValueGrid => BuildGridFunction(_valueGrid);
    public IGridFunction<double> OffsetGrid => BuildGridFunction(_offsetGrid);
    public IGridFunction<double> DistanceGrid => BuildGridFunction(_distanceGrid, TraceOuterMargin);

    #if DEBUG
    public IGridFunction<double> DebugGrid => BuildGridFunction(_debugGrid);
    #endif

    public readonly TraceCollisionHandler CollisionHandler;

    private readonly Dictionary<Segment, TraceResult> _traceResults = new();

    internal IReadOnlyDictionary<Segment, TraceResult> TraceResults => _traceResults;

    private readonly GridKernel _followGridKernel = GridKernel.Square(3, 3);
    private readonly GridKernel _avoidGridKernel = GridKernel.Shield(2, 5, 3);

    private readonly IGridFunction<double> _overlapAvoidanceGrid = Zero;

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
        _valueGrid = new double[outerSizeX, outerSizeZ];
        _offsetGrid = new double[outerSizeX, outerSizeZ];
        _distanceGrid = new double[outerSizeX, outerSizeZ];
        _segmentGrid = new Segment[outerSizeX, outerSizeZ];

        #if DEBUG
        _debugGrid = new double[outerSizeX, outerSizeZ];
        #endif

        CollisionHandler = new TraceCollisionHandler(this);

        if (TraceOuterMargin > 0)
        {
            _overlapAvoidanceGrid = new ScaleWithBias(
                new Cache<double>(_distanceGrid, TraceOuterMargin), -1 / TraceOuterMargin, 1
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
                _valueGrid[x, z] = 0;
                _offsetGrid[x, z] = 0;
                _distanceGrid[x, z] = TraceOuterMargin;
                _segmentGrid[x, z] = null;

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
                var branches = segment.Branches.ToList();
                var threshold = branches.Count / 2d - 0.5d;

                for (var i = 0; i < branches.Count; i++)
                {
                    var branch = branches[i];

                    var rangeBranch = branch.TraceParams.ArcStableRange;
                    branch.ApplyLocalStabilityAtTail(rangeBranch / 2, rangeBranch / 2);

                    if (i < threshold)
                    {
                        branch.AngleDeltaNegLockLength = SplitAngleLockLength;
                    }
                    else if (i > threshold)
                    {
                        branch.AngleDeltaPosLockLength = SplitAngleLockLength;
                    }
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
                Enqueue(rootSegment, originFrame, 0, false);
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
                    foreach (var branch in task.segment.Branches)
                    {
                        if (branch.ParentCount <= 1)
                        {
                            Enqueue(branch, result.finalFrame, task.distFromRoot + result.finalFrame.dist, result.everInBounds);
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

                            Enqueue(branch, mergedFrame, maxDistFromRoot, parentResults.Any(r => r.everInBounds));
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

        void Enqueue(Segment branch, TraceFrame baseFrame, double distFromRoot, bool everInBounds)
        {
            if (_traceResults.ContainsKey(branch) || taskQueue.Any(t => t.segment == branch)) return;

            var collisionList = simulatedCollisions?.Where(c => c.segmentB == branch).ToList();

            if (collisionList is { Count: 0 }) collisionList = null;

            var marginHead = branch.IsLeaf ? TraceInnerMargin : 0;
            var marginTail = branch.IsRoot ? TraceInnerMargin : 0;

            taskQueue.Enqueue(new TraceTask(
                branch, baseFrame, collisionList, marginHead, marginTail, distFromRoot, everInBounds
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

        var stepSize = task.segment.TraceParams.StepSize.WithMin(1);

        var initialFrame = new TraceFrame(task.baseFrame, task.segment, GridMargin, -task.marginTail);

        #if DEBUG
        DebugOutput($"Segment {task.segment.Id} with length {length:F2} started with initial frame [{initialFrame}] and params {task.segment.TraceParams}");
        #endif

        var everFullyInBounds = task.everInBounds || !initialFrame.PossiblyOutOfBounds(Vector2d.Zero, GridOuterSize);

        if (length <= 0) return new TraceResult(initialFrame, initialFrame, everFullyInBounds);

        List<PathFinder.Node> pathNodes = null;

        if (extParams.Target != null)
        {
            var target = extParams.Target.Value + GridMargin;

            var costGrid = Zero;

            if (extParams.CostGrid != null)
            {
                costGrid = new Transform<double>(extParams.CostGrid, GridMargin.x, GridMargin.z);
            }

            if (extParams.AvoidOverlap > 0)
            {
                costGrid = new Add(costGrid, new Multiply(Of(extParams.AvoidOverlap), _overlapAvoidanceGrid));
            }

            var angleLimit = (1d - extParams.AngleTenacity) * 180d / (initialFrame.width * Math.PI);

            var arcCount = angleLimit switch { < 2d => 2, < 5d => 3, _ => 4 };

            var pathFinder = new PathFinder(this, new PathFinder.ArcKernel(arcCount, stepSize * angleLimit, (int) stepSize))
            {
                Grid = costGrid,
                ObstacleThreshold = 100d,
                FullStepDistance = stepSize,
                QtClosedLoc = 0.5d * stepSize,
                QtOpenLoc = 0.5d * stepSize,
                AngleDeltaLimit = angleLimit,
                IterationLimit = 10000
            };

            if (extParams.SwerveFunc != null)
            {
                var baseDist = task.distFromRoot;
                var swerveFunc = extParams.SwerveFunc;
                pathFinder.DirectionBias = (d, c) => swerveFunc.ValueAt(baseDist + d, c);
            }

            for (int i = 0; i <= 3; i++)
            {
                pathFinder.HeuristicDistanceWeight = 1f + (float) Math.Pow(2, i);
                pathNodes = pathFinder.FindPath(initialFrame.pos, initialFrame.normal, target);
                if (pathNodes != null) break;
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

                    if (_frameBuffer.Count + 1 >= pathNodes.Count)
                    {
                        length = a.dist + distDelta;
                        pathNodes = null;
                    }

                    #if DEBUG
                    DebugOutput($"Angle delta {angleDelta:F2} over distance {distDelta:F2} for {node}");
                    DebugLine(new TraceDebugLine(this, node.Parent.Position, node.Position, 4));
                    #endif
                }
                else
                {
                    distDelta = Math.Min(stepSize, length + task.marginHead - a.dist);

                    var costAtFrame = 0d;
                    var followVec = Vector2d.Zero;

                    if (extParams.CostGrid != null)
                    {
                        followVec = _followGridKernel.CalculateAt(
                            new(1, 0), new(0, 1), extParams.CostGrid, a.pos - GridMargin, ref costAtFrame
                        );
                    }

                    if (extParams.AvoidOverlap > 0)
                    {
                        followVec += extParams.AvoidOverlap * _avoidGridKernel.CalculateAt(
                            a.normal, a.perpCW, _overlapAvoidanceGrid, a.pos, ref costAtFrame
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

                    if (extParams.SwerveFunc != null)
                    {
                        angleDelta += extParams.SwerveFunc.ValueAt(task.distFromRoot + a.dist, costAtFrame);
                    }

                    if (a.dist < task.segment.AngleDeltaPosLockLength && angleDelta > 0) angleDelta = 0;
                    if (a.dist < task.segment.AngleDeltaNegLockLength && angleDelta < 0) angleDelta = 0;

                    var widthForTenacity = extParams.StaticAngleTenacity ? initialFrame.width : a.width;
                    var maxAngleDelta = (1 - extParams.AngleTenacity) * 180 * distDelta / (widthForTenacity * Math.PI);
                    angleDelta = (distDelta * angleDelta).NormalizeDeg().InRange(-maxAngleDelta, maxAngleDelta);
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
                task.segment, distDelta, angleDelta,
                extraValue, extraOffset, GridMargin,
                out var pivotPoint, out var pivotOffset,
                Math.Abs(angleDelta) >= RadialThreshold
            );

            var extentLeftA = a.extentLeftMul;
            var extentRightA = a.extentRightMul;
            var extentLeftB = b.extentLeftMul;
            var extentRightB = b.extentRightMul;

            var extentMax = Math.Max(Math.Max(extentLeftA, extentRightA), Math.Max(extentLeftB, extentRightB));

            if (extentLeftA < 1 && extentRightA < 1 && a.dist >= 0)
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

                                        if (valueDiff >= valueThr || offsetDiff >= offsetThr)
                                        {
                                            #if DEBUG
                                            DebugOutput($"Collision {task.segment.Id} vs {_segmentGrid[x, z].Id} at {pos} with value diff {valueDiff} and offset diff {offsetDiff}");
                                            #endif

                                            return new TraceResult(initialFrame, a, everFullyInBounds, new TraceCollision
                                            {
                                                segmentA = task.segment,
                                                segmentB = _segmentGrid[x, z],
                                                framesA = ExchangeFrameBuffer(),
                                                position = pos
                                            });
                                        }

                                        #if DEBUG
                                        DebugOutput($"Ignoring collision {task.segment.Id} vs {_segmentGrid[x, z].Id} at {pos} with value diff {valueDiff} and offset diff {offsetDiff}");
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

                                    _segmentGrid[x, z] = task.segment;

                                    if (nowDist <= 0)
                                    {
                                        _mainGrid[x, z] = extent;

                                        #if DEBUG
                                        _debugGrid[x, z] = task.segment.Id;
                                        #endif
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

    /// <summary>
    /// Wrap the given raw grid array in a GridFunction, transforming values into map space.
    /// </summary>
    private IGridFunction<double> BuildGridFunction(double[,] grid, double fallback = 0d)
    {
        if (GridMargin == Vector2d.Zero) return new Cache<double>(grid);
        return new Transform<double>(new Cache<double>(grid, fallback), -GridMargin.x, -GridMargin.z);
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
        DebugLog?.Add(debug);
    }

    internal static void DebugLine(TraceDebugLine debugLine)
    {
        if (DebugLines != null && (DebugLines.Count < 50000 || debugLine.Color > 1))
        {
            DebugLines.Add(debugLine);
        }
    }

    #endif
}
