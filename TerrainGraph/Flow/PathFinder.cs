using System;
using System.Collections.Generic;
using System.Linq;
using TerrainGraph.Util;

namespace TerrainGraph.Flow;

/// <summary>
/// Modified (non-discrete) version of the eLIAN limited-angle pathfinding
/// algorithm developed by Andreychuk et al.
/// https://arxiv.org/abs/1811.00797
/// </summary>
[HotSwappable]
public class PathFinder
{
    public GridValueSupplier<double> LocalPathCost = (_,_) => 0d;
    public GridValueSupplier<(double, double)> AngleDeltaLimits = (_,_) => (90d, 90d);
    public GridValueSupplier<double> DirectionBias = (_,_) => 0d;

    public double ObstacleThreshold = 1d;
    public double FullStepDistance = 1d;
    public double TargetAcceptRadius = 1d;
    public double PlanarTargetFallback = 0d;

    public float HeuristicDistanceWeight = 2;
    public float HeuristicCurvatureWeight = 0;

    public int IterationLimit = 20000;

    public double QtClosedLoc = 1d;
    public double QtClosedRot = 1d;
    public double QtOpenLoc = 1d;
    public double QtOpenRot = 1d;

    private readonly PathTracer _tracer;
    private readonly ArcKernel _kernel;

    private readonly HashSet<NodeKey> _closed = new(100);
    private readonly Dictionary<NodeKey, Node> _open = new(100);
    private readonly FastPriorityQueue<Node> _openQueue = new(100);

    public delegate T GridValueSupplier<out T>(Vector2d position, double distance);

    public PathFinder(PathTracer tracer, ArcKernel kernel)
    {
        _tracer = tracer;
        _kernel = kernel;
    }

    public List<Node> FindPath(Vector2d startPos, Vector2d startDirection, Vector2d targetPos)
    {
        _open.Clear();
        _closed.Clear();
        _openQueue.Clear();

        var targetRadiusSq = TargetAcceptRadius * TargetAcceptRadius;
        var mainDirection = (targetPos - startPos).Normalized;
        var totalDistance = Vector2d.Distance(startPos, targetPos);
        var startNode = new Node(startPos, startDirection, 0, 0);

        var planarP1 = targetPos + mainDirection * PlanarTargetFallback + mainDirection.PerpCCW * 10;
        var planarP2 = targetPos + mainDirection * PlanarTargetFallback + mainDirection.PerpCW * 10;

        #if DEBUG
        PathTracer.DebugOutput($"Attempting to find path to {targetPos} with hwDist {HeuristicDistanceWeight:F2}");
        #endif

        _openQueue.Enqueue(startNode, HeuristicDistanceWeight * (float) totalDistance);
        _open[new NodeKey(startNode, QtOpenLoc, QtOpenRot)] = startNode;

        var iterations = 0;

        while (_openQueue.Count > 0 && ++iterations <= IterationLimit)
        {
            var curNode = _openQueue.Dequeue();

            _closed.Add(new NodeKey(curNode, QtClosedLoc, QtClosedRot));

            if (Vector2d.DistanceSq(curNode.Position, targetPos) <= targetRadiusSq)
            {
                #if DEBUG
                var targetDistance = Vector2d.Distance(curNode.Position, targetPos);
                PathTracer.DebugOutput($"Found path after {iterations} iterations ending {targetDistance:F2} away from target");
                #endif

                return WeavePath(curNode);
            }

            if (PlanarTargetFallback > 0 && Vector2d.PointToLineOrientation(planarP1, planarP2, curNode.Position) > 0)
            {
                #if DEBUG
                var targetDistance = Vector2d.Distance(curNode.Position, targetPos);
                PathTracer.DebugOutput($"Accepted fallback path after {iterations} iterations ending {targetDistance:F2} away from target");
                #endif

                return WeavePath(curNode);
            }

            Expand(curNode, targetPos);
        }

        #if DEBUG
        var minDist = Math.Sqrt(_open.Values.Min(n => Vector2d.DistanceSq(n.Position, targetPos)));
        PathTracer.DebugOutput($"Failed to find path after {iterations} iterations, the closest node was {minDist:F2} from the target");
        #endif

        return null;
    }

    private bool Expand(Node curNode, Vector2d target)
    {
        var newNodes = 0;
        var obstructed = 0;

        var curTotalDistance = curNode.PathDepth * FullStepDistance;
        var newTotalDistance = curTotalDistance + FullStepDistance;

        var splitDistance = FullStepDistance / _kernel.SplitCount;
        var directionBias = DirectionBias(curNode.Position, curTotalDistance);
        var (angleLimitN, angleLimitP) = AngleDeltaLimits(curNode.Position, curTotalDistance);

        #if DEBUG
        PathTracer.DebugLine(new TraceDebugLine(_tracer, curNode.Position, 0, 0, $"{directionBias:F2} [ -{angleLimitN:F2} | {angleLimitP:F2} ]"));
        #endif

        for (int i = 0; i <= _kernel.ArcCount * 2; i++)
        {
            int dirIdxDelta = i % 2 == 1 ? i / 2 + 1 : -i / 2;
            int kernelArcIdx = Math.Abs(dirIdxDelta) - 1;

            var newPos = curNode.Position;
            var newDir = curNode.Direction;

            var totalCost = 0d;
            var angleDelta = 0d;
            var hitObstacle = false;

            if (kernelArcIdx >= 0)
            {
                angleDelta = _kernel.AngleData[kernelArcIdx] / splitDistance;

                if (angleDelta > (i % 2 == 1 ? angleLimitN : angleLimitP))
                {
                    if (angleDelta > (i % 2 == 1 ? angleLimitP : angleLimitN)) break;
                    continue;
                }

                var pivotOffset = 180d / (Math.PI * -angleDelta) * (i % 2 == 1 ? -1 : 1);
                var pivotPoint = curNode.Position + curNode.Direction.PerpCCW * pivotOffset;

                for (int s = _kernel.MaxSplitIdx; s >= 0; s--)
                {
                    var sin = _kernel.SinCosData[kernelArcIdx, s, 0];
                    var cos = _kernel.SinCosData[kernelArcIdx, s, 1];

                    var sDir = curNode.Direction.Rotate(i % 2 == 1 ? sin : -sin, cos);
                    var sPos = pivotPoint - sDir.PerpCCW * pivotOffset;
                    var sCost = splitDistance * (1 + LocalPathCost(sPos, newTotalDistance));

                    if (s == _kernel.MaxSplitIdx)
                    {
                        newPos = sPos;
                        newDir = sDir;
                    }

                    totalCost += sCost;

                    if (sCost >= ObstacleThreshold)
                    {
                        #if DEBUG
                        PathTracer.DebugLine(new TraceDebugLine(_tracer, sPos, 1, 0, $"{sCost}"));
                        #endif

                        hitObstacle = true;
                        break;
                    }
                }
            }
            else
            {
                for (int s = _kernel.MaxSplitIdx; s >= 0; s--)
                {
                    var sDist = FullStepDistance * _kernel.SplitFraction(s);
                    var sPos = curNode.Position + curNode.Direction * sDist;
                    var sCost = splitDistance * (1 + LocalPathCost(sPos, newTotalDistance));

                    if (s == _kernel.MaxSplitIdx)
                    {
                        newPos = sPos;
                    }

                    totalCost += sCost;

                    if (sCost >= ObstacleThreshold)
                    {
                        hitObstacle = true;
                        break;
                    }
                }
            }

            if (hitObstacle)
            {
                #if DEBUG
                PathTracer.DebugLine(new TraceDebugLine(_tracer, curNode.Position, newPos, 1));
                #endif

                obstructed++;
                continue;
            }

            var newDirIdx = curNode.DirectionIdx + dirIdxDelta;

            if (newDirIdx > _kernel.ArcsPerHalf) newDirIdx -= 2 * _kernel.ArcsPerHalf;
            else if (newDirIdx <= -_kernel.ArcsPerHalf) newDirIdx += 2 * _kernel.ArcsPerHalf;

            if (_closed.Contains(new NodeKey(newPos, newDirIdx, QtClosedLoc, QtClosedRot))) continue;

            var newNode = new Node(newPos, newDir, newDirIdx, curNode.PathDepth + 1, curNode)
            {
                TotalCost = curNode.TotalCost + (float) totalCost
            };

            var priority = newNode.TotalCost + HeuristicDistanceWeight * (float) Vector2d.Distance(newPos, target);

            if (kernelArcIdx >= 0)
            {
                priority += HeuristicCurvatureWeight * (float) angleDelta;
            }

            if (directionBias != 0)
            {
                priority += (float) (directionBias - angleDelta * (i % 2 == 1 ? -1 : 1)).Abs();
            }

            newNodes++;

            #if DEBUG
            PathTracer.DebugLine(new TraceDebugLine(_tracer, curNode.Position, newPos, directionBias != 0 ? 2 : 0));
            #endif

            var newKey = new NodeKey(newPos, newDirIdx, QtOpenLoc, QtOpenRot);

            if (_open.TryGetValue(newKey, out var existing))
            {
                if (existing.Priority <= priority) continue;
                _openQueue.Remove(existing);
            }

            _openQueue.Enqueue(newNode, priority);
            _open[newKey] = newNode;
        }

        var distToTarget = Vector2d.Distance(curNode.Position, target);

        if (distToTarget <= FullStepDistance)
        {
            var cordVec = target - curNode.Position;
            var cordMid = curNode.Position + cordVec * 0.5;
            var cordNrm = cordVec.Normalized.PerpCW;
            var nodeNrm = curNode.Direction.PerpCW;

            if (distToTarget > 0 && Vector2d.TryIntersect(curNode.Position, cordMid, nodeNrm, cordNrm, out var center, out var scalarA, 0.01))
            {
                var scalarB = Vector2d.PerpDot(nodeNrm, curNode.Position - cordMid) / Vector2d.PerpDot(nodeNrm, cordNrm);

                if (Math.Sign(scalarA) == Math.Sign(scalarB))
                {
                    var newDir = ((target - center).PerpCW * Math.Sign(scalarA)).Normalized;
                    var angleDelta = Vector2d.Angle(curNode.Direction, newDir) / distToTarget;

                    if (angleDelta <= (scalarA < 0 ? angleLimitN : angleLimitP))
                    {
                        var totalCost = 0d;
                        var hitObstacle = false;

                        #if DEBUG
                        PathTracer.DebugLine(new TraceDebugLine(_tracer, curNode.Position, target, 2));
                        #endif

                        for (double p = 0; p < distToTarget; p += splitDistance)
                        {
                            var sRad = (angleDelta * p * Math.Sign(scalarA)).ToRad();
                            var sDir = curNode.Direction.Rotate(Math.Sin(sRad), Math.Cos(sRad));
                            var sPos = center + sDir.PerpCCW * scalarA;
                            var sCost = splitDistance * (1 + LocalPathCost(sPos, newTotalDistance));

                            totalCost += sCost;

                            if (sCost >= ObstacleThreshold)
                            {
                                hitObstacle = true;
                                break;
                            }
                        }

                        if (!hitObstacle)
                        {
                            var newNode = new Node(target, newDir, curNode.DirectionIdx, curNode.PathDepth + 1, curNode)
                            {
                                TotalCost = curNode.TotalCost + (float) totalCost
                            };

                            var priority = newNode.TotalCost + HeuristicCurvatureWeight * (float) angleDelta;

                            _openQueue.Enqueue(newNode, priority);
                            newNodes++;
                        }
                    }
                }
            }
            else if (Vector2d.Dot(curNode.Direction, target - curNode.Position) >= 0)
            {
                var totalCost = 0d;
                var hitObstacle = false;

                #if DEBUG
                PathTracer.DebugLine(new TraceDebugLine(_tracer, curNode.Position, target, 2));
                #endif

                for (double p = 0; p < distToTarget; p += splitDistance)
                {
                    var sPos = curNode.Position + p * curNode.Direction;
                    var sCost = splitDistance * (1 + LocalPathCost(sPos, newTotalDistance));

                    totalCost += sCost;

                    if (sCost >= ObstacleThreshold)
                    {
                        hitObstacle = true;
                        break;
                    }
                }

                if (!hitObstacle)
                {
                    var newNode = new Node(target, curNode.Direction, curNode.DirectionIdx, curNode.PathDepth + 1, curNode)
                    {
                        TotalCost = curNode.TotalCost + (float) totalCost
                    };

                    var priority = newNode.TotalCost;

                    _openQueue.Enqueue(newNode, priority);
                    newNodes++;
                }
            }
        }

        return newNodes == 0 && obstructed > 0;
    }

    private List<Node> WeavePath(Node node)
    {
        var list = new List<Node>();

        while (node != null)
        {
            list.Add(node);
            node = node.Parent;
        }

        list.Reverse();

        return list;
    }

    public class ArcKernel
    {
        public readonly int ArcCount;
        public readonly int SplitCount;
        public readonly int ArcsPerHalf;

        public readonly double[] AngleData;
        public readonly double[,,] SinCosData;

        public int MaxSplitIdx => SplitCount - 1;

        public double SplitFraction(int splitIdx) => (splitIdx + 1d) / SplitCount;

        public ArcKernel(int arcCount, double arcMaxAngle, int splitCount)
        {
            ArcCount = arcCount;
            SplitCount = splitCount;

            AngleData = new double[ArcCount];
            SinCosData = new double[ArcCount, SplitCount, 2];

            var angleInterval = MathUtil.LargestDivisorLessThanOrEqual(180d, arcMaxAngle.WithMax(180d) / arcCount);

            ArcsPerHalf = (int) Math.Round(180d / angleInterval);

            #if DEBUG
            PathTracer.DebugOutput($"Creating kernel with {arcCount} arcs and max angle {arcMaxAngle:F2} -> {angleInterval:F2} x {ArcsPerHalf}");
            #endif

            for (int i = 0; i < ArcCount; i++)
            {
                var splitDeg = angleInterval / splitCount * (i + 1);
                var splitRad = splitDeg * (Math.PI / 180);

                AngleData[i] = splitDeg;

                for (int s = 0; s < SplitCount; s++)
                {
                    var arcRad = splitRad * (s + 1);

                    SinCosData[i, s, 0] = Math.Sin(arcRad);
                    SinCosData[i, s, 1] = Math.Cos(arcRad);
                }
            }
        }
    }

