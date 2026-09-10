using Fabolus.Core.Common;
using Fabolus.Core.Features.Decal;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using GeometryManifold.Internal;
using System.Numerics;

namespace GeometryManifold.Text;

/// <summary>
/// Builds engraved and embossed text solids and wraps them onto a curved surface. Both halves
/// leaned entirely on MeshLib - its planar triangulator for the glyph caps, its spatial index for
/// the surface queries - and Manifold provides neither, so the triangulation comes from
/// <see cref="PolygonTriangulator"/> and every surface query from <see cref="MeshBvh"/>.
/// </summary>
internal static class TextMeshBuilder
{
    /// <summary>How close to the baseline a point must sit to be placed on it directly rather than projected.</summary>
    private const float BaselineVerticalTolerance = 1e-4f;

    /// <summary>Below this squared length a cross product is treated as degenerate, not a normal.</summary>
    private const float DegenerateCrossLengthSquared = 1e-8f;

    /// <summary>Below this the two baseline samples are effectively coincident, so don't interpolate between them.</summary>
    private const float MinInterpolationSpan = 1e-6f;

    /// <summary>Millimetres to step along the surface when marching out the baseline. Smaller follows curvature more closely at the cost of more distance queries.</summary>
    private const float BaselineStepSizeMm = 0.5f;

    /// <summary>Millimetres of baseline marched past each end of the text, so glyphs at the extremes still sample a frame either side of themselves.</summary>
    private const float BaselineMarginMm = 2.0f;

    /// <summary>Millimetres to back the projection ray off along the frame normal before firing it at the surface. Must clear the tallest mesh this is used on.</summary>
    private const float ProjectionRayOffsetMm = 150.0f;

    /// <summary>Dot product below which the hit surface has turned more than 60 degrees away from the decal frame - too curved for the text to sit flat.</summary>
    private const float MaxSurfaceDeviationDot = 0.5f;

