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

    public double Volume => Mesh.Volume;

    #endregion

    #region Constructors
    public Bolus() {
        Mesh = new MeshModel();
    }

    public Bolus(MeshModel mesh) {
        Mesh = new MeshModel(OrientationCentre(mesh.Mesh));
    }

    public Bolus(DMesh3 mesh) {
        Mesh = new MeshModel(OrientationCentre(mesh));
    }


    #endregion


    private static DMesh3 OrientationCentre(DMesh3 mesh) {
        double x = mesh.CachedBounds.Center.x * -1;
        double y = mesh.CachedBounds.Center.y * -1;
        double z = mesh.CachedBounds.Center.z * -1;
        MeshTransforms.Translate(mesh, x, y, z);
        return mesh;
    }

    private static MeshModel OrientationCentre(MeshModel model) {
        double x = model._mesh.BoundingBox.Center().X * -1;
        double y = model._mesh.BoundingBox.Center().Y * -1;
        double z = model._mesh.BoundingBox.Center().Z * -1;
        MeshTransforms.Translate(model, x, y, z);
        return model;
    }
}
