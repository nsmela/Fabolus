namespace Fabolus.Core;
public static class FabolusMeshExtensions {
    public static Fabolus.Core.Geometry.Metadata.MeshMetadata GetFabolusMetadata(this IMesh mesh) {
        return mesh.Metadata.AsFabolus();
    }
    public static Fabolus.Core.Geometry.Metadata.MeshMetadata AsFabolus(this GeometryEngine.Core.Geometry.MeshMetadata baseMetadata) {
        if (baseMetadata is Fabolus.Core.Geometry.Metadata.MeshMetadata fm) return fm;
        return new Fabolus.Core.Geometry.Metadata.MeshMetadata(baseMetadata.Name, baseMetadata.CreatedBy);
    }

    public static BasicResults.Result<IMesh> Rotate(this GeometryEngine.Core.Geometry.IGeometryTransforms transforms, IMesh mesh, System.Numerics.Quaternion q) {
        var axis = new System.Numerics.Vector3(q.X, q.Y, q.Z);
        var length = axis.Length();
        if (length < 1e-6f) return BasicResults.Result<IMesh>.Success(mesh);
        var angle = 2 * System.Math.Atan2(length, q.W);
        var dir = GeometryEngine.Core.Geometry.Primitives.Direction.From(new Vector3(axis.X, axis.Y, axis.Z)).Value;
        return transforms.Rotate(mesh, dir, angle);
    }

    public static BasicResults.Result<IMesh> Translate(this GeometryEngine.Core.Geometry.IGeometryTransforms transforms, IMesh mesh, double x, double y, double z) {
        return transforms.Translate(mesh, new Vector3(x, y, z));
    }

}