    /// <summary>
    /// Builds an extruded 3D solid from 2D polygon outlines in the tangent frame, following the
    /// target surface where one is supplied.
    /// </summary>
    public static Result<IMesh> BuildPrism(
        IGeometryEngine engine,
        IReadOnlyList<Polygon2D> outlines,
        DecalFrame frame,
        float depth,
        float sink,
        float overshoot,
        float maxEdgeLength = 0f,
        IMesh? targetMesh = null)
    {
        if (outlines is null || outlines.Count == 0)
            return DecalErrors.EmptyOutlines;

        var resampled = maxEdgeLength > 0f ? ResamplePolygons(outlines, maxEdgeLength) : outlines;

        var contours = new List<IReadOnlyList<Vector2>>();
        foreach (var polygon in resampled)
        {
            if (polygon.OuterBoundary.Count < 3) continue;
            contours.Add(polygon.OuterBoundary);

            foreach (var hole in polygon.Holes)
            {
                if (hole.Count >= 3) contours.Add(hole);
            }
        }

        if (contours.Count == 0)
            return DecalErrors.EmptyOutlines;

        var (pts2D, rawFaces) = PolygonTriangulator.Triangulate(contours);
        if (rawFaces.Count == 0)
            return DecalErrors.TriangulationFailed;

        // The triangulator already emits counter-clockwise faces, but normalise anyway so the
        // cap winding below is correct regardless of what it hands back.
        var faces2D = new List<(int A, int B, int C)>(rawFaces.Count);
        foreach (var (a, b, c) in rawFaces)
        {
            var vA = pts2D[a];
            var vB = pts2D[b];
            var vC = pts2D[c];
            float cross = (vB.X - vA.X) * (vC.Y - vA.Y) - (vB.Y - vA.Y) * (vC.X - vA.X);
            faces2D.Add(cross < 0 ? (a, c, b) : (a, b, c));
        }

        float zMin = sink;
        float zMax = depth + overshoot;

        int ptCount = pts2D.Count;
        var vertices = new List<double>(ptCount * 2 * 3);
        var triangles = new List<int>(faces2D.Count * 2 * 3 + ptCount * 6);

        var bottomMap = new int[ptCount];
        var topMap = new int[ptCount];

        MeshBvh? targetBvh = null;
        List<BaselineFrame>? baseline = null;

        if (targetMesh is not null && targetMesh.TriangleCount > 0)
        {
            targetBvh = new MeshBvh(targetMesh.Vertices, targetMesh.Triangles);

            // Seeded from the first point, not from 0: seeding at 0 forces the range to straddle
            // the origin, which silently widens the sampled baseline for any outline set that does
            // not already centre on it.
            float minX = ptCount > 0 ? pts2D[0].X : 0f;
            float maxX = minX;
            for (int i = 1; i < ptCount; i++)
            {
                if (pts2D[i].X < minX) minX = pts2D[i].X;
                if (pts2D[i].X > maxX) maxX = pts2D[i].X;
            }

            baseline = BuildSurfaceBaseline(targetBvh, frame, minX, maxX, BaselineStepSizeMm);
        }

        bool hasSurface = targetBvh is not null && baseline is not null;

        for (int i = 0; i < ptCount; i++)
        {
            var p = pts2D[i];
            Vector3 pSurface;
            Vector3 localNorm;

            if (hasSurface)
            {
                var baseFrame = SampleBaseline(baseline!, p.X);

                if (MathF.Abs(p.Y) < BaselineVerticalTolerance)
                {
                    pSurface = baseFrame.Position;
                    localNorm = baseFrame.N;
                }
                else
                {
                    var proposed = baseFrame.Position + p.Y * baseFrame.V;
                    if (targetBvh!.ClosestPoint(proposed, out _, out int triangle, out _))
                    {
                        var norm = targetBvh.TriangleNormal(triangle);
                        if (norm == Vector3.Zero) norm = baseFrame.N;
                        if (Vector3.Dot(norm, baseFrame.N) < 0f) norm = -norm;

                        targetBvh.GetTriangleVertices(triangle, out var v0, out _, out _);
                        pSurface = proposed - Vector3.Dot(proposed - v0, norm) * norm;
                        localNorm = norm;
                    }
                    else
                    {
                        pSurface = proposed;
                        localNorm = baseFrame.N;
                    }
                }
            }
            else
            {
                pSurface = frame.Origin + p.X * frame.U + p.Y * frame.V;
                localNorm = frame.N;
            }

            var pBot = pSurface + zMin * localNorm;
            var pTop = pSurface + zMax * localNorm;

            bottomMap[i] = vertices.Count / 3;
            vertices.Add(pBot.X);
            vertices.Add(pBot.Y);
            vertices.Add(pBot.Z);

            topMap[i] = vertices.Count / 3;
            vertices.Add(pTop.X);
            vertices.Add(pTop.Y);
            vertices.Add(pTop.Z);
        }

        var edgeCounts = new Dictionary<(int, int), int>();

        foreach (var (a, b, c) in faces2D)
        {
            // Bottom face is wound clockwise (normal -N), top counter-clockwise (normal +N).
            triangles.Add(bottomMap[a]);
            triangles.Add(bottomMap[c]);
            triangles.Add(bottomMap[b]);

            triangles.Add(topMap[a]);
            triangles.Add(topMap[b]);
            triangles.Add(topMap[c]);

            EdgeCounter.Add(edgeCounts, a, b);
            EdgeCounter.Add(edgeCounts, b, c);
            EdgeCounter.Add(edgeCounts, c, a);
        }

        foreach (var (edge, _) in edgeCounts)
        {
            var (a, b) = edge;
            if (edgeCounts.ContainsKey((b, a))) continue;

            triangles.Add(bottomMap[a]);
            triangles.Add(bottomMap[b]);
            triangles.Add(topMap[b]);

            triangles.Add(bottomMap[a]);
            triangles.Add(topMap[b]);
            triangles.Add(topMap[a]);
        }

        var meshResult = engine.CreateMesh(vertices.ToArray().AsSpan(), triangles.ToArray().AsSpan());
        if (meshResult.IsFailure) return meshResult;

        var metadata = new MeshMetadata().WithProperties(m =>
            m.Set(CoreKeys.Id, Guid.NewGuid())
             .Set(CoreKeys.Name, "Text Prism")
             .Set(CoreKeys.CreatedBy, "TextMeshBuilder.BuildPrism"));

        return Result.Success(meshResult.Value.WithMetadata(metadata));
    }

