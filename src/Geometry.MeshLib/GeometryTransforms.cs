using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using System.Numerics;
using static MR;

namespace GeometryMeshLib;

internal sealed class GeometryTransforms : IGeometryTransforms
{
    private readonly GeometryEngine _engine;

    public GeometryTransforms(GeometryEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public Result<IMesh> Translate(IMesh source, double deltaX, double deltaY, double deltaZ)
    {
        if (source is not MRMesh mrMesh)
            return GeometryErrors.InvalidMeshType;

        var clone = new MR.Mesh(mrMesh.Mesh);
        var pts = clone.points.vec;
        ulong size = pts.size();
        for (ulong i = 0; i < size; i++)
        {
            var p = pts[i];
            pts[i] = new MR.Vector3f(p.x + (float)deltaX, p.y + (float)deltaY, p.z + (float)deltaZ);
        }
        clone.invalidateCaches();

        IMesh? transformedOriginal = null;
        if (mrMesh.OriginalMesh != null)
        {
            var origResult = Translate(mrMesh.OriginalMesh, deltaX, deltaY, deltaZ);
            if (origResult.IsSuccess)
            {
                transformedOriginal = origResult.Value;
            }
        }

        var newMetadata = source.Metadata.WithProperties(m =>
            m.Set(CoreKeys.Name, $"Translated ({source.Metadata.Name})")
             .Set(CoreKeys.CreatedBy, $"Translate({deltaX}, {deltaY}, {deltaZ})"));

        return new MRMesh(clone, newMetadata, transformedOriginal);
    }

    public Result<IMesh> Scale(IMesh source, double scaleFactor) =>
        Scale(source, scaleFactor, scaleFactor, scaleFactor);

    public Result<IMesh> Scale(IMesh source, double scaleX, double scaleY, double scaleZ)
    {
        if (scaleX <= 0 || scaleY <= 0 || scaleZ <= 0)
            return GeometryErrors.InvalidScale;

        if (source is not MRMesh mrMesh)
            return GeometryErrors.InvalidMeshType;

        var clone = new MR.Mesh(mrMesh.Mesh);
        var pts = clone.points.vec;
        ulong size = pts.size();
        for (ulong i = 0; i < size; i++)
        {
            var p = pts[i];
            pts[i] = new MR.Vector3f(p.x * (float)scaleX, p.y * (float)scaleY, p.z * (float)scaleZ);
        }
        clone.invalidateCaches();

        IMesh? transformedOriginal = null;
        if (mrMesh.OriginalMesh != null)
        {
            var origResult = Scale(mrMesh.OriginalMesh, scaleX, scaleY, scaleZ);
            if (origResult.IsSuccess)
            {
                transformedOriginal = origResult.Value;
            }
        }

        var newMetadata = source.Metadata.WithProperties(m =>
            m.Set(CoreKeys.Name, $"Scaled ({source.Metadata.Name})")
             .Set(CoreKeys.CreatedBy, $"Scale({scaleX}, {scaleY}, {scaleZ})"));

        return new MRMesh(clone, newMetadata, transformedOriginal);
    }

    public Result<IMesh> Rotate(IMesh source, Quaternion q) {
        if (source is not MRMesh mrMesh)
            return GeometryErrors.InvalidMeshType;

        var clone = new MR.Mesh(mrMesh.Mesh);

        // Calculate quaternion component products
        float xx = q.X * q.X;
        float yy = q.Y * q.Y;
        float zz = q.Z * q.Z;
        float xy = q.X * q.Y;
        float xz = q.X * q.Z;
        float yz = q.Y * q.Z;
        float wx = q.W * q.X;
        float wy = q.W * q.Y;
        float wz = q.W * q.Z;

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
        clone.transform(transform);

        return Result<IMesh>.Success(new MRMesh(clone, source.Metadata, source));
    }

}
