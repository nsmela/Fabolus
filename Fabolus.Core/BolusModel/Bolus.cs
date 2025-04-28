using Fabolus.Core.Meshes;
using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.BolusModel;
public class Bolus {

    #region Public Properties and Fields

    public MeshModel Mesh { get; set; }
    public double XOffset { get; protected set; }
    public double YOffset { get; protected set; }
    public double ZOffset { get; protected set; }

    public double Volume => Mesh.Volume;

    #endregion

    #region Constructors
    public Bolus() {
        Mesh = new MeshModel();
        SetOffsets();
    }

    public Bolus(MeshModel mesh) {
        Mesh = new MeshModel(OrientationCentre(mesh.Mesh));
        SetOffsets();
    }

    public Bolus(DMesh3 mesh) {
        Mesh = new MeshModel(OrientationCentre(mesh));
        SetOffsets();
    }


    #endregion

    #region Public Methods

    public void CopyOffsets(Bolus bolus) {
        XOffset = bolus.XOffset;
        YOffset = bolus.YOffset;
        ZOffset = bolus.ZOffset;
    }

    #endregion

    #region Private Methods

    protected void SetOffsets() {
        if (Mesh.IsEmpty()) {
            XOffset = 0.0; 
            YOffset = 0.0; 
            ZOffset = 0.0;
            return;
        }

        var offsets = Mesh.Offsets;
        XOffset = offsets[0];
        YOffset = offsets[1];
        ZOffset = offsets[2];

    }

    private static DMesh3 OrientationCentre(DMesh3 mesh) {
        double x = mesh.CachedBounds.Center.x * -1;
        double y = mesh.CachedBounds.Center.y * -1;
        double z = mesh.CachedBounds.Center.z * -1;
        MeshTransforms.Translate(mesh, x, y, z);
        return mesh;
    }
    #endregion

}