    /// <summary>
    /// Projects each vertex of a text prism onto the curved surface of the target mesh along the
    /// frame's normal.
    /// </summary>
    public static Result<IMesh> ProjectPrism(
        IGeometryEngine engine,
        IMesh targetMesh,
        DecalFrame frame,
        IMesh prismMesh,
        List<string>? warnings = null)
    {
        if (targetMesh is null || prismMesh is null)
            return Result.Success(prismMesh!);

        var bvh = new MeshBvh(targetMesh.Vertices, targetMesh.Triangles);

        var vertices = prismMesh.Vertices;
        var triangles = prismMesh.Triangles;
        var projected = new double[vertices.Length * 3];

        bool hadMiss = false;
        bool hadLargeDeviation = false;

        var rayDirection = -frame.N;

        for (int i = 0; i < vertices.Length; i++)
        {
            var local = frame.ToLocal(vertices[i]); // (u, v, zLocal)
            var anchor = frame.Origin + local.X * frame.U + local.Y * frame.V;

            Vector3 hitPoint;
            var hitNormal = frame.N;

            var origin = anchor + ProjectionRayOffsetMm * frame.N;
            if (bvh.Raycast(origin, rayDirection, out float distance, out int triangle))
            {
                hitPoint = origin + rayDirection * distance;

                var normal = bvh.TriangleNormal(triangle);
                if (normal != Vector3.Zero && normal.LengthSquared() > DegenerateCrossLengthSquared)
                {
                    hitNormal = normal;
                }

                if (Vector3.Dot(hitNormal, frame.N) < MaxSurfaceDeviationDot)
                {
                    hadLargeDeviation = true;
                }
            }
            else
            {
                // Fall back to firing the other way: the anchor may sit past the far side.
                var reverseOrigin = anchor - ProjectionRayOffsetMm * frame.N;
                if (bvh.Raycast(reverseOrigin, frame.N, out float reverseDistance, out _))
                {
                    hitPoint = reverseOrigin + frame.N * reverseDistance;
                }
                else
                {
                    hadMiss = true;
                    hitPoint = anchor;
                }
            }

            var final = hitPoint + hitNormal * local.Z;
            projected[i * 3] = final.X;
            projected[i * 3 + 1] = final.Y;
            projected[i * 3 + 2] = final.Z;
        }

        if (hadMiss) warnings?.Add("Label extends past the surface");
        if (hadLargeDeviation) warnings?.Add("Surface too curved for this size");

        var createResult = engine.CreateMesh(projected.AsSpan(), triangles.AsSpan());
        if (createResult.IsFailure) return createResult;

        return Result.Success(createResult.Value.WithMetadata(prismMesh.Metadata));
    }

    private static IReadOnlyList<Polygon2D> ResamplePolygons(IReadOnlyList<Polygon2D> polygons, float maxEdgeLength)
    {
        var result = new List<Polygon2D>(polygons.Count);
        foreach (var polygon in polygons)
        {
            var holes = new List<IReadOnlyList<Vector2>>(polygon.Holes.Count);
            foreach (var hole in polygon.Holes)
            {
                holes.Add(ResampleRing(hole, maxEdgeLength));
            }

            result.Add(new Polygon2D
            {
                OuterBoundary = ResampleRing(polygon.OuterBoundary, maxEdgeLength),
                Holes = holes,
            });
        }
        return result;
    }

    private static IReadOnlyList<Vector2> ResampleRing(IReadOnlyList<Vector2> ring, float maxEdgeLength)
    {
        if (ring.Count < 3) return ring;

        var points = new List<Vector2>();
        for (int i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            points.Add(a);

            float distance = Vector2.Distance(a, b);
            if (distance <= maxEdgeLength) continue;

            int segments = (int)MathF.Ceiling(distance / maxEdgeLength);
            for (int s = 1; s < segments; s++)
            {
                points.Add(Vector2.Lerp(a, b, (float)s / segments));
            }
        }
        return points;
    }

    private readonly struct BaselineFrame
    {
        public readonly float ArcLength;
        public readonly Vector3 Position;
        public readonly Vector3 U;
        public readonly Vector3 V;
        public readonly Vector3 N;

        public BaselineFrame(float arcLength, Vector3 position, Vector3 u, Vector3 v, Vector3 n)
        {
            ArcLength = arcLength;
            Position = position;
            U = u;
            V = v;
            N = n;
        }
    }

