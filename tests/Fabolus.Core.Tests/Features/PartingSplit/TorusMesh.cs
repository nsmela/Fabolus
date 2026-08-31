using System.Numerics;
using Fabolus.Core.Common;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// Builds a torus test mesh directly via IGeometryEngine.CreateMesh - there's no torus
/// generator in the engine, this is test-only scaffolding for exercising parting-line hole
/// detection. The hole runs along <c>holeAxis</c>, so a pull along that axis passes through it.
/// </summary>
internal static class TorusMesh
{
    /// <param name="holeAxis">
    /// Which axis the hole runs along - only UnitX (the default) and UnitY are supported. Tests of
    /// the parting line alone can use either; tests that go on to build a parting mesh need UnitY,
    /// since the flange is only supported on that axis.
    /// </param>
    public static Result<IMesh> Create(
        IGeometryEngine engine,
        double majorRadius,
        double minorRadius,
        int majorSegments,
        int minorSegments,
        Vector3? holeAxis = null)
    {
        var axis = holeAxis ?? Vector3.UnitX;
        if (axis != Vector3.UnitX && axis != Vector3.UnitY)
            return new Error("Torus.UnsupportedAxis", "Only UnitX and UnitY hole axes are supported.");

        var alongY = axis == Vector3.UnitY;
        var vertices = new List<double>();

        for (int i = 0; i < majorSegments; i++)
        {
            double phi = 2.0 * Math.PI * i / majorSegments;
            double cosPhi = Math.Cos(phi);
            double sinPhi = Math.Sin(phi);

            for (int j = 0; j < minorSegments; j++)
            {
                double theta = 2.0 * Math.PI * j / minorSegments;
                double cosTheta = Math.Cos(theta);
                double sinTheta = Math.Sin(theta);

                double ringRadius = majorRadius + minorRadius * cosTheta;

                var alongAxis = minorRadius * sinTheta;  // offset along the hole's axis
                var ringA = ringRadius * cosPhi;         // the two in-plane components
                var ringB = ringRadius * sinPhi;

                if (alongY)
                {
                    vertices.Add(ringA);       // X
                    vertices.Add(alongAxis);   // Y - along the hole's axis
                    vertices.Add(ringB);       // Z
                }
                else
                {
                    vertices.Add(alongAxis);   // X - along the hole's axis
                    vertices.Add(ringA);       // Y
                    vertices.Add(ringB);       // Z
                }
            }
        }

        var triangles = new List<int>();
        for (int i = 0; i < majorSegments; i++)
        {
            int iNext = (i + 1) % majorSegments;
            for (int j = 0; j < minorSegments; j++)
            {
                int jNext = (j + 1) % minorSegments;

                int a = i * minorSegments + j;
                int b = iNext * minorSegments + j;
                int c = iNext * minorSegments + jNext;
                int d = i * minorSegments + jNext;

                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(a); triangles.Add(c); triangles.Add(d);
            }
        }

        return engine.CreateMesh(vertices.ToArray(), triangles.ToArray());
    }
}
