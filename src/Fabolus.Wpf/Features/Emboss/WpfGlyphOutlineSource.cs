using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using Fabolus.Core.Common;
using Fabolus.Core.Features.Decal;
using Fabolus.Core.Geometry;

namespace Fabolus.Wpf.Features.Decal;

public sealed class WpfGlyphOutlineSource : IGlyphOutlineSource
{
    /// <summary>
    /// Flattening tolerance floor, in millimetres.
    /// </summary>
    private const double MinFlattenTolerance = 0.005;

    /// <summary>
    /// Flattening tolerance as a fraction of cap height. A *relative* tolerance would be a
    /// fraction of the geometry's own extent and so would coarsen as the label gets longer: at
    /// 0.01 across a 50mm run the chord error lands near 0.5mm, which leaves a 2mm bowl on an 'O'
    /// as roughly four segments - visibly a polygon. These outlines are in millimetres, so the
    /// tolerance belongs in millimetres too. Scaling it off the cap height keeps the segment count
    /// per glyph constant instead of letting it drift with text length, and 0.0025 puts a 6mm cap
    /// at ~25 segments around an 'O'.
    /// </summary>
    private const double CapHeightToleranceFactor = 0.0025;
    private const float PointClosureDistanceSquared = 1e-6f;
    private const double DefaultCapsHeightRatio = 0.7;
    private const double MinValidCapsHeight = 0.1;

    /// <summary>
    /// Cache of built outlines. Every preview tick, every decal and every preset hover asks for
    /// outlines, and each miss costs a FormattedText plus BuildGeometry plus a flatten pass per
    /// character. The inputs are a small closed set in practice (a handful of labels at a handful
    /// of sizes), so caching them turns the hot path into a dictionary lookup.
    /// </summary>
    private static readonly Dictionary<GlyphRunKey, IReadOnlyList<Polygon2D>> OutlineCache = [];
    private static readonly Dictionary<GlyphRunKey, TextMetrics> MetricsCache = [];
    private static readonly object CacheLock = new();

    private readonly record struct GlyphRunKey(string Text, DecalFont Font, float CapHeight, float Tracking);

    private static readonly FontFamily SansFontFamily = new("IBM Plex Sans, Segoe UI, Arial, sans-serif");
    private static readonly FontFamily MonoFontFamily = new("IBM Plex Mono, Consolas, Courier New, monospace");
    private static readonly FontFamily BoldFontFamily = new("Segoe UI Black, Impact, Arial Black, sans-serif");

    public Result<IReadOnlyList<Polygon2D>> GetOutlines(string text, DecalFont font, float capHeight, float tracking)
    {
        var key = new GlyphRunKey(text ?? string.Empty, font, capHeight, tracking);
        lock (CacheLock)
        {
            if (OutlineCache.TryGetValue(key, out var cached))
                return Result.Success(cached);
        }

        var result = BuildOutlines(text!, font, capHeight, tracking);
        if (result.IsSuccess)
        {
            lock (CacheLock)
            {
                OutlineCache[key] = result.Value;
            }
        }

        return result;
    }

    public TextMetrics MeasureText(string text, DecalFont font, float capHeight, float tracking)
    {
        var key = new GlyphRunKey(text ?? string.Empty, font, capHeight, tracking);
        lock (CacheLock)
        {
            if (MetricsCache.TryGetValue(key, out var cached))
                return cached;
        }

        var metrics = ComputeMetrics(text!, font, capHeight, tracking);
        lock (CacheLock)
        {
            MetricsCache[key] = metrics;
        }

        return metrics;
    }

    private static (FontFamily Family, FontWeight Weight) ResolveTypeface(DecalFont font) => font switch
    {
        DecalFont.Mono => (MonoFontFamily, FontWeights.SemiBold),
        DecalFont.Bold => (BoldFontFamily, FontWeights.Black),
        _ => (SansFontFamily, FontWeights.SemiBold)
    };

    private static Result<IReadOnlyList<Polygon2D>> BuildOutlines(string text, DecalFont font, float capHeight, float tracking)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Result.Success<IReadOnlyList<Polygon2D>>(Array.Empty<Polygon2D>());

        var (fontFamily, fontWeight) = ResolveTypeface(font);
        var typeface = new Typeface(fontFamily, FontStyles.Normal, fontWeight, FontStretches.Normal);

        double capsHeightRatio = typeface.CapsHeight > MinValidCapsHeight ? typeface.CapsHeight : DefaultCapsHeightRatio;
        double emSize = capHeight / capsHeightRatio;

        var combinedGeometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        double currentX = 0.0;

        for (int i = 0; i < text.Length; i++)
        {
            string ch = text.Substring(i, 1);
            var ft = new FormattedText(
                ch,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                emSize,
                Brushes.Black,
                1.0);

            var charGeo = ft.BuildGeometry(new Point(0, 0));
            if (charGeo is not null && !charGeo.IsEmpty())
            {
                var transform = new TranslateTransform(currentX, 0);
                var transformedGeo = charGeo.Clone();
                transformedGeo.Transform = transform;
                combinedGeometry.Children.Add(transformedGeo);
            }

            // Tracking sits between glyphs, not after the last one, matching MeasureText.
            double adv = ft.WidthIncludingTrailingWhitespace;
            if (i < text.Length - 1)
                adv += tracking;

            currentX += adv;
        }

