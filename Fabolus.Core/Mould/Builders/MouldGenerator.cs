using Fabolus.Core.Meshes;
using g3;

namespace Fabolus.Core.Mould.Builders;

/// <summary>
/// The inherited class for all mould generators
/// </summary>
public abstract record MouldGenerator {
    protected DMesh3 BolusReference { get; set; } //mesh to invert while entirely within
    public double OffsetXY { get; protected set; } = 4.0;
    public double OffsetTop { get; protected set; } = 3.0;
    public double OffsetBottom { get; protected set; } = 3.0;
    public int CalculationResolution { get; protected set; } = 32; //how accurate the implicit meshs are (higher is better, but slower)
    public double ContourResolution { get; protected set; } = 2.0; //xy contour detection grid size (lower is better, but slower)
    protected DMesh3[] ToolMeshes { get; set; } = []; // mesh to boolean subtract from the mold

    public abstract Result<MeshModel> Build();
    public abstract Result<MeshModel> Preview();

}
