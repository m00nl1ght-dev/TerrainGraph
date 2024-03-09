using System;
using System.Collections.Generic;
using System.Linq;
using TerrainGraph.Util;
using UnityEngine;

namespace TerrainGraph.Flow;

/// <summary>
/// Implementation of the eLIAN limited-angle pathfinding algorithm developed by Andreychuk et al.
/// https://arxiv.org/abs/1811.00797
/// </summary>
[HotSwappable]
public class PathFinder
{
    public IGridFunction<double> Grid;

    public double ObstacleThreshold = 1d;
    public double AngleDeltaLimit = 10d;

    public float HeuristicCostWeight = 2;
    public float HeuristicCurvatureWeight = 0;

    public int StepsUntilKernelRollback = 2;
    public int ClosedNodeMaxLimit = 10000;

    public readonly IReadOnlyList<GridKernel> Kernels;

    private readonly float _maxKernelSize;

    private readonly HashSet<NodeKey> _closed = new(100);
    private readonly FastPriorityQueue<Node> _open = new(100);

    public PathFinder(IReadOnlyList<GridKernel> kernels)
    {
        Kernels = kernels;
        _maxKernelSize = (float) kernels.Max(k => k.Size * k.Extend);
    }

    public List<Node> FindPath(Vector2Int startPos, double startAngle, Vector2Int targetPos)
    {
        _open.Clear();
        _closed.Clear();

        _open.Enqueue(new Node(startPos, startAngle), HeuristicCostWeight * Vector2Int.Distance(startPos, targetPos));

        while (_open.Count > 0 && _closed.Count < ClosedNodeMaxLimit)
        {
            var curNode = _open.Dequeue();
            _closed.Add(new NodeKey(curNode));

            if (curNode.Position == targetPos)
            {
                return WeavePath(curNode);
            }

            while (!Expand(curNode, targetPos) && curNode.Kernel + 1 < Kernels.Count)
            {
                curNode.Kernel++;
            }
        }

        return null;
    }

    private bool Expand(Node curNode, Vector2Int target)
    {
        var anyNewNodes = false;

        var kernel = Kernels[curNode.Kernel];

        for (int i = 0; i < kernel.PointCount; i++)
        {
            var angleDelta = MathUtil.AngleDeltaAbs(curNode.Angle, kernel.Angles[i]);
            if (angleDelta > AngleDeltaLimit * kernel.Distances[i]) continue;

            var offset = kernel.Offsets[i];

            var newX = curNode.Position.x + offset.x;
            var newY = curNode.Position.y + offset.z;

            if (Grid.ValueAt(newX, newY) >= ObstacleThreshold) continue;

            var newPos = new Vector2Int((int) newX, (int) newY);

            if (!CheckLineOfSight(curNode.Position, newPos)) continue;
            if (_closed.Contains(new NodeKey(curNode.Position, newPos))) continue;

            var newNode = new Node(newPos, kernel.Angles[i], curNode)
            {
                TotalCost = curNode.TotalCost + CalculateCost(curNode.Position, newPos)
            };

            var priority = newNode.TotalCost + HeuristicCostWeight * Vector2Int.Distance(newPos, target);

            if (curNode.Parent != null)
            {
                priority += HeuristicCurvatureWeight * _maxKernelSize * (float) angleDelta;
            }

            if (newNode.Kernel > 0)
            {
                var steps = 1;
                var node = curNode.Parent;

                while (steps < StepsUntilKernelRollback && node != null && node.Kernel == newNode.Kernel)
                {
                    node = node.Parent;
                    steps++;
                }

                if (steps == StepsUntilKernelRollback)
                {
                    newNode.Kernel--;
                }
            }

            _open.Enqueue(newNode, priority);
            anyNewNodes = true;
        }

        var distToTarget = Vector2Int.Distance(curNode.Position, target);

        if (distToTarget <= _maxKernelSize)
        {
            var angle = -Vector2d.SignedAngle(Vector2d.AxisX, target - curNode.Position);
            var angleDelta = MathUtil.AngleDeltaAbs(curNode.Angle, angle);

            if (angleDelta <= AngleDeltaLimit * distToTarget)
            {
                if (CheckLineOfSight(curNode.Position, target))
                {
                    if (!_closed.Contains(new NodeKey(curNode.Position, target)))
                    {
                        var newNode = new Node(target, angle, curNode)
                        {
                            TotalCost = curNode.TotalCost + CalculateCost(curNode.Position, target)
                        };

                        var priority = newNode.TotalCost;

                        if (curNode.Parent != null)
                        {
                            priority += HeuristicCurvatureWeight * _maxKernelSize * (float) angleDelta;
                        }

                        _open.Enqueue(newNode, priority);
                        anyNewNodes = true;
                    }
                }

            }
        }

        return anyNewNodes;
    }

