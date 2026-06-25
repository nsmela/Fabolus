using Fabolus.Core.Common;
using Fabolus.Core.Geometry;

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

    public Result<IMesh> Rotate(IMesh source, double angleRadians, double axisX, double axisY, double axisZ)
    {
        if (source is not MRMesh mrMesh)
            return GeometryErrors.InvalidMeshType;

        double axisLenSq = axisX * axisX + axisY * axisY + axisZ * axisZ;
        if (axisLenSq < 1e-10)
            return GeometryErrors.InvalidAxis;

        double axisLen = Math.Sqrt(axisLenSq);
        double ux = axisX / axisLen;
        double uy = axisY / axisLen;
        double uz = axisZ / axisLen;

        var clone = new MR.Mesh(mrMesh.Mesh);

        // TODO: must be a cleaner way
        // Rotate around mesh bounding box center
        var bbox = MR.computeBoundingBox(clone.topology, clone.points, null, null);
        var center = bbox.center();

        var pts = clone.points.vec;
        ulong size = pts.size();
        float cosTheta = (float)Math.Cos(angleRadians);
        float sinTheta = (float)Math.Sin(angleRadians);
        float ax = (float)ux;
        float ay = (float)uy;
        float az = (float)uz;

        for (ulong i = 0; i < size; i++)
        {
            var p = pts[i];
            float x = p.x - center.x;
            float y = p.y - center.y;
            float z = p.z - center.z;

            float dot = ax * x + ay * y + az * z;
            float crossX = ay * z - az * y;
            float crossY = az * x - ax * z;
            float crossZ = ax * y - ay * x;

            float rx = x * cosTheta + crossX * sinTheta + ax * dot * (1.0f - cosTheta);
            float ry = y * cosTheta + crossY * sinTheta + ay * dot * (1.0f - cosTheta);
            float rz = z * cosTheta + crossZ * sinTheta + az * dot * (1.0f - cosTheta);

            pts[i] = new MR.Vector3f(rx + center.x, ry + center.y, rz + center.z);
        }
        clone.invalidateCaches();

        var rotationMetadata = new MeshRotation(
            angleRadians,
            ux, uy, uz,
            center.x, center.y, center.z
        );

        IMesh? transformedOriginal = null;
        if (mrMesh.OriginalMesh != null)
        {
            if (mrMesh.OriginalMesh is MRMesh origMR)
            {
                var origClone = new MR.Mesh(origMR.Mesh);
                var origPts = origClone.points.vec;
                ulong origSize = origPts.size();
                for (ulong i = 0; i < origSize; i++)
                {
                    var p = origPts[i];
                    float x = p.x - center.x;
                    float y = p.y - center.y;
                    float z = p.z - center.z;

                    float dot = ax * x + ay * y + az * z;
                    float crossX = ay * z - az * y;
                    float crossY = az * x - ax * z;
                    float crossZ = ax * y - ay * x;

                    float rx = x * cosTheta + crossX * sinTheta + ax * dot * (1.0f - cosTheta);
                    float ry = y * cosTheta + crossY * sinTheta + ay * dot * (1.0f - cosTheta);
                    float rz = z * cosTheta + crossZ * sinTheta + az * dot * (1.0f - cosTheta);

                    origPts[i] = new MR.Vector3f(rx + center.x, ry + center.y, rz + center.z);
                }
                origClone.invalidateCaches();
                transformedOriginal = new MRMesh(origClone, origMR.Metadata, null);
            }
        }

        var newMetadata = source.Metadata.WithProperties(m =>
            m.Set(CoreKeys.Name, $"Rotated ({source.Metadata.Name})")
             .Set(CoreKeys.CreatedBy, $"Rotate({angleRadians}rad)")
             .Set(CoreKeys.Rotation, rotationMetadata));

        return new MRMesh(clone, newMetadata, transformedOriginal);
    }

}
