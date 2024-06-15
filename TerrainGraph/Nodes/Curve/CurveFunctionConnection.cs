using System;
using NodeEditorFramework;
using UnityEngine;

namespace TerrainGraph;

public class CurveFunctionConnection : ValueConnectionType
{
    public const string Id = "CurveFunc";

    public override string Identifier => Id;
    public override Color Color => new Color(0.51f, 0.51f, 1.25f);
    public override Type Type => typeof(ISupplier<ICurveFunction<double>>);
}
