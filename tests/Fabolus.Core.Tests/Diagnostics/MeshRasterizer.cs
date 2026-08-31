using System.Numerics;
using Fabolus.Core.Geometry;

namespace Fabolus.Tests.Diagnostics;

/// <summary>A named direction the camera looks along.</summary>
internal readonly record struct View(string Name, Vector3 Look);

internal static class Views
{
    public static readonly View Front = new("front", new Vector3(0, 0, 1));
    public static readonly View Back = new("back", new Vector3(0, 0, -1));
    public static readonly View Left = new("left", new Vector3(1, 0, 0));
    public static readonly View Right = new("right", new Vector3(-1, 0, 0));
    public static readonly View Top = new("top", new Vector3(0, -1, 0));
    public static readonly View Bottom = new("bottom", new Vector3(0, 1, 0));
    public static readonly View OrbitA = new("orbit-a", Vector3.Normalize(new Vector3(-0.8f, -0.5f, 0.8f)));
    public static readonly View OrbitB = new("orbit-b", Vector3.Normalize(new Vector3(0.8f, -0.5f, -0.8f)));

    public static IReadOnlyList<View> Standard { get; } =
        new[] { Front, Back, Left, Right, Top, Bottom, OrbitA, OrbitB };
}

/// <summary>
/// Orthographic camera fitted to a mesh. The fit runs over every vertex rather than the eight bounding
/// corners: it costs one pass and gives a tighter, consistent framing across views, which matters when
/// eight images of the same body are read side by side.
/// </summary>
internal sealed class Camera
{
    public Vector3 Right { get; private init; }
    public Vector3 Up { get; private init; }
    public Vector3 Forward { get; private init; }
    public Vector3 Centre { get; private init; }
    public float Scale { get; private init; }
    public float Diagonal { get; private init; }

    private float _fitX;
    private float _fitY;
    private int _width;
    private int _height;

    public static Camera Fit(IMesh mesh, View view, int width, int height, float margin = 0.92f)
    {
        var forward = Vector3.Normalize(view.Look);

        // Y is the app's pull/up axis, so seed with it unless the camera is looking straight down it.
        var seed = MathF.Abs(Vector3.Dot(forward, Vector3.UnitY)) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;
        var right = Vector3.Normalize(Vector3.Cross(seed, forward));
        var up = Vector3.Cross(forward, right);

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var vertex in mesh.Vertices)
        {
            min = Vector3.Min(min, vertex);
            max = Vector3.Max(max, vertex);
        }

        var centre = (min + max) * 0.5f;
        float diagonal = Vector3.Distance(min, max);

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var vertex in mesh.Vertices)
        {
            var local = vertex - centre;
            float x = Vector3.Dot(local, right);
            float y = Vector3.Dot(local, up);
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        float spanX = MathF.Max(maxX - minX, 1e-6f);
        float spanY = MathF.Max(maxY - minY, 1e-6f);

        return new Camera
        {
            Right = right,
            Up = up,
            Forward = forward,
            Centre = centre,
            Diagonal = diagonal,
            Scale = margin * MathF.Min(width / spanX, height / spanY),
            _fitX = (minX + maxX) * 0.5f,
            _fitY = (minY + maxY) * 0.5f,
            _width = width,
            _height = height,
        };
    }

    /// <summary>
    /// The same camera framed on a patch of the body rather than the whole of it, so a defect a few
    /// millimetres across can be looked at. Built from <see cref="Fit"/> so the basis and the depth
    /// bias are identical and a close-up can be read against the wide shot it came from.
    /// </summary>
    /// <param name="radius">Half the width of the framed patch, in model units.</param>
    public static Camera Focus(
        IMesh mesh, View view, Vector3 at, float radius, int width, int height)
    {
        var fitted = Fit(mesh, view, width, height);
        var local = at - fitted.Centre;

        return new Camera
        {
            Right = fitted.Right,
            Up = fitted.Up,
            Forward = fitted.Forward,
            Centre = fitted.Centre,
            Diagonal = fitted.Diagonal,
            Scale = MathF.Min(width, height) * 0.5f / MathF.Max(radius, 1e-4f),
            _fitX = Vector3.Dot(local, fitted.Right),
            _fitY = Vector3.Dot(local, fitted.Up),
            _width = width,
            _height = height,
        };
    }

    /// <summary>Returns (pixelX, pixelY, viewDepth). Depth grows away from the camera.</summary>
    public Vector3 Project(Vector3 world)
    {
        var local = world - Centre;
        float x = Vector3.Dot(local, Right);
        float y = Vector3.Dot(local, Up);
        float z = Vector3.Dot(local, Forward);

        return new Vector3(
            (_width * 0.5f) + ((x - _fitX) * Scale),
            (_height * 0.5f) - ((y - _fitY) * Scale),   // image rows grow downward
            z);
    }
}