    private float CalculateCost(Vector2Int from, Vector2Int to)
    {
        return Vector2Int.Distance(from, to);
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

    private bool CheckLineOfSight(Vector2Int a, Vector2Int b)
    {
        int x1 = a.x;
        int x2 = b.x;
        int y1 = a.y;
        int y2 = b.y;

        int dx, dy;

        int step = 0;
        int rot = 0;

        if (x1 > x2 && y1 > y2)
        {
            (x1, x2) = (x2, x1);
            (y1, y2) = (y2, y1);

            dx = x2 - x1;
            dy = y2 - y1;
        }
        else
        {
            dx = x2 - x1;
            dy = y2 - y1;

            if (dx >= 0 && dy >= 0)
            {
                rot = 2;
            }
            else if (dy < 0)
            {
                (y1, y2) = (y2, y1);
                dy = -dy;
                rot = 1;
            }
            else if (dx < 0)
            {
                (x1, x2) = (x2, x1);
                dx = -dx;
                rot = 3;
            }
        }

        if (rot == 1)
        {
            if (dx >= dy)
            {
                for (int x = x1; x <= x2; ++x)
                {
                    if (Grid.ValueAt(x, y2) >= ObstacleThreshold) return false;

                    step += dy;

                    if (step >= dx)
                    {
                        step -= dx;
                        y2--;
                    }
                }
            }
            else
            {
                for (int y = y1; y <= y2; ++y)
                {
                    if (Grid.ValueAt(x2, y) >= ObstacleThreshold) return false;

                    step += dx;

                    if (step >= dy)
                    {
                        step -= dy;
                        x2--;
                    }
                }
            }

            return true;
        }

        if (rot == 2)
        {
            if (dx >= dy)
            {
                for (int x = x1; x <= x2; ++x)
                {
                    if (Grid.ValueAt(x, y1) >= ObstacleThreshold) return false;

                    step += dy;

                    if (step >= dx)
                    {
                        step -= dx;
                        y1++;
                    }
                }

                return true;
            }

            for (int y = y1; y <= y2; ++y)
            {
                if (Grid.ValueAt(x1, y) >= ObstacleThreshold) return false;

                step += dx;

                if (step >= dy)
                {
                    step -= dy;
                    x1++;
                }
            }

            return true;
        }

        if (rot == 3)
        {
            if (dx >= dy)
            {
                for (int x = x1; x <= x2; ++x)
                {
                    if (Grid.ValueAt(x, y2) >= ObstacleThreshold) return false;

                    step += dy;

                    if (step >= dx)
                    {
                        step -= dx;
                        y2--;
                    }
                }
            }
            else
            {
                for (int y = y1; y <= y2; ++y)
                {
                    if (Grid.ValueAt(x2, y) >= ObstacleThreshold) return false;

                    step += dx;

                    if (step >= dy)
                    {
                        step -= dy;
                        x2--;
                    }
                }
            }

            return true;
        }

        if (dx >= dy)
        {
            for (int x = x1; x <= x2; ++x)
            {
                if (Grid.ValueAt(x, y1) >= ObstacleThreshold) return false;

                step += dy;

                if (step >= dx)
                {
                    step -= dx;
                    y1++;
                }
            }
        }
        else
        {
            for (int y = y1; y <= y2; ++y)
            {
                if (Grid.ValueAt(x1, y) >= ObstacleThreshold) return false;

                step += dx;

                if (step >= dy)
                {
                    step -= dy;
                    x1++;
                }
            }
        }

        return true;
    }

    public class Node : FastPriorityQueueNode
    {
        public readonly Node Parent;
        public readonly Vector2Int Position;
        public readonly double Angle;

        public int Kernel;
        public float TotalCost;

        public Node(Vector2Int position, double angle, Node parent = null)
        {
            this.Position = position;
            this.Angle = angle;
            this.Parent = parent;
            this.Kernel = parent?.Kernel ?? 0;
        }

        public override string ToString() =>
            $"{nameof(Position)}: {Position}, " +
            $"{nameof(Parent)}: {(Parent == null ? "null" : Parent.Position)}, " +
            $"{nameof(Angle)}: {Angle:F2}, " +
            $"{nameof(Kernel)}: {Kernel}, " +
            $"{nameof(Priority)}: {Priority:F2}, " +
            $"{nameof(TotalCost)}: {TotalCost:F2}";
    }

    public readonly struct NodeKey : IEquatable<NodeKey>
    {
        public readonly int nx;
        public readonly int ny;
        public readonly int px;
        public readonly int py;

        public NodeKey(Vector2Int parent, Vector2Int current)
        {
            this.px = parent.x;
            this.py = parent.y;
            this.nx = current.x;
            this.ny = current.y;
        }

        public NodeKey(Node node) : this(node.Parent?.Position ?? node.Position, node.Position) {}

        public bool Equals(NodeKey other)
        {
            return nx == other.nx && ny == other.ny && px == other.px && py == other.py;
        }

        public override bool Equals(object obj)
        {
            return obj is NodeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = nx;
                hashCode = (hashCode * 397) ^ ny;
                hashCode = (hashCode * 397) ^ px;
                hashCode = (hashCode * 397) ^ py;
                return hashCode;
            }
        }
    }
}
