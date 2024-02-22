using System.Collections.Generic;
using NodeEditorFramework;
using UnityEngine;

// ReSharper disable ForCanBeConvertedToForeach

namespace TerrainGraph;

public class TerrainCanvasTraversal : NodeCanvasTraversal
{
    public TerrainCanvas TerrainCanvas => (TerrainCanvas) nodeCanvas;

    // A list of Nodes from which calculation originates -> Call StartCalculation
    public List<Node> workList;

    public TerrainCanvasTraversal(TerrainCanvas canvas) : base(canvas) { }

    /// <summary>
    /// Recalculate from every node regarded as an input node
    /// </summary>
    public override void TraverseAll()
    {
        workList = [];
        for (int i = 0; i < nodeCanvas.nodes.Count; i++)
        {
            var node = nodeCanvas.nodes[i];
            if (node.isInput())
            {
                // Add all Inputs
                node.ClearCalculation();
                workList.Add(node);
            }
        }

        StartCalculation(TerrainCanvas.HasActiveGUI);
    }

    /// <summary>
    /// Recalculate from the specified node
    /// </summary>
    public override void OnChange(Node node)
    {
        node.ClearCalculation();
        workList = [node];
        StartCalculation(TerrainCanvas.HasActiveGUI);
    }

    /// <summary>
    /// Iteratively calculates all nodes from the worklist, including child nodes, until no further calculation is possible
    /// </summary>
    private void StartCalculation(bool refreshPreviews)
    {
        if (workList == null || workList.Count == 0)
            return;

        bool limitReached = false;
        while (!limitReached)
        {
            // Runs until the whole workList is calculated thoroughly or no further calculation is possible
            limitReached = true;
            for (int workCnt = 0; workCnt < workList.Count; workCnt++)
            {
                // Iteratively check workList
                if (ContinueCalculation(workList[workCnt], refreshPreviews))
                    limitReached = false;
            }
        }

        if (workList.Count > 0)
        {
            Debug.LogError("Did not complete calculation! " + workList.Count +
                           " nodes block calculation from advancing!");
            foreach (var node in workList)
                Debug.LogError("" + node.name + " blocks calculation!");
        }
    }

    /// <summary>
    /// Recursively calculates this node and it's children
    /// All nodes that could not be calculated in the current state are added to the workList for later calculation
    /// Returns whether calculation could advance at all
    /// </summary>
    private bool ContinueCalculation(Node node, bool refreshPreviews)
    {
        if (node.calculated)
        {
            // Already calculated
            workList.Remove(node);
            return true;
        }

        if (node.ancestorsCalculated() && node.Calculate())
        {
            // Calculation was successful
            node.calculated = true;
            workList.Remove(node);
            if (refreshPreviews) ((NodeBase) node).RefreshPreview();
            if (node.ContinueCalculation)
            {
                // Continue with children
                for (int i = 0; i < node.outputPorts.Count; i++)
                {
                    var outPort = node.outputPorts[i];
                    for (int t = 0; t < outPort.connections.Count; t++)
                        ContinueCalculation(outPort.connections[t].body, refreshPreviews);
                }
            }

            return true;
        }

        if (!workList.Contains(node))
        {
            // Calculation failed, record to calculate later on
            workList.Add(node);
        }

        return false;
    }
}