internal sealed record RenderOptions
{
    public Rgb Background { get; init; } = new(24, 26, 32);
    public Rgb DefaultFace { get; init; } = new(190, 193, 200);
    public float Ambient { get; init; } = 0.14f;

    /// <summary>
    /// Contours are lifted only 0.002 x diagonal along the vertex normal, which projects to nearly
    /// nothing along the view axis at grazing angles - without a view-space bias they z-fight with the
    /// surface they describe and come out stippled. 0.005 wins reliably and is still far short of a
    /// shell wall, so a line never punches through to the far side.
    /// </summary>
    public float DepthBiasFraction { get; init; } = 0.005f;

    public float LineWidthPx { get; init; } = 2.6f;

    /// <summary>Draw the hidden run of a contour dimmed, so a rim can be checked for going all the
    /// way round without turning the model over.</summary>
    public bool DrawOccludedLines { get; init; } = true;

    public float OccludedAlpha { get; init; } = 0.30f;

    public static RenderOptions Default { get; } = new();
}

internal static class MeshRasterizer
{
    /// <summary>
    /// Fills the mesh into a fresh raster. Nothing is back-face culled: the sign of twice the signed
    /// area is folded into the reciprocal, so coverage is winding-agnostic. A body with locally flipped
    /// winding has to render as geometry rather than as holes, or a shading bug reads as a geometry bug.
    /// Shading is two-sided for the same reason, and the depth buffer hides the interior anyway.
    /// </summary>
    public static Raster Render(
        IMesh mesh, Camera camera, int width, int height,
        RenderOptions options, Func<int, Rgb>? faceColour = null)
    {
        var raster = new Raster(width, height, options.Background);

        var vertices = mesh.Vertices;
        var triangles = mesh.Triangles;
        int faceCount = triangles.Length / 3;

        var projected = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++) projected[i] = camera.Project(vertices[i]);

        // An off-axis key. A pure headlight flattens a bolus into a featureless blob; the rim term is
        // what keeps the silhouette legible against a dark background.
        var key = Vector3.Normalize(-camera.Forward + (0.35f * camera.Right) + (0.45f * camera.Up));

