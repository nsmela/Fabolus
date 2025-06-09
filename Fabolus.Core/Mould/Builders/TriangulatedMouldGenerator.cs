using Fabolus.Core.Meshes;
using Fabolus.Core.Meshes.MeshTools;
using g3;
using gs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Mould.Builders;

public sealed record TriangulatedMouldGenerator : MouldGenerator {
    public double MaxHeight { get; private set; } = 10.0;
    public double MinHeight { get; private set; } = 0.0;

    public static TriangulatedMouldGenerator New() => new();
    public TriangulatedMouldGenerator WithBottomOffset(double offset) => this with { OffsetBottom = offset };
    public TriangulatedMouldGenerator WithBolus(MeshModel bolus) => this with { BolusReference = bolus };
    public TriangulatedMouldGenerator WithOffsets(double offset) => this with { OffsetTop = offset, OffsetBottom = offset, OffsetXY = offset };
    public TriangulatedMouldGenerator WithContourResolution(double resolution) => this with { ContourResolution = resolution };
    public TriangulatedMouldGenerator WithToolMeshes(MeshModel[] toolMeshes) => this with { ToolMeshes = toolMeshes.Select(tm => tm.Mesh).ToArray() };
    public TriangulatedMouldGenerator WithTopOffset(double offset) => this with { OffsetTop = offset };
    public TriangulatedMouldGenerator WithXYOffsets(double offset) => this with { OffsetXY = offset };

    public override Result<MeshModel> Build() {
        throw new NotImplementedException();
    }

    public override Result<MeshModel> Preview() {
        if (BolusReference is null) { throw new NullReferenceException("Build: Bolus mesh is null"); }

        MaxHeight = BolusReference.CachedBounds.Max.z + OffsetTop;
        MinHeight = BolusReference.CachedBounds.Min.z - OffsetBottom;

        var contour = MeshTools.OutlineContour(BolusReference, OffsetXY);
        var mesh = MeshTools.TriangulateContour(contour, MinHeight);

        // TODO extrude mesh
        //sides
        DMesh3 sides = new();

        List<int> lowerLoop = new();
        List<int> upperLoop = new();
        foreach (var v in mesh.Vertices()) {
            //add vertex and record the index id
            lowerLoop.Add(sides.AppendVertex(new Vector3d(v.x, v.y, MinHeight)));
            upperLoop.Add(sides.AppendVertex(new Vector3d(v.x, v.y, MaxHeight)));
        }

        int n = contour.Count - 1;
        for (int i = 0; i < n; ++i) {
            var p0 = lowerLoop[i];
            var p1 = lowerLoop[i + 1];
            var p2 = upperLoop[i];
            var p3 = upperLoop[i + 1];

            sides.AppendTriangle(p0, p1, p2);
            sides.AppendTriangle(p1, p3, p2);
        }

        MeshEditor editor = new(sides);
        //editor.AppendMesh(sides);

        MeshAutoRepair repair = new(editor.Mesh);
        repair.Apply();

        // return the mesh
        return Result<MeshModel>.Pass(new MeshModel(repair.Mesh));
    }
}

