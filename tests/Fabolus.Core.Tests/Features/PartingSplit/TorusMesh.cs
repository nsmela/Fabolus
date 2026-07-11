using Fabolus.Core.Common;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// Builds a torus test mesh directly via IGeometryEngine.CreateMesh - there's no torus
/// generator in the engine, this is test-only scaffolding for exercising parting-line hole
/// detection. The hole runs along the X axis (pull direction UnitX passes through it).
/// </summary>
internal static class TorusMesh
{
    public static Result<IMesh> Create(IGeometryEngine engine, double majorRadius, double minorRadius, int majorSegments, int minorSegments)
    {
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

                vertices.Add(minorRadius * sinTheta);   // X - along the hole's axis
                vertices.Add(ringRadius * cosPhi);       // Y
                vertices.Add(ringRadius * sinPhi);       // Z
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
