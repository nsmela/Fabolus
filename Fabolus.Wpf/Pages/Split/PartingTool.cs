using Fabolus.Core.Meshes;
using Fabolus.Core.Meshes.PartingTools;
using g3;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;

// aliases
using MeshGeometry3D = HelixToolkit.Wpf.SharpDX.MeshGeometry3D;

namespace Fabolus.Wpf.Pages.Split;

internal class PartingTool {
    // User-modifiable
    public List<int> AnchorIndexes { get; private set; } = [];
    public Vector3[] AnchorPoints => 
        Model
        .GetVertices(AnchorIndexes.ToArray())
        .Select(v => new Vector3((float)v[0], (float)v[1], (float)v[2]))
        .ToArray();

    // generated between anchor points
    private int[] PathIndexes { get; set; } = [];
    public Vector3[] PathPoints { get; private set; } = [];

    // to show
    public MeshGeometry3D Geometry { get; private set; } = new();

    // reference mesh
    public MeshModel Model { get; init; }

    public PartingTool(MeshModel model, IEnumerable<int> anchors) =>
        (Model, AnchorIndexes) = (model, anchors.ToList());
    
    // calculate pathing between points
    public void Compute() {
        // reset
        PathIndexes = [];
        PathPoints = [];
        Geometry = new();

        // insufficient anchors to use for pathing
        if (AnchorIndexes.Count < 3) { return; }

        // grab the path
        PathIndexes = PartingTools.GeneratePartingLine(Model, AnchorIndexes.ToArray());
        PathPoints = Model
            .GetVertices(PathIndexes)
            .Select( v => new Vector3((float)v[0], (float)v[1], (float)v[2]))
            .ToArray();


        // use path to mesh
        MeshBuilder builder = new();
        int count = PathPoints.Length;
        double diameter = 0.5;
        double radius = 0.25;
        int segments = 32;
        for(int i = 0; i < count; i++) {
            var v0 = PathPoints[i];
            var v1 = PathPoints[(i + 1) % count];
            builder.AddCylinder(v0, v1, diameter, segments);
            builder.AddSphere(v0, radius);
        }

        Geometry = builder.ToMeshGeometry3D();
    }

    // public methods

    public void AddAnchor(Vector3 point) {
        // find closest vertex
        var index = Model.GetClosestVertex(new System.Numerics.Vector3(point.X, point.Y, point.Z));
        Vector3 vector = Model
            .GetVertices([index])
            .Select(v => new Vector3((float)v[0], (float)v[1], (float)v[2]))
            .First();

        if (AnchorIndexes.Count < 3) {
            AnchorIndexes.Add(index);
            return;
        }

        // adding to the closest point on the path
        int min = int.MaxValue;
        float min_distance = float.MaxValue;
        for (int i = 0; i < PathPoints.Length; i++) {
            float distance = (PathPoints[i] - vector).LengthSquared();
            if (distance >= min_distance) { continue; }

            min = i;
            min_distance = distance;
        }

        // which two anchors is this one between?
        int last_anchor = -1;
        for (int i = 0; i < PathIndexes.Length; i++) {
            int vId = PathIndexes[i];
            if (AnchorIndexes.Contains(vId)) {
                last_anchor = AnchorIndexes.IndexOf(vId);
                continue;
            }

            if (vId != min) { continue; }

            break;
        }

        if (last_anchor < 0) {
            throw new Exception("Add Anchor: Unable to find an anchor!");
        }

        if (last_anchor >= AnchorIndexes.Count) {
            AnchorIndexes.Add(index);
        } else {
            AnchorIndexes.Insert(last_anchor, index);
        }

    }

    public MeshGeometry3D PreviewAnchor(Vector3 point) {
        var index = Model.GetClosestVertex(new System.Numerics.Vector3(point.X, point.Y, point.Z));
        Vector3 vector = Model
            .GetVertices([index])
            .Select(v => new Vector3((float)v[0], (float)v[1], (float)v[2]))
            .First();

        Vector3 normal = Model
            .GetVtxNormals([index])
            .Select(v => new Vector3((float)v[0], (float)v[1], (float)v[2]))
            .First();

        MeshBuilder builder = new();
        builder.AddCylinder(point, point + normal * 4.0f, 1, 32);
        return builder.ToMeshGeometry3D();
    }
}