    public class Node : FastPriorityQueueNode
    {
        public readonly Node Parent;

        public readonly Vector2d Position;
        public readonly Vector2d Direction;

        public short DirectionIdx;
        public ushort PathDepth;
        public float TotalCost;

        public Node(Vector2d position, Vector2d direction, int directionIdx, int pathDepth, Node parent = null)
        {
            this.Position = position;
            this.Direction = direction;
            this.DirectionIdx = (short) directionIdx;
            this.PathDepth = (ushort) pathDepth;
            this.Parent = parent;
        }

        public override string ToString() =>
            $"{nameof(Position)}: {Position}, " +
            $"{nameof(Direction)}: {Direction}, " +
            $"{nameof(DirectionIdx)}: {DirectionIdx}, " +
            $"{nameof(PathDepth)}: {PathDepth}, " +
            $"{nameof(Parent)}: {(Parent == null ? "null" : Parent.Position)}, " +
            $"{nameof(Priority)}: {Priority:F2}, " +
            $"{nameof(TotalCost)}: {TotalCost:F2}";
    }

    public readonly struct NodeKey : IEquatable<NodeKey>
    {
        public readonly short x;
        public readonly short z;
        public readonly short d;

        public NodeKey(Node node, double qtLoc, double qtRot) :
            this(node.Position, node.DirectionIdx, qtLoc, qtRot) {}

        public NodeKey(Vector2d pos, int dirIdx, double qtLoc, double qtRot)
        {
            this.x = (short) Math.Floor(pos.x / qtLoc);
            this.z = (short) Math.Floor(pos.z / qtLoc);
            this.d = (short) (dirIdx / qtRot);
        }

        public bool Equals(NodeKey other) => x == other.x && z == other.z && d == other.d;

        public override bool Equals(object obj) => obj is NodeKey other && Equals(other);

        public override int GetHashCode() => unchecked((((x * 397) ^ z) * 397) ^ d);
    }
}