        double flattenTolerance = Math.Max(MinFlattenTolerance, capHeight * CapHeightToleranceFactor);
        var flattened = combinedGeometry.GetFlattenedPathGeometry(flattenTolerance, ToleranceType.Absolute);
        if (flattened is null || flattened.Figures.Count == 0)
            return Result.Success<IReadOnlyList<Polygon2D>>(Array.Empty<Polygon2D>());

        var bounds = flattened.Bounds;
        double centerX = (bounds.Left + bounds.Right) / 2.0;
        double centerY = (bounds.Top + bounds.Bottom) / 2.0;

        // Extract raw loops in Cartesian local coordinates (+U right, +V up)
        var rawLoops = new List<List<Vector2>>();
        foreach (PathFigure figure in flattened.Figures)
        {
            var points = new List<Vector2>
            {
                new Vector2((float)(figure.StartPoint.X - centerX), -(float)(figure.StartPoint.Y - centerY))
            };

            foreach (PathSegment segment in figure.Segments)
            {
                if (segment is PolyLineSegment polyLine)
                {
                    foreach (var pt in polyLine.Points)
                        points.Add(new Vector2((float)(pt.X - centerX), -(float)(pt.Y - centerY)));
                }
                else if (segment is LineSegment line)
                {
                    points.Add(new Vector2((float)(line.Point.X - centerX), -(float)(line.Point.Y - centerY)));
                }
            }

            if (points.Count > 1 && Vector2.DistanceSquared(points[0], points[^1]) < PointClosureDistanceSquared)
                points.RemoveAt(points.Count - 1);

            if (points.Count >= 3)
                rawLoops.Add(points);
        }

        if (rawLoops.Count == 0)
            return Result.Success<IReadOnlyList<Polygon2D>>(Array.Empty<Polygon2D>());

        return Result.Success(OrganizeIntoPolygons(rawLoops));
    }

    private static TextMetrics ComputeMetrics(string text, DecalFont font, float capHeight, float tracking)
    {
        if (string.IsNullOrEmpty(text))
            return TextMetrics.Empty;

        var (fontFamily, fontWeight) = ResolveTypeface(font);
        var typeface = new Typeface(fontFamily, FontStyles.Normal, fontWeight, FontStretches.Normal);

        double capsHeightRatio = typeface.CapsHeight > MinValidCapsHeight ? typeface.CapsHeight : DefaultCapsHeightRatio;
        double emSize = capHeight / capsHeightRatio;

        double totalWidth = 0.0;
        var advances = new List<float>(text.Length);

        for (int i = 0; i < text.Length; i++)
        {
            string ch = text.Substring(i, 1);
            var ft = new FormattedText(
                ch,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                emSize,
                Brushes.Black,
                1.0);

            double adv = ft.WidthIncludingTrailingWhitespace;
            if (i < text.Length - 1)
                adv += tracking;

            advances.Add((float)adv);
            totalWidth += adv;
        }

        return new TextMetrics((float)totalWidth, capHeight, advances);
    }

    private static IReadOnlyList<Polygon2D> OrganizeIntoPolygons(List<List<Vector2>> loops)
    {
        int n = loops.Count;
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = -1;

        for (int i = 0; i < n; i++)
        {
            var testPt = loops[i][0];
            int bestContainer = -1;
            float smallestArea = float.MaxValue;

            for (int j = 0; j < n; j++)
            {
                if (i == j) continue;
                if (IsPointInsidePolygon(testPt, loops[j]))
                {
                    float area = Math.Abs(ComputeSignedArea(loops[j]));
                    if (area < smallestArea)
                    {
                        smallestArea = area;
                        bestContainer = j;
                    }
                }
            }

            parent[i] = bestContainer;
        }

        var polygons = new List<Polygon2D>();

        for (int i = 0; i < n; i++)
        {
            int depth = 0;
            int curr = parent[i];
            while (curr != -1)
            {
                depth++;
                curr = parent[curr];
            }

            if (depth % 2 == 0)
            {
                var outer = loops[i];
                if (ComputeSignedArea(outer) < 0)
                    outer.Reverse();

                var holes = new List<IReadOnlyList<Vector2>>();
                for (int j = 0; j < n; j++)
                {
                    if (parent[j] == i)
                    {
                        var hole = loops[j];
                        if (ComputeSignedArea(hole) > 0)
                            hole.Reverse();
                        holes.Add(hole);
                    }
                }

                polygons.Add(new Polygon2D
                {
                    OuterBoundary = outer,
                    Holes = holes
                });
            }
        }

        return polygons;
    }

    private static float ComputeSignedArea(List<Vector2> ring)
    {
        float area = 0f;
        for (int i = 0; i < ring.Count; i++)
        {
            var p1 = ring[i];
            var p2 = ring[(i + 1) % ring.Count];
            area += (p1.X * p2.Y - p2.X * p1.Y);
        }
        return area * 0.5f;
    }

    private static bool IsPointInsidePolygon(Vector2 point, List<Vector2> ring)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            if (((ring[i].Y > point.Y) != (ring[j].Y > point.Y)) &&
                (point.X < (ring[j].X - ring[i].X) * (point.Y - ring[i].Y) / (ring[j].Y - ring[i].Y) + ring[i].X))
            {
                inside = !inside;
            }
        }
        return inside;
    }
}