    /// <summary>
    /// Walks the surface out from the frame's origin in both directions, re-seating each step onto
    /// the mesh and carrying the tangent frame with it. This is what makes text follow curvature
    /// rather than sitting on a flat plane through the anchor.
    /// </summary>
    private static List<BaselineFrame> BuildSurfaceBaseline(
        MeshBvh bvh,
        DecalFrame frame,
        float minX,
        float maxX,
        float stepSize)
    {
        var positive = March(bvh, frame, MathF.Max(0f, maxX + BaselineMarginMm), stepSize, forward: true);
        var negative = March(bvh, frame, MathF.Abs(MathF.Min(0f, minX - BaselineMarginMm)), stepSize, forward: false);

        var frames = new List<BaselineFrame>(positive.Count + negative.Count + 1);
        for (int i = negative.Count - 1; i >= 0; i--) frames.Add(negative[i]);
        frames.Add(new BaselineFrame(0f, frame.Origin, frame.U, frame.V, frame.N));
        frames.AddRange(positive);

        return frames;
    }

    private static List<BaselineFrame> March(MeshBvh bvh, DecalFrame frame, float distance, float stepSize, bool forward)
    {
        var frames = new List<BaselineFrame>();
        int steps = (int)MathF.Ceiling(distance / stepSize);

        var position = frame.Origin;
        var u = forward ? frame.U : -frame.U;
        var v = frame.V;
        var n = frame.N;

        for (int i = 1; i <= steps; i++)
        {
            float arcLength = forward ? i * stepSize : -i * stepSize;
            var proposed = position + u * stepSize;

            if (bvh.ClosestPoint(proposed, out _, out int triangle, out _))
            {
                var norm = bvh.TriangleNormal(triangle);
                if (norm == Vector3.Zero) norm = n;
                if (Vector3.Dot(norm, n) < 0f) norm = -norm;

                // Slide the proposed point back onto the triangle's plane, then re-orthogonalise
                // the frame against the new normal so the next step travels along the surface.
                bvh.GetTriangleVertices(triangle, out var v0, out _, out _);
                position = proposed - Vector3.Dot(proposed - v0, norm) * norm;

                var uProjected = u - Vector3.Dot(u, norm) * norm;
                u = uProjected.LengthSquared() > DegenerateCrossLengthSquared ? Vector3.Normalize(uProjected) : u;

                var nextV = Vector3.Cross(norm, forward ? u : -u);
                if (nextV.LengthSquared() > DegenerateCrossLengthSquared)
                {
                    nextV = Vector3.Normalize(nextV);
                    if (Vector3.Dot(nextV, v) < 0f) nextV = -nextV;
                    v = nextV;
                }

                n = norm;
            }
            else
            {
                position = proposed;
            }

            frames.Add(new BaselineFrame(arcLength, position, forward ? u : -u, v, n));
        }

        return frames;
    }

    private static BaselineFrame SampleBaseline(IReadOnlyList<BaselineFrame> frames, float u)
    {
        if (frames.Count == 0)
            return new BaselineFrame(u, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        if (frames.Count == 1 || u <= frames[0].ArcLength)
            return frames[0];
        if (u >= frames[^1].ArcLength)
            return frames[^1];

        int low = 0;
        int high = frames.Count - 1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (frames[mid].ArcLength < u) low = mid + 1;
            else high = mid - 1;
        }

        int index0 = Math.Max(0, low - 1);
        int index1 = Math.Min(frames.Count - 1, index0 + 1);

        var f0 = frames[index0];
        var f1 = frames[index1];

        float span = f1.ArcLength - f0.ArcLength;
        float t = span > MinInterpolationSpan ? Math.Clamp((u - f0.ArcLength) / span, 0f, 1f) : 0f;

        return new BaselineFrame(
            u,
            Vector3.Lerp(f0.Position, f1.Position, t),
            Vector3.Normalize(Vector3.Lerp(f0.U, f1.U, t)),
            Vector3.Normalize(Vector3.Lerp(f0.V, f1.V, t)),
            Vector3.Normalize(Vector3.Lerp(f0.N, f1.N, t)));
    }
}