        for (int face = 0; face < faceCount; face++)
        {
            var a = projected[triangles[face * 3]];
            var b = projected[triangles[(face * 3) + 1]];
            var c = projected[triangles[(face * 3) + 2]];

            float area2 = ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
            if (MathF.Abs(area2) < 1e-9f) continue;   // edge-on sliver
            float inv = 1f / area2;

            var normal = FaceNormal(
                vertices[triangles[face * 3]],
                vertices[triangles[(face * 3) + 1]],
                vertices[triangles[(face * 3) + 2]]);
            if (normal == Vector3.Zero) continue;
            if (Vector3.Dot(normal, camera.Forward) > 0f) normal = -normal;

            float lambert = MathF.Max(0f, Vector3.Dot(normal, key));
            float fillLight = MathF.Max(0f, Vector3.Dot(normal, -key)) * 0.20f;
            float facing = MathF.Min(1f, MathF.Abs(Vector3.Dot(normal, camera.Forward)));
            float rim = MathF.Pow(1f - facing, 3f) * 0.25f;
            float intensity = Math.Clamp(options.Ambient + (0.80f * lambert) + fillLight + rim, 0f, 1f);

            var tint = faceColour?.Invoke(face) ?? options.DefaultFace;
            var shaded = tint.Scale(MathF.Pow(intensity, 1f / 1.35f));

            int x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))));
            int x1 = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))));
            int y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))));
            int y1 = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))));

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f;
                    float py = y + 0.5f;

                    float w0 = Edge(b, c, px, py) * inv;
                    if (w0 < 0f) continue;
                    float w1 = Edge(c, a, px, py) * inv;
                    if (w1 < 0f) continue;
                    float w2 = 1f - w0 - w1;
                    if (w2 < 0f) continue;

                    // Orthographic depth is affine in screen space, so this is exact.
                    float z = (w0 * a.Z) + (w1 * b.Z) + (w2 * c.Z);
                    int index = (y * width) + x;
                    if (z >= raster.Depth[index]) continue;

                    raster.Depth[index] = z;
                    raster.Set(x, y, shaded);
                }
            }
        }

        return raster;
    }

    /// <summary>
    /// Draws a 3D polyline with depth testing. Visible pixels write depth, so contours that cross each
    /// other occlude correctly and a curve doubling back on itself is visible as such.
    /// </summary>
    public static void DrawPolyline(
        Raster raster, Camera camera, IReadOnlyList<Vector3> points, bool closed,
        Rgb colour, RenderOptions options)
    {
        if (points.Count < 2) return;

        float bias = options.DepthBiasFraction * camera.Diagonal;
        int segments = closed ? points.Count : points.Count - 1;

        for (int i = 0; i < segments; i++)
        {
            var p = camera.Project(points[i]);
            var q = camera.Project(points[(i + 1) % points.Count]);

            int steps = (int)MathF.Ceiling(MathF.Max(MathF.Abs(q.X - p.X), MathF.Abs(q.Y - p.Y)));
            steps = Math.Max(steps, 1);

            for (int s = 0; s <= steps; s++)
            {
                float t = (float)s / steps;
                Stamp(raster, camera,
                    p.X + ((q.X - p.X) * t),
                    p.Y + ((q.Y - p.Y) * t),
                    p.Z + ((q.Z - p.Z) * t) - bias,
                    colour, options);
            }
        }
    }

    /// <summary>
    /// As above, but each segment takes its own colour. Used to paint a measurement along the curve it
    /// was taken on, which is the only way to see <em>where</em> a quantity varies rather than just
    /// that it does.
    /// </summary>
    public static void DrawPolyline(
        Raster raster, Camera camera, IReadOnlyList<Vector3> points, bool closed,
        Func<int, Rgb> colourAt, RenderOptions options)
    {
        if (points.Count < 2) return;

        float bias = options.DepthBiasFraction * camera.Diagonal;
        int segments = closed ? points.Count : points.Count - 1;

        for (int i = 0; i < segments; i++)
        {
            var p = camera.Project(points[i]);
            var q = camera.Project(points[(i + 1) % points.Count]);
            var colour = colourAt(i);

            int steps = (int)MathF.Ceiling(MathF.Max(MathF.Abs(q.X - p.X), MathF.Abs(q.Y - p.Y)));
            steps = Math.Max(steps, 1);

            for (int s = 0; s <= steps; s++)
            {
                float t = (float)s / steps;
                Stamp(raster, camera,
                    p.X + ((q.X - p.X) * t),
                    p.Y + ((q.Y - p.Y) * t),
                    p.Z + ((q.Z - p.Z) * t) - bias,
                    colour, options);
            }
        }
    }

    /// <summary>A filled square, for marking the endpoints of an open contour - the fastest way to see
    /// where a ridge stopped.</summary>
    public static void DrawMarker(
        Raster raster, Camera camera, Vector3 point, Rgb colour, RenderOptions options, int radius = 4)
    {
        var p = camera.Project(point);
        float bias = options.DepthBiasFraction * camera.Diagonal * 2f;
        int cx = (int)MathF.Round(p.X);
        int cy = (int)MathF.Round(p.Y);

        for (int y = cy - radius; y <= cy + radius; y++)
            for (int x = cx - radius; x <= cx + radius; x++)
                Write(raster, x, y, p.Z - bias, colour, options);
    }

    private static void Stamp(
        Raster raster, Camera camera, float x, float y, float z, Rgb colour, RenderOptions options)
    {
        int radius = Math.Max(0, (int)MathF.Round(options.LineWidthPx * 0.5f));
        int cx = (int)MathF.Round(x);
        int cy = (int)MathF.Round(y);

        for (int py = cy - radius; py <= cy + radius; py++)
        {
            for (int px = cx - radius; px <= cx + radius; px++)
            {
                int dx = px - cx;
                int dy = py - cy;
                if ((dx * dx) + (dy * dy) > radius * radius + radius) continue;
                Write(raster, px, py, z, colour, options);
            }
        }
    }

    private static void Write(Raster raster, int x, int y, float z, Rgb colour, RenderOptions options)
    {
        if (!raster.Contains(x, y)) return;

        int index = (y * raster.Width) + x;
        if (z < raster.Depth[index])
        {
            raster.Depth[index] = z;
            raster.Set(x, y, colour);
        }
        else if (options.DrawOccludedLines)
        {
            // No depth write: a hidden line must not occlude anything, including itself.
            raster.Blend(x, y, colour, options.OccludedAlpha);
        }
    }

    private static float Edge(Vector3 a, Vector3 b, float px, float py) =>
        ((b.X - a.X) * (py - a.Y)) - ((b.Y - a.Y) * (px - a.X));

    private static Vector3 FaceNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        var normal = Vector3.Cross(b - a, c - a);
        float length = normal.Length();
        return length < 1e-12f ? Vector3.Zero : normal / length;
    }

    /// <summary>A palette that stays distinguishable at contour-line width against a dark ground.</summary>
    public static Rgb ContourColour(int index) => Palette[index % Palette.Length];

    private static readonly Rgb[] Palette =
    {
        new(198, 76, 255),   // the app's own ridge purple, first
        new(255, 214, 64),
        new(64, 224, 208),
        new(255, 105, 140),
        new(126, 255, 108),
        new(120, 170, 255),
        new(255, 150, 60),
        new(230, 230, 230),
    };
}
