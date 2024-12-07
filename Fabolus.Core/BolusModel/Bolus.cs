using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.BolusModel;
public class Bolus {
    #region Public Properties and Fields
    public DMesh3 Mesh { get; set; }
    public double XOffset { get; protected set; }
    public double YOffset { get; protected set; }
    public double ZOffset { get; protected set; }

    public double Volume {
        get {
            if (Mesh is null || Mesh.VertexCount == 0) { return 0.0; }

            var volumeAndArea = MeshMeasurements.VolumeArea(Mesh, Mesh.TriangleIndices(), Mesh.GetVertex);
            return volumeAndArea.x / 1000;
        }
    }

    #endregion

    #region Constructors
    public Bolus() {
        Mesh = new DMesh3();
        SetOffsets();
    }

    public Bolus(DMesh3 mesh) {
        Mesh = mesh;
        SetOffsets();
    }

    #endregion

    #region Private Methods

    protected void SetOffsets() {
        if (Mesh is null || Mesh.VertexCount == 0) {
            XOffset = 0.0; 
            YOffset = 0.0; 
            ZOffset = 0.0;
            return;
        }

        XOffset = Mesh.CachedBounds.Center.x;
        YOffset = Mesh.CachedBounds.Center.y;
        ZOffset = Mesh.CachedBounds.Center.z;

    }

    #endregion

}
