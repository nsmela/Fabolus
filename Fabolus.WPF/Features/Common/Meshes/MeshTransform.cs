using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.WPF.Features.Common.Meshes;

/// <summary>
/// Collection of transforms applied to the mesh
/// </summary>
public class MeshTransform {
    private List<Quaterniond> Rotations { get; set; } = [];
    public Quaterniond Rotation { get; private set; } = new();

    public void ApplyRotation(Quaterniond rotation) {
        Rotations.Add(rotation);

        //TODO: calculate total rotation
    }

    public void Clear() {
        Rotations.Clear();
        Rotation = new();
    }
}