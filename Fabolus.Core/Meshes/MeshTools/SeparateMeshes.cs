using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.MeshTools;

public static partial class MeshTools {
    public static MeshModel[] SeperateModels(MeshModel model) {
        if (model is null) { throw new ArgumentNullException(nameof(model)); }

        // get the connected components of the mesh
        DMesh3[] components = MeshConnectedComponents.Separate(model.Mesh);
        if (components.Length == 0) { return []; }

        // create a mesh model for each component
        MeshModel[] models = new MeshModel[components.Length];
        for (int i = 0; i < components.Length; i++) {
            models[i] = new MeshModel(components[i]);
        }

        return models;
    }
}
