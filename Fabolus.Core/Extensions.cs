using Fabolus.Core.Common;
using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HelixToolkit.Wpf.SharpDX;
using static MR.DotNet;

namespace Fabolus.Core;
public static class Extensions {
    public static DMesh3 ToDMesh(this MeshGeometry3D mesh) {
        if (mesh is null || mesh.Positions.Count == 0) { return new DMesh3(); }

        List<Vector3d> vertices = new();

        foreach (var point in mesh.Positions) {
            vertices.Add(new Vector3d(point.X, point.Y, point.Z));
        }

        List<g3.Vector3f>? normals = new();
        foreach (var normal in mesh.Normals) {
            normals.Add(new g3.Vector3f(normal.X, normal.Y, normal.Z));
        }

        if (normals.Count == 0) { normals = null; }

        List<Index3i> triangles = new();
        for (int i = 0; i < mesh.TriangleIndices.Count; i += 3) {
            triangles.Add(new Index3i(mesh.TriangleIndices[i], mesh.TriangleIndices[i + 1], mesh.TriangleIndices[i + 2]));
        }

        //converting the meshes to use Implicit Surface Modeling
        return DMesh3Builder.Build(vertices, triangles, normals);
    }

    public static DMesh3 BooleanSubtract(this MeshEditor editor, DMesh3 tool) 
        => Subtraction(editor.Mesh, tool);

    /// <summary>
    /// Adds multiple meshes to the main main.
    /// Useful if the meshes do not overlap.
    /// </summary>
    /// <param name="editor"></param>
    /// <param name="tools"></param>
    /// <returns></returns>
    public static MeshEditor Join(this MeshEditor editor, IList<DMesh3> tools) {
        foreach (var mesh in tools) {
            editor.AppendMesh(mesh);
        }

        return editor;
    }

    public static DMesh3 Subtraction(DMesh3 body, DMesh3 tool) {
        Mesh bodyMesh = body.ToMesh();
        Mesh toolMesh = tool.ToMesh();

        var result = Boolean(bodyMesh, toolMesh, BooleanOperation.DifferenceAB);
        return result.mesh.ToDMesh();

    }

    public static Mesh ToMesh(this DMesh3 mesh) {
        List<MR.DotNet.Vector3f> verts = mesh.Vertices().Select(v => new MR.DotNet.Vector3f((float)v.x, (float)v.y, (float)v.z)).ToList();
        List<ThreeVertIds> tris = mesh.Triangles().Select(t => new ThreeVertIds(t.a, t.b, t.c)).ToList();
        return Mesh.FromTriangles(verts, tris);
    }

    public static DMesh3 ToDMesh(this Mesh mesh) {
        DMesh3 result = new();

        foreach (var p in mesh.Points) {
            result.AppendVertex(new Vector3d(p.X, p.Y, p.Z));
        }
        foreach (var t in mesh.Triangulation) {
            result.AppendTriangle(t.v0.Id, t.v1.Id, t.v2.Id);
        }

        return result;
    }
}
