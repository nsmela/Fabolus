using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using Fabolus.Core.Features.Emboss;
using Fabolus.Core.Geometry;

namespace Fabolus.Wpf.Features.Emboss;

public sealed class WpfGlyphOutlineSource : IGlyphOutlineSource
{
    private static readonly FontFamily SansFontFamily = new("IBM Plex Sans, Segoe UI, Arial, sans-serif");
    private static readonly FontFamily MonoFontFamily = new("IBM Plex Mono, Consolas, Courier New, monospace");

    public IReadOnlyList<Polygon2D> GetOutlines(string text, DecalFont font, float capHeight, float tracking)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<Polygon2D>();

        var fontFamily = font == DecalFont.Mono ? MonoFontFamily : SansFontFamily;
        var typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        double capsHeightRatio = typeface.CapsHeight > 0.1 ? typeface.CapsHeight : 0.7;
        double emSize = capHeight / capsHeightRatio;

        var combinedGeometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        double currentX = 0.0;
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

            var charGeo = ft.BuildGeometry(new Point(0, 0));
            if (charGeo != null && !charGeo.IsEmpty())
            {
                var transform = new TranslateTransform(currentX, 0);
                var transformedGeo = charGeo.Clone();
                transformedGeo.Transform = transform;
                combinedGeometry.Children.Add(transformedGeo);
            }

            double adv = ft.WidthIncludingTrailingWhitespace + tracking;
            advances.Add((float)adv);
            currentX += adv;
        }

        var flattened = combinedGeometry.GetFlattenedPathGeometry(0.01, ToleranceType.Relative);
        if (flattened == null || flattened.Figures.Count == 0)
            return Array.Empty<Polygon2D>();

        var bounds = flattened.Bounds;
        double centerX = (bounds.Left + bounds.Right) / 2.0;
        double centerY = (bounds.Top + bounds.Bottom) / 2.0;

        // Extract raw loops in Cartesian local coordinates (+U right, +V up)
        var rawLoops = new List<List<Vector2>>();
        foreach (PathFigure figure in flattened.Figures)
        {
            var points = new List<Vector2>();
            points.Add(new Vector2((float)(figure.StartPoint.X - centerX), -(float)(figure.StartPoint.Y - centerY)));

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

            if (points.Count > 1 && Vector2.DistanceSquared(points[0], points[^1]) < 1e-6f)
                points.RemoveAt(points.Count - 1);

            if (points.Count >= 3)
                rawLoops.Add(points);
        }

        if (rawLoops.Count == 0)
            return Array.Empty<Polygon2D>();

        return OrganizeIntoPolygons(rawLoops);
    }

    public TextMetrics MeasureText(string text, DecalFont font, float capHeight, float tracking)
    {
        if (string.IsNullOrEmpty(text))
            return TextMetrics.Empty;

        var fontFamily = font == DecalFont.Mono ? MonoFontFamily : SansFontFamily;
        var typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        double capsHeightRatio = typeface.CapsHeight > 0.1 ? typeface.CapsHeight : 0.7;
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
        // Compute nesting levels by testing point containment
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
            // Even nesting depth = Outer boundary
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
                    outer.Reverse(); // Ensure CCW outer

                // Find holes (immediate children with odd depth)
                var holes = new List<IReadOnlyList<Vector2>>();
                for (int j = 0; j < n; j++)
                {
                    if (parent[j] == i)
                    {
                        var hole = loops[j];
                        if (ComputeSignedArea(hole) > 0)
                            hole.Reverse(); // Ensure CW hole
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
