using System.Numerics;
using Fabolus.Core.Common;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Diagnostics;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Core.Tests.Diagnostics;

/// <summary>
/// An exploration, not a feature: build the parting surface from the mould rather than from the body.
///
/// <para>
/// The sweep in use takes the body's surface normal at the parting line and marches outward along it,
/// so the mould's own shape never enters into it and the body's undulation is carried the whole way to
/// the outer wall. But only the inner edge of that surface has to follow anatomy - it is the rim the
/// two halves meet the bolus on. The outer edge merely has to come out somewhere on the mould's wall,
/// and where it comes out is free.
/// </para>
///
/// <para>
/// So: take the outer edge from the mould instead. The mould has a top and a bottom along the pull
/// axis; the curve halfway between them, carried round the mould's silhouette, is smooth by
/// construction - it owes nothing to the body. Then loft between the two, anatomical at the inner edge
/// and level at the outer, and let the blend absorb the difference.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class MouldLedgeParting
{
    private const int Size = 900;

    private readonly IGeometryEngine _engine;
    private readonly PartingMeshFeature _sut;
    private readonly ITestOutputHelper _out;

    public MouldLedgeParting(GeometryEngineFixture fixture, ITestOutputHelper output)
    {
        _engine = fixture.Engine;
        _sut = new PartingMeshFeature(_engine);
        _out = output;
    }

    [Theory]
    [InlineData("chin.3mf")]
    [InlineData("scalp.3mf")]
    [InlineData("nose.3mf")]
    public void LoftFromTheMouldsMidHeightLedge(string file)
    {
        var directory = Environment.GetEnvironmentVariable("FABOLUS_LEDGE_DIR");
        var path = Path.Combine(Assets(), "3mf", file);
        if (!File.Exists(path)) { _out.WriteLine($"{file}: absent"); return; }

        var imported = _engine.IO.Import(path);
        var mould = MouldMesh.Create(imported.Value);
        var body = BodyMesh.Create(_engine, mould.Value).Value;

        var traced = _sut.GeneratePartingLineFromThickness(body);
        if (traced.IsFailure) { _out.WriteLine($"{file}: trace failed"); return; }

        var line = traced.Value.Loops[0];
        var resolved = PartingMeshFeature.ResolveAxis(traced.Value, PartingMeshParameters.Default);
        var axis = Vector3.Normalize(resolved.Value.Axis);

        // 1. The mould's top and bottom along the pull axis. The obvious ledge is halfway between them,
        //    and that is what this tried first - but the parting line does not sit halfway. On chin it
        //    runs near the top of the mould, so a mid-height ledge makes the loft drop the better part
        //    of a centimetre on its way out and every face it crosses is steep for the trip. The height
        //    that costs nothing is the line's own, so the surface leaves level on average and only the
        //    line's departures from it have to be absorbed.
        var (low, high) = Extent(mould.Value.Mesh.Vertices, axis);

        float ledge = 0f;
        foreach (var p in line) ledge += Vector3.Dot(p, axis);
        ledge /= line.Count;

        // 2. The mould's own outer contour, re-heighted to the ledge. Taken from the engine rather than
        //    rebuilt by bearing: a bearing fan assumes the outline is star-shaped about its centre, and
        //    a mould's is not, which is what made the first attempt's ring wander.
        // The mould's own outline seen along the axis - its hull in that plane, so a rounded box comes
        // back rounded rather than as the axis-aligned rectangle GenerateOuterContour hands out.
        var outline = Outline(mould.Value.Mesh.Vertices, axis, line.Count);

        // Two rings to compare. The level one is the ledge as first tried. The matched one takes its
        // height from the parting line at the same bearing, so each radial of the loft runs out level
        // and the climb that made the steep faces is not there to make.
        var level = Lay(outline, axis, _ => ledge);
        var matched = Lay(outline, axis, Matched(outline, line, axis));

        _out.WriteLine($"{file}: mould spans {low,7:F1}..{high,7:F1} along the axis, ledge at {ledge,7:F1}");

        var flat = Build(line, level, axis);
        var echo = Build(line, matched, axis);

        if (flat.IsSuccess) Report("level ledge  ", flat.Value, axis);
        if (echo.IsSuccess) Report("height-matched", echo.Value, axis);

        if (string.IsNullOrWhiteSpace(directory) || echo.IsFailure) return;
        Directory.CreateDirectory(directory);
        Draw(directory, file, mould.Value.Mesh, echo.Value, line, matched);
    }

    /// <summary>
    /// The mould's outline seen along the pull axis, as <paramref name="count"/> evenly spaced points
    /// in the plane perpendicular to it.
    ///
    /// <para>
    /// Its convex hull, taken in that plane rather than in XY, because the pull axis is not a world
    /// axis and the engine's hull is not told which plane to work in. A mould is a rounded box, so the
    /// hull is the outline; and it is the outline rather than the bounding rectangle, which is the
    /// whole point - a rectangle sends the loft out to four sharp corners it has to fan across.
    /// </para>
    /// </summary>
    private static Vector2[] Outline(IReadOnlyList<Vector3> vertices, Vector3 axis, int count)
    {
        var (u, v) = Frame(axis);

        var points = new List<Vector2>(vertices.Count);
        foreach (var p in vertices) points.Add(Flat(p, u, v));

        var hull = Hull(points);

        var cumulative = new float[hull.Count + 1];
        for (int i = 0; i < hull.Count; i++)
            cumulative[i + 1] = cumulative[i] + Vector2.Distance(hull[i], hull[(i + 1) % hull.Count]);

        float perimeter = cumulative[hull.Count];
        var ring = new Vector2[count];

        int segment = 0;
        for (int k = 0; k < count; k++)
        {
            float target = perimeter * k / count;
            while (segment < hull.Count - 1 && cumulative[segment + 1] < target) segment++;

            float span = cumulative[segment + 1] - cumulative[segment];
            float t = span > 1e-6f ? Math.Clamp((target - cumulative[segment]) / span, 0f, 1f) : 0f;
            ring[k] = Vector2.Lerp(hull[segment], hull[(segment + 1) % hull.Count], t);
        }

        return ring;
    }

    /// <summary>Andrew's monotone chain, counter-clockwise.</summary>
    private static List<Vector2> Hull(List<Vector2> points)
    {
        points.Sort((a, b) => a.X == b.X ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));

        var hull = new List<Vector2>(points.Count + 1);

        for (int pass = 0; pass < 2; pass++)
        {
            int start = hull.Count;
            var order = pass == 0 ? points : Enumerable.Reverse(points).ToList();

            foreach (var p in order)
            {
                while (hull.Count >= start + 2 && Turn(hull[^2], hull[^1], p) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }

            hull.RemoveAt(hull.Count - 1);
        }

        return hull;
    }

    private static float Turn(Vector2 a, Vector2 b, Vector2 c) =>
        ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));

    /// <summary>
    /// A height for each outline point, taken from the parting line at the nearest bearing - the
    /// "section of the inner contour closest to that direction". Smoothed round the ring afterwards:
    /// the nearest-bearing pick steps whenever the winner changes, and a stepped ring would put a
    /// crease in the surface at every step.
    /// </summary>
    private static Func<int, float> Matched(
        Vector2[] outline, IReadOnlyList<Vector3> line, Vector3 axis)
    {
        var (u, v) = Frame(axis);

        var centre = Vector2.Zero;
        foreach (var p in line) centre += Flat(p, u, v);
        centre /= line.Count;

        var bearings = new float[line.Count];
        var heights = new float[line.Count];
        for (int i = 0; i < line.Count; i++)
        {
            var d = Flat(line[i], u, v) - centre;
            bearings[i] = MathF.Atan2(d.Y, d.X);
            heights[i] = Vector3.Dot(line[i], axis);
        }

        var picked = new float[outline.Length];
        for (int k = 0; k < outline.Length; k++)
        {
            var d = outline[k] - centre;
            float bearing = MathF.Atan2(d.Y, d.X);

            float closest = float.MaxValue;
            for (int i = 0; i < line.Count; i++)
            {
                float gap = MathF.Abs(Wrap(bearings[i] - bearing));
                if (gap >= closest) continue;

                closest = gap;
                picked[k] = heights[i];
            }
        }

        Smooth(picked, passes: 6);
        return k => picked[k];
    }

    /// <summary>Lifts a flat ring to a height per point.</summary>
    private static Vector3[] Lay(Vector2[] outline, Vector3 axis, Func<int, float> height)
    {
        var (u, v) = Frame(axis);

        var ring = new Vector3[outline.Length];
        for (int k = 0; k < outline.Length; k++)
            ring[k] = (u * outline[k].X) + (v * outline[k].Y) + (axis * height(k));

        return ring;
    }

    private Result<IMesh> Build(IReadOnlyList<Vector3> line, Vector3[] outer, Vector3 axis) =>
        Stitch(Loft(line, outer, axis, rings: 14));

    /// <summary>Lowest and highest the mesh reaches along the axis.</summary>
    private static (float Low, float High) Extent(IReadOnlyList<Vector3> vertices, Vector3 axis)
    {
        float low = float.MaxValue, high = float.MinValue;
        foreach (var v in vertices)
        {
            float h = Vector3.Dot(v, axis);
            low = MathF.Min(low, h);
            high = MathF.Max(high, h);
        }
        return (low, high);
    }

    /// <summary>
    /// The mould's outer contour resampled to <paramref name="count"/> evenly spaced points and laid
    /// flat at <paramref name="height"/> - the ledge the loft runs out to.
    /// </summary>
    private static Vector3[] Ring(
        IReadOnlyList<Vector3> contour, Vector3 axis, float height, int count)
    {
        int m = contour.Count;
        var cumulative = new float[m + 1];
        for (int i = 0; i < m; i++)
            cumulative[i + 1] = cumulative[i] + Vector3.Distance(contour[i], contour[(i + 1) % m]);

        float perimeter = cumulative[m];
        var ring = new Vector3[count];

        int segment = 0;
        for (int k = 0; k < count; k++)
        {
            float target = perimeter * k / count;
            while (segment < m - 1 && cumulative[segment + 1] < target) segment++;

            float span = cumulative[segment + 1] - cumulative[segment];
            float t = span > 1e-6f ? Math.Clamp((target - cumulative[segment]) / span, 0f, 1f) : 0f;

            var p = Vector3.Lerp(contour[segment], contour[(segment + 1) % m], t);
            ring[k] = p - (axis * Vector3.Dot(p, axis)) + (axis * height);
        }

        return ring;
    }

    /// <summary>
    /// The mould's outline seen along the axis, as a ring of <paramref name="count"/> points at
    /// <paramref name="height"/>. Built by bearing: the furthest vertex in each of a fan of directions
    /// from the centre, which is the outline of the shape rather than of its bounding box.
    /// </summary>
    private static Vector3[] Silhouette(
        IReadOnlyList<Vector3> vertices, Vector3 axis, float height, int count)
    {
        var (u, v) = Frame(axis);

        var centre = Vector2.Zero;
        foreach (var p in vertices) centre += Flat(p, u, v);
        centre /= vertices.Count;

        // One bucket per bearing, holding how far out the mould reaches on it.
        int buckets = Math.Max(count, 64);
        var reach = new float[buckets];

        foreach (var p in vertices)
        {
            var d = Flat(p, u, v) - centre;
            float r = d.Length();
            if (r < 1e-4f) continue;

            int bucket = Bucket(MathF.Atan2(d.Y, d.X), buckets);
            reach[bucket] = MathF.Max(reach[bucket], r);
        }

        // Empty bearings borrow from their neighbours, and the whole ring is then eased, so a mould
        // whose silhouette is a rounded box comes back as one rather than as a staircase.
        Fill(reach);
        Smooth(reach, passes: 4);

        var ring = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float angle = (MathF.Tau * i / count) - MathF.PI;
            float r = Sample(reach, angle);
            var flat = centre + (new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * r);
            ring[i] = (u * flat.X) + (v * flat.Y) + (axis * height);
        }

        return ring;
    }

    /// <summary>An angle difference brought back into -pi..pi.</summary>
    private static float Wrap(float angle)
    {
        while (angle > MathF.PI) angle -= MathF.Tau;
        while (angle < -MathF.PI) angle += MathF.Tau;
        return angle;
    }

    private static int Bucket(float angle, int buckets) =>
        Math.Clamp((int)((angle + MathF.PI) / MathF.Tau * buckets), 0, buckets - 1);

    private static float Sample(float[] reach, float angle)
    {
        float at = (angle + MathF.PI) / MathF.Tau * reach.Length;
        int i = ((int)MathF.Floor(at) % reach.Length + reach.Length) % reach.Length;
        int j = (i + 1) % reach.Length;
        float t = at - MathF.Floor(at);
        return (reach[i] * (1f - t)) + (reach[j] * t);
    }

    private static void Fill(float[] reach)
    {
        float last = 0f;
        foreach (float r in reach) if (r > last) last = r;
        for (int i = 0; i < reach.Length; i++) if (reach[i] <= 0f) reach[i] = last;
    }

    private static void Smooth(float[] reach, int passes)
    {
        int n = reach.Length;
        for (int pass = 0; pass < passes; pass++)
        {
            var next = new float[n];
            for (int i = 0; i < n; i++)
                next[i] = (reach[(i - 1 + n) % n] + (2f * reach[i]) + reach[(i + 1) % n]) * 0.25f;
            Array.Copy(next, reach, n);
        }
    }

    /// <summary>
    /// Rings from the parting line out to the mould ring. Each parting point is matched to the ring
    /// point on its own bearing, so the loft runs outward rather than winding round.
    /// </summary>
    private static List<Vector3[]> Loft(
        IReadOnlyList<Vector3> line, Vector3[] outer, Vector3 axis, int rings)
    {
        var (u, v) = Frame(axis);

        var centre = Vector2.Zero;
        foreach (var p in line) centre += Flat(p, u, v);
        centre /= line.Count;

        // The ring is indexed by arc length round the mould, the line by its own walk, so the two are
        // matched by bearing from the shared centre - each parting point runs out on its own heading
        // rather than to whichever ring point happens to share its index.
        var bearings = new float[outer.Length];
        for (int i = 0; i < outer.Length; i++)
        {
            var d = Flat(outer[i], u, v) - centre;
            bearings[i] = MathF.Atan2(d.Y, d.X);
        }

        int n = line.Count;
        var target = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            var d = Flat(line[i], u, v) - centre;
            float angle = MathF.Atan2(d.Y, d.X);

            int best = 0;
            float closest = float.MaxValue;
            for (int k = 0; k < outer.Length; k++)
            {
                float gap = MathF.Abs(Wrap(bearings[k] - angle));
                if (gap >= closest) continue;

                closest = gap;
                best = k;
            }

            target[i] = outer[best];
        }

        var result = new List<Vector3[]>(rings + 1);
        for (int r = 0; r <= rings; r++)
        {
            float s = (float)r / rings;
            float ease = s * s * (3f - (2f * s));      // smoothstep

            var ring = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                // Footprint travels linearly, height on the eased curve: the surface stays where the
                // body put it near the rim and flattens where there is room for it to.
                var a = line[i];
                var b = target[i];

                float ha = Vector3.Dot(a, axis);
                float hb = Vector3.Dot(b, axis);

                var flatA = a - (axis * ha);
                var flatB = b - (axis * hb);

                ring[i] = Vector3.Lerp(flatA, flatB, s) + (axis * ((ha * (1f - ease)) + (hb * ease)));
            }

            result.Add(ring);
        }

        return result;
    }

    private Result<IMesh> Stitch(List<Vector3[]> rings)
    {
        int n = rings[0].Length;
        var vertices = new double[rings.Count * n * 3];
        for (int r = 0; r < rings.Count; r++)
            for (int i = 0; i < n; i++)
            {
                int at = ((r * n) + i) * 3;
                vertices[at] = rings[r][i].X;
                vertices[at + 1] = rings[r][i].Y;
                vertices[at + 2] = rings[r][i].Z;
            }

        var triangles = new List<int>(rings.Count * n * 6);
        for (int r = 0; r + 1 < rings.Count; r++)
            for (int i = 0; i < n; i++)
            {
                int a = (r * n) + i, b = (r * n) + ((i + 1) % n);
                int c = ((r + 1) * n) + i, d = ((r + 1) * n) + ((i + 1) % n);
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(b); triangles.Add(c); triangles.Add(d);
            }

        return _engine.CreateMesh(vertices.AsSpan(), System.Runtime.InteropServices.CollectionsMarshal.AsSpan(triangles));
    }

    private void Report(string label, IMesh mesh, Vector3 axis)
    {
        var v = mesh.Vertices;
        var t = mesh.Triangles;

        var slopes = new List<(float Deg, float Area)>();
        float total = 0f;
        for (int f = 0; f < t.Length; f += 3)
        {
            var cross = Vector3.Cross(v[t[f + 1]] - v[t[f]], v[t[f + 2]] - v[t[f]]);
            float len = cross.Length();
            if (len < 1e-12f) continue;

            float area = len * 0.5f;
            slopes.Add((MathF.Acos(Math.Clamp(MathF.Abs(Vector3.Dot(cross / len, axis)), 0f, 1f)) * 180f / MathF.PI, area));
            total += area;
        }

        slopes.Sort((a, b) => a.Deg.CompareTo(b.Deg));

        float Pct(float share)
        {
            float run = 0f;
            foreach (var (deg, area) in slopes) { run += area; if (run >= total * share) return deg; }
            return slopes[^1].Deg;
        }

        float Past(float limit)
        {
            float run = 0f;
            foreach (var (deg, area) in slopes) if (deg > limit) run += area;
            return run / total * 100f;
        }

        _out.WriteLine(
            $"  {label}: median={Pct(0.5f),5:F1}  p90={Pct(0.9f),5:F1}  max={slopes[^1].Deg,5:F1} deg" +
            $"   area past 40deg={Past(40f),5:F1}%  past 60deg={Past(60f),5:F1}%  tris={t.Length / 3}");
    }

    private static void Draw(
        string directory, string file, IMesh mould, IMesh flange,
        IReadOnlyList<Vector3> line, Vector3[] outer)
    {
        string stem = Path.GetFileNameWithoutExtension(file);
        var options = RenderOptions.Default;

        foreach (var view in new[] { Views.OrbitA, Views.Front, Views.Right })
        {
            // The mould, with the parting line it must split along and the ledge the loft runs out to.
            var camera = Camera.Fit(mould, view, Size, Size);
            var raster = MeshRasterizer.Render(mould, camera, Size, Size, options);
            MeshRasterizer.DrawPolyline(raster, camera, line, true, new Rgb(80, 220, 255), options);
            MeshRasterizer.DrawPolyline(raster, camera, outer, true, new Rgb(255, 170, 60), options);
            raster.Save(Path.Combine(directory, $"{stem}-mould-{view.Name}.png"));

            // The surface the two curves bound, on the same camera so the two read together.
            var surface = MeshRasterizer.Render(flange, camera, Size, Size, options);
            MeshRasterizer.DrawPolyline(surface, camera, line, true, new Rgb(80, 220, 255), options);
            MeshRasterizer.DrawPolyline(surface, camera, outer, true, new Rgb(255, 170, 60), options);
            surface.Save(Path.Combine(directory, $"{stem}-loft-{view.Name}.png"));
        }
    }

    private static (Vector3 U, Vector3 V) Frame(Vector3 axis)
    {
        var seed = MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(seed, axis));
        return (u, Vector3.Cross(axis, u));
    }

    private static Vector2 Flat(Vector3 p, Vector3 u, Vector3 v) =>
        new(Vector3.Dot(p, u), Vector3.Dot(p, v));

    private static string Assets()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "files")))
            dir = dir.Parent;

        return dir is null ? "" : Path.Combine(dir.FullName, "tests", "files");
    }
}
