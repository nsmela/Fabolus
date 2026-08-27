using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using System.Numerics;
using static MR;

namespace GeometryMeshLib;

internal sealed class Transforms : IGeometryTransforms
{
    private readonly GeometryEngine _engine;

    public Transforms(GeometryEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public Result<IMesh> Translate(IMesh source, double deltaX, double deltaY, double deltaZ)
    {
        using var mrMesh = source.ToMRMesh();
        var pts = mrMesh.points.vec;
        ulong size = pts.size();
        for (ulong i = 0; i < size; i++)
        {
            var p = pts[i];
            pts[i] = new MR.Vector3f(p.x + (float)deltaX, p.y + (float)deltaY, p.z + (float)deltaZ);
        }
        mrMesh.invalidateCaches();

        var newMetadata = source.Metadata.WithProperties(m =>
            m.Set(CoreKeys.Name, $"Translated ({source.Metadata.Name})")
             .Set(CoreKeys.CreatedBy, $"Translate({deltaX}, {deltaY}, {deltaZ})"));

        return Result.Success(mrMesh.ToIMesh(newMetadata));
    }

    public Result<IMesh> Scale(IMesh source, double scaleFactor) =>
        Scale(source, scaleFactor, scaleFactor, scaleFactor);

    public Result<IMesh> Scale(IMesh source, double scaleX, double scaleY, double scaleZ)
    {
        if (scaleX <= 0 || scaleY <= 0 || scaleZ <= 0)
            return GeometryErrors.InvalidScale;

        using var mrMesh = source.ToMRMesh();
        var pts = mrMesh.points.vec;
        ulong size = pts.size();
        for (ulong i = 0; i < size; i++)
        {
            var p = pts[i];
            pts[i] = new MR.Vector3f(p.x * (float)scaleX, p.y * (float)scaleY, p.z * (float)scaleZ);
        }
        mrMesh.invalidateCaches();

        var newMetadata = source.Metadata.WithProperties(m =>
            m.Set(CoreKeys.Name, $"Scaled ({source.Metadata.Name})")
             .Set(CoreKeys.CreatedBy, $"Scale({scaleX}, {scaleY}, {scaleZ})"));

        return Result.Success(mrMesh.ToIMesh(newMetadata));
    }

    public Result<IMesh> Rotate(IMesh source, Quaternion q) {
        using var mrMesh = source.ToMRMesh();

        // Normalize the quaternion to ensure no scaling is applied
        var nq = Quaternion.Normalize(q);

        // Calculate quaternion component products
        float xx = nq.X * nq.X;
        float yy = nq.Y * nq.Y;
        float zz = nq.Z * nq.Z;
        float xy = nq.X * nq.Y;
        float xz = nq.X * nq.Z;
        float yz = nq.Y * nq.Z;
        float wx = nq.W * nq.X;
        float wy = nq.W * nq.Y;
        float wz = nq.W * nq.Z;

        // Convert quaternion to a 3x3 rotation matrix (row by row)
        var rowX = new Vector3f(
            1.0f - 2.0f * (yy + zz),
            2.0f * (xy - wz),
            2.0f * (xz + wy)
        );

        var rowY = new Vector3f(
            2.0f * (xy + wz),
            1.0f - 2.0f * (xx + zz),
            2.0f * (yz - wx)
        );

        var rowZ = new Vector3f(
            2.0f * (xz - wy),
            2.0f * (yz + wx),
            1.0f - 2.0f * (xx + yy)
        );

        var rotationMatrix = new Matrix3f(rowX, rowY, rowZ);
        var transform = AffineXf3f.linear(rotationMatrix);
        mrMesh.transform(transform);

        return Result.Success(mrMesh.ToIMesh(source.Metadata));
    }

}
