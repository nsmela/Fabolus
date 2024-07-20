using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.WPF.Features.Common.Meshes;

/// <summary>
/// Contains all Meshes, provides methods to covnert or select the most relevant Mesh
/// </summary>
public class MeshCollection {
    public Dictionary<MeshTypes, DMesh3> Meshes { get; set; } = [];

    /// <summary>
    /// Returns the latest mesh applied
    /// </summary>
    public DMesh3 CurrentMesh {
        get {
            if (Meshes.Count == 0) { return new DMesh3(); }

            var key = Meshes.Keys.Max();
            return Meshes[key];
        }
    }

    /// <summary>
    /// Applies a mesh to the collection and clears all meshes high than it
    /// </summary>
    /// <param name="type"></param>
    /// <param name="mesh"></param>
    public void ApplyMesh(MeshTypes type,  DMesh3 mesh) {
        Meshes.Add(type, mesh);
        Update(type);
    }

    public void RemoveMesh(MeshTypes type) {
        Meshes.Remove(type);
        Update(type);
    }

    private void Update(MeshTypes type) {
        //get the largest enum and convert all values to int
        var max = (int)Meshes.Keys.Max();
        var nextType = (int)type + 1; //next type 

        if (max <= nextType) { return; } //added type is the latest mesh

        for (int i = nextType; i < max; i++){
            Meshes.Remove((MeshTypes)i);
        }
    }
}