using System;
using NodeEditorFramework;
using TerrainGraph.Flow;
using UnityEngine;

namespace TerrainGraph;

public class PathFunctionConnection : ValueConnectionType
{
    public const string Id = "PathFunc";

    public override string Identifier => Id;
    public override Color Color => new(1.2f, 1.2f, 1.2f);
    public override Type Type => typeof(ISupplier<Path>);
}
