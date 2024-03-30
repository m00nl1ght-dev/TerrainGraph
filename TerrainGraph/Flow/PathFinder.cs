using System;
using System.Collections.Generic;
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
    public IGridFunction<double> Grid;

    public double ObstacleThreshold = 1d;
    public double AngleDeltaLimit = 10d;

    public double FullStepDistance = 1d;

    public float HeuristicCostWeight = 2;
    public float HeuristicCurvatureWeight = 0;

    public int StepsUntilKernelRollback = 2;
    public int ClosedNodeMaxLimit = 10000;

    private readonly ArcKernel _kernel;

    private readonly HashSet<NodeKey> _closed = new(100);
    private readonly FastPriorityQueue<Node> _open = new(100);

    public PathFinder(ArcKernel kernel)
    {
        _kernel = kernel;
    }

    public List<Node> FindPath(Vector2d startPos, Vector2d startDirection, Vector2d targetPos)
    {
        _open.Clear();
        _closed.Clear();

        var totalDistance = Vector2d.Distance(startPos, targetPos);

        _open.Enqueue(new Node(startPos, startDirection, 0, _kernel.MaxSplitIdx), HeuristicCostWeight * (float) totalDistance);

        while (_open.Count > 0 && _closed.Count < ClosedNodeMaxLimit)
        {
            var curNode = _open.Dequeue();
            _closed.Add(new NodeKey(curNode));

            if (curNode.Position == targetPos)
            {
                return WeavePath(curNode);
            }

            while (!Expand(curNode, targetPos) && curNode.KernelSplit > 0)
            {
                curNode.KernelSplit--;
            }
        }

        return null;
    }

    private bool Expand(Node curNode, Vector2d target)
    {
        var anyNewNodes = false;

        var splitDistance = FullStepDistance / _kernel.SplitCount;
        var possibleDirCount = _kernel.ArcCount * _kernel.SplitCount;

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
                if (angleDelta > AngleDeltaLimit) break;

                var sin = _kernel.SinCosData[curNode.KernelSplit, kernelArcIdx, 0];
                var cos = _kernel.SinCosData[curNode.KernelSplit, kernelArcIdx, 1];

                var pivotOffset = 180d / (Math.PI * -angleDelta);
                var pivotPoint = curNode.Position + curNode.Direction.PerpCCW * pivotOffset;

                for (int s = curNode.KernelSplit; s >= 0; s--)
                {
                    var sDir = curNode.Direction.Rotate(i % 2 == 1 ? sin : -sin, cos);
                    var sPos = pivotPoint - sDir.PerpCCW * pivotOffset;
                    var sCost = Grid.ValueAt(sPos.x, sPos.z);

                    if (s == curNode.KernelSplit)
                    {
                        newPos = sPos;
                        newDir = sDir;
                    }

                    totalCost += sCost;

                    if (sCost >= ObstacleThreshold)
                    {
                        hitObstacle = true;
                        break;
                    }
                }
            }
            else
            {
                for (int s = curNode.KernelSplit; s >= 0; s--)
                {
                    var sDist = FullStepDistance * _kernel.SplitFraction(s);
                    var sPos = curNode.Position + curNode.Direction * sDist;
                    var sCost = Grid.ValueAt(sPos.x, sPos.z);

                    if (s == curNode.KernelSplit)
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

            if (hitObstacle) continue;

            var newDirIdx = curNode.DirectionIdx + dirIdxDelta * (curNode.KernelSplit + 1);

            if (newDirIdx > possibleDirCount) newDirIdx -= 2 * possibleDirCount;
            else if (newDirIdx <= -possibleDirCount) newDirIdx += 2 * possibleDirCount;

            if (_closed.Contains(new NodeKey(newPos, newDirIdx))) continue;

            var newNode = new Node(newPos, newDir, newDirIdx, curNode.KernelSplit, curNode)
            {
                TotalCost = curNode.TotalCost + (float) totalCost
            };

            var priority = newNode.TotalCost + HeuristicCostWeight * (float) Vector2d.Distance(newPos, target);

            if (kernelArcIdx >= 0)
            {
                priority += HeuristicCurvatureWeight * (float) angleDelta;
            }

            if (newNode.KernelSplit < _kernel.MaxSplitIdx)
            {
                var steps = 1;
                var node = curNode.Parent;

                while (steps < StepsUntilKernelRollback && node != null && node.KernelSplit == newNode.KernelSplit)
                {
                    node = node.Parent;
                    steps++;
                }

                if (steps == StepsUntilKernelRollback)
                {
                    newNode.KernelSplit++;
                }
            }

            _open.Enqueue(newNode, priority);
            anyNewNodes = true;
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

                    if (angleDelta <= AngleDeltaLimit)
                    {
                        var totalCost = 0d;
                        var hitObstacle = false;

                        for (double p = 0; p < distToTarget; p += splitDistance)
                        {
                            var sRad = (angleDelta * p * Math.Sign(scalarA)).ToRad();
                            var sDir = curNode.Direction.Rotate(Math.Sin(sRad), Math.Cos(sRad));
                            var sPos = center + sDir.PerpCCW * scalarA;
                            var sCost = Grid.ValueAt(sPos.x, sPos.z);

                            totalCost += sCost;

                            if (sCost >= ObstacleThreshold)
                            {
                                hitObstacle = true;
                                break;
                            }
                        }

                        if (!hitObstacle)
                        {
                            var newNode = new Node(target, newDir, curNode.DirectionIdx, curNode.KernelSplit, curNode)
                            {
                                TotalCost = curNode.TotalCost + (float) totalCost
                            };

                            var priority = newNode.TotalCost + HeuristicCurvatureWeight * (float) angleDelta;

                            _open.Enqueue(newNode, priority);
                            anyNewNodes = true;
                        }
                    }
                }
            }
            else if (Vector2d.Dot(curNode.Direction, target - curNode.Position) >= 0)
            {
                var totalCost = 0d;
                var hitObstacle = false;

                for (double p = 0; p < distToTarget; p += splitDistance)
                {
                    var sPos = curNode.Position + p * curNode.Direction;
                    var sCost = Grid.ValueAt(sPos.x, sPos.z);

                    totalCost += sCost;

                    if (sCost >= ObstacleThreshold)
                    {
                        hitObstacle = true;
                        break;
                    }
                }

                if (!hitObstacle)
                {
                    var newNode = new Node(target, curNode.Direction, curNode.DirectionIdx, curNode.KernelSplit, curNode)
                    {
                        TotalCost = curNode.TotalCost + (float) totalCost
                    };

                    var priority = newNode.TotalCost;

                    _open.Enqueue(newNode, priority);
                    anyNewNodes = true;
                }
            }
        }

        return anyNewNodes;
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

        public readonly double[] AngleData;
        public readonly double[,,] SinCosData;

        public int MaxSplitIdx => SplitCount - 1;

        public double SplitFraction(int splitIdx) => (splitIdx + 1d) / SplitCount;

        public ArcKernel(int arcCount, int splitCount)
        {
            ArcCount = arcCount;
            SplitCount = splitCount;

            AngleData = new double[ArcCount];
            SinCosData = new double[ArcCount, SplitCount, 2];

            var angleInterval = 180d / (SplitCount * ArcCount);

            for (int i = 0; i < ArcCount; i++)
            {
                var splitDeg = angleInterval * (i + 1);
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

        public int DirectionIdx;
        public int KernelSplit;

        public float TotalCost;

        public Node(Vector2d position, Vector2d direction, int directionIdx, int kernelSplit, Node parent = null)
        {
            this.Position = position;
            this.Direction = direction;
            this.DirectionIdx = directionIdx;
            this.KernelSplit = kernelSplit;
            this.Parent = parent;
        }

        public override string ToString() =>
            $"{nameof(Position)}: {Position}, " +
            $"{nameof(Direction)}: {Direction}, " +
            $"{nameof(DirectionIdx)}: {DirectionIdx}, " +
            $"{nameof(KernelSplit)}: {KernelSplit}, " +
            $"{nameof(Parent)}: {(Parent == null ? "null" : Parent.Position)}, " +
            $"{nameof(Priority)}: {Priority:F2}, " +
            $"{nameof(TotalCost)}: {TotalCost:F2}";
    }

    public readonly struct NodeKey : IEquatable<NodeKey>
    {
        public readonly int x;
        public readonly int z;
        public readonly int d;

        public NodeKey(Node node) : this(node.Position, node.DirectionIdx) {}

        public NodeKey(Vector2d pos, int directionIdx)
        {
            this.x = (int) Math.Round(pos.x);
            this.z = (int) Math.Round(pos.z);
            this.d = directionIdx;
        }

        public bool Equals(NodeKey other) => x == other.x && z == other.z && d == other.d;

        public override bool Equals(object obj) => obj is NodeKey other && Equals(other);

        public override int GetHashCode() => unchecked((((x * 397) ^ z) * 397) ^ d);
    }
}
