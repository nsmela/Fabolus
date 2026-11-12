using BasicResults;
using Fabolus.Core.Geometry;
using System.Collections.Immutable;

namespace Fabolus.Core.Features.PartingSplit;

/// <summary>
/// How much of a flange's mating face stands in the way of the halves coming apart along the axis.
/// </summary>
/// <param name="MatingAreaMm2">The flange outside the body and inside the mould: what the halves meet on.</param>
/// <param name="TurnedOverMm2">Mating area whose face has turned past vertical against the rest of its sheet.</param>
/// <param name="DoubledOverMm2">Mating area, seen along the axis, that the flange lies over twice.</param>
/// <param name="Suspect">Per face of the flange: turned over, or over a doubled stretch.</param>
public sealed record FlangeSeparability(
    double MatingAreaMm2, double TurnedOverMm2, double DoubledOverMm2, IReadOnlyList<bool> Suspect)
{
    /// <summary>Whether a line parallel to the axis meets the mating face once at most.</summary>
    public bool IsSingleValued =>
        TurnedOverMm2 < PartingSeparability.LeastAreaMm2 && DoubledOverMm2 < PartingSeparability.LeastAreaMm2;
}

/// <summary>What sliding one half away from the other along the axis found.</summary>
/// <param name="WorstOverlapMm3">The largest volume the two shared at any distance tried.</param>
/// <param name="AtMm">The distance it was found at.</param>
/// <param name="Along">+1 or -1: which way along the axis the larger piece leaves.</param>
/// <param name="Overlap">The shared volume itself, slid back to where the halves sit; null when there is none.</param>
public sealed record PullApart(double WorstOverlapMm3, double AtMm, double Along, IMesh? Overlap)
{
    public bool Parts => WorstOverlapMm3 < PartingSeparability.LeastOverlapMm3;
}

/// <summary>
/// Whether a parting comes apart: measured on the flange, where a fold shows before anything is cut,
/// and on the finished halves, which is the answer that counts.
///
/// <para>
/// The two halves of a mould leave each other along one axis. That works exactly when the surface they
/// meet on is a height field over the plane across that axis - one height at each point of the plane.
/// A flange that climbs past vertical and comes back over itself is not one: the half underneath is
/// hooked by the half above, and they lock. Every sweep can produce that. The loft folds where the
/// line's footprint steps backwards, the marching sweep where the body's normals fan apart, and the
/// skin bisector never consults the axis at all.
/// </para>
///
/// <para>
/// Both measurements are kept because they fail differently. The flange test is cheap and says where,
/// but it reads the flange, and what leaves the boolean is not always what went in. The pull test
/// reads the result and cannot be argued with, but costs a boolean per distance and says little about
/// where. A flange can also fold harmlessly in a sliver the cut never reaches, which only the pull
/// test can tell from a fold that locks.
/// </para>
/// </summary>
public static class PartingSeparability
{
    /// <summary>
    /// How far past vertical a face has to lean before it counts as turned over. A face exactly edge-on
    /// to the axis has no side to speak of, and rounding would hand it one at random.
    /// </summary>
    public const double EdgeOnCos = 0.02;

    /// <summary>Cell of the grid the flange is sampled on, looking along the axis.</summary>
    public const double GridMm = 0.5;

    /// <summary>
    /// How far apart along the axis two sheets over one cell must be to count as two. Above the
    /// cutter's own thickness, so the two faces either side of a shared edge never do.
    /// </summary>
    public const double TwoSheetsMm = 0.3;

    /// <summary>
    /// Turned-over or doubled-over area below this is not reported. A couple of cells of the grid:
    /// less than that is one sliver face on a seam, which every sweep has somewhere and which hooks
    /// nothing.
    /// </summary>
    public const double LeastAreaMm2 = 1.0;

    /// <summary>
    /// Overlap below this is not reported. The cutter leaves a 0.1 mm gap, so two halves that part
    /// cleanly share nothing at all; this is the allowance for a boolean's own noise on meshes tens of
    /// thousands of triangles each, set an order below the smallest overlap measured on a flange that
    /// visibly hooks (27 mm³).
    /// </summary>
    public const double LeastOverlapMm3 = 1.0;

    /// <summary>
    /// The distances the halves are slid apart to. Near ones catch a lip; far ones catch a piece that
    /// clears its neighbour and then meets something further up.
    /// </summary>
    private static readonly double[] PullDistancesMm = [0.5, 2.0, 5.0, 15.0, 40.0];

    /// <summary>
    /// Measures the flange's mating face against <paramref name="axis"/>.
    /// </summary>
    /// <param name="mould">
    /// What bounds the mating face on the outside. Without one the whole flange outside the body is
    /// measured, which counts the stretch that runs on past the mould for nothing.
    /// </param>
    public static Result<FlangeSeparability> OfFlange(
        IGeometryEngine engine, IMesh flange, IMesh body, IMesh? mould, Vector3 axis)
    {
        if (engine is null || flange is null || body is null) return MeshErrors.NullSource;
        if (flange.IsEmpty || body.IsEmpty) return MeshErrors.InvalidMesh;
        if (axis == Vector3.Zero) return MeshErrors.InvalidPullDirection;

        axis = axis.Normalize();
        var vs = flange.Vertices;
        var ts = flange.Triangles;
        int faces = ts.Length / 3;

        var bodyIndex = engine.Spatial.IndexFor(body);
        if (bodyIndex.IsFailure) return bodyIndex.Error;

        var area = new double[faces];
        var cos = new double[faces];
        var centres = ImmutableArray.CreateBuilder<Vector3>(faces);
        for (int f = 0; f < faces; f++)
        {
            var a = vs[ts[3 * f]];
            var b = vs[ts[(3 * f) + 1]];
            var c = vs[ts[(3 * f) + 2]];
            centres.Add((a + b + c) / 3.0);

            var cross = (b - a).Cross(c - a);
            double length = cross.Length;
            if (length < 1e-12) continue;

            area[f] = length * 0.5;
            cos[f] = (cross / length).Dot(axis);
        }

        var at = centres.MoveToImmutable();
        var toBody = bodyIndex.Value.SignedDistances(at);

        ImmutableArray<double> toMould = default;
        if (mould is { IsEmpty: false })
        {
            var mouldIndex = engine.Spatial.IndexFor(mould);
            if (mouldIndex.IsFailure) return mouldIndex.Error;
            toMould = mouldIndex.Value.SignedDistances(at);
        }

        var mating = new bool[faces];
        for (int f = 0; f < faces; f++)
            mating[f] = area[f] > 0.0 && toBody[f] > 0.0 && (toMould.IsDefault || toMould[f] < 0.0);

        // Which way is "with the axis" is settled a sheet at a time. A flange with a hole patch is
        // several sheets laid side by side and nothing winds them the same way round, so read against
        // one sign for the whole mesh a patch wound the other way is all turned over while lying flat.
        // Within one sheet the winding is continuous, and there a face that disagrees with the rest
        // really has gone past vertical.
        var sheet = Enumerable.Range(0, faces).ToArray();
        int Root(int x)
        {
            while (sheet[x] != x) x = sheet[x] = sheet[sheet[x]];
            return x;
        }

        var firstOn = new Dictionary<long, int>(faces * 2);
        for (int f = 0; f < faces; f++)
            for (int k = 0; k < 3; k++)
            {
                int a = ts[(3 * f) + k], b = ts[(3 * f) + ((k + 1) % 3)];
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (firstOn.TryGetValue(key, out int other)) sheet[Root(f)] = Root(other);
                else firstOn[key] = f;
            }

        var facing = new double[faces];
        for (int f = 0; f < faces; f++)
            if (mating[f]) facing[Root(f)] += area[f] * cos[f];

        var suspect = new bool[faces];
        double matingArea = 0.0, turnedOver = 0.0;
        for (int f = 0; f < faces; f++)
        {
            if (!mating[f]) continue;
            matingArea += area[f];

            double with = (facing[Root(f)] >= 0.0 ? 1.0 : -1.0) * cos[f];
            if (with >= -EdgeOnCos) continue;

            turnedOver += area[f];
            suspect[f] = true;
        }

        double doubled = DoubledOver(vs, ts, mating, cos, axis, suspect);
        return new FlangeSeparability(matingArea, turnedOver, doubled, suspect);
    }

    /// <summary>
    /// The mating area a line along the axis meets twice. Each face is sampled onto a grid in the plane
    /// across the axis, every cell keeping the lowest and the highest sheet over it; a cell where the
    /// two stand apart has the flange over it twice. Faces over such a cell are marked in
    /// <paramref name="suspect"/>.
    /// </summary>
    private static double DoubledOver(
        ImmutableArray<Vector3> vs, ImmutableArray<int> ts, bool[] mating, double[] cos, Vector3 axis, bool[] suspect)
    {
        var seed = Math.Abs(axis.Dot(Vector3.UnitY)) < 0.9 ? Vector3.UnitY : Vector3.UnitX;
        var u = seed.Cross(axis).Normalize();
        var v = axis.Cross(u);

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (int f = 0; f < mating.Length; f++)
        {
            if (!mating[f]) continue;
            for (int k = 0; k < 3; k++)
            {
                var p = vs[ts[(3 * f) + k]];
                minX = Math.Min(minX, p.Dot(u)); maxX = Math.Max(maxX, p.Dot(u));
                minY = Math.Min(minY, p.Dot(v)); maxY = Math.Max(maxY, p.Dot(v));
            }
        }

        if (minX > maxX) return 0.0;

        int nx = (int)Math.Ceiling((maxX - minX) / GridMm) + 1;
        int ny = (int)Math.Ceiling((maxY - minY) / GridMm) + 1;
        var low = new float[nx * ny];
        var high = new float[nx * ny];
        Array.Fill(low, float.PositiveInfinity);
        Array.Fill(high, float.NegativeInfinity);

        void Sample(int f, Action<int, double> visit)
        {
            // A face edge-on to the axis covers no cell: it is a wall, not a second sheet.
            if (Math.Abs(cos[f]) < EdgeOnCos) return;

            var a = vs[ts[3 * f]];
            var b = vs[ts[(3 * f) + 1]];
            var c = vs[ts[(3 * f) + 2]];
            double ax = a.Dot(u), ay = a.Dot(v), bx = b.Dot(u), by = b.Dot(v), cx = c.Dot(u), cy = c.Dot(v);
            double area2 = ((bx - ax) * (cy - ay)) - ((by - ay) * (cx - ax));
            if (Math.Abs(area2) < 1e-12) return;

            int x0 = Math.Max(0, (int)Math.Floor((Math.Min(ax, Math.Min(bx, cx)) - minX) / GridMm));
            int x1 = Math.Min(nx - 1, (int)Math.Ceiling((Math.Max(ax, Math.Max(bx, cx)) - minX) / GridMm));
            int y0 = Math.Max(0, (int)Math.Floor((Math.Min(ay, Math.Min(by, cy)) - minY) / GridMm));
            int y1 = Math.Min(ny - 1, (int)Math.Ceiling((Math.Max(ay, Math.Max(by, cy)) - minY) / GridMm));

            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    double px = minX + (x * GridMm), py = minY + (y * GridMm);
                    double w0 = (((bx - px) * (cy - py)) - ((by - py) * (cx - px))) / area2;
                    double w1 = (((cx - px) * (ay - py)) - ((cy - py) * (ax - px))) / area2;
                    double w2 = 1.0 - w0 - w1;
                    if (w0 < 0.0 || w1 < 0.0 || w2 < 0.0) continue;

                    visit((y * nx) + x, (w0 * a.Dot(axis)) + (w1 * b.Dot(axis)) + (w2 * c.Dot(axis)));
                }
        }

        for (int f = 0; f < mating.Length; f++)
            if (mating[f])
                Sample(f, (cell, height) =>
                {
                    low[cell] = Math.Min(low[cell], (float)height);
                    high[cell] = Math.Max(high[cell], (float)height);
                });

        int doubled = 0;
        for (int i = 0; i < low.Length; i++)
            if (!float.IsPositiveInfinity(low[i]) && high[i] - low[i] > TwoSheetsMm) doubled++;

        if (doubled > 0)
            for (int f = 0; f < mating.Length; f++)
            {
                if (!mating[f]) continue;
                int face = f;
                Sample(f, (cell, _) => { if (high[cell] - low[cell] > TwoSheetsMm) suspect[face] = true; });
            }

        return doubled * GridMm * GridMm;
    }

    /// <summary>
    /// Slides the first of <paramref name="pieces"/> away from the rest along <paramref name="axis"/>
    /// and intersects them at each of several distances. Halves that part cleanly share nothing at any
    /// of them.
    ///
    /// <para>
    /// Which way is "away" is not something the pieces say. Both ways are tried at one distance and the
    /// one that overlaps less is followed: the wrong way drives one half straight into the other and
    /// overlaps by its whole footprint, so the two are never close.
    /// </para>
    /// </summary>
    public static Result<PullApart> OfHalves(IGeometryEngine engine, IReadOnlyList<IMesh> pieces, Vector3 axis)
    {
        if (engine is null || pieces is null) return MeshErrors.NullSource;
        if (pieces.Count < 2) return MeshErrors.SplitProducedSinglePiece;
        if (axis == Vector3.Zero) return MeshErrors.InvalidPullDirection;

        axis = axis.Normalize();
        var moving = pieces[0];
        var rest = pieces.Count == 2 ? Result.Success(pieces[1]) : Join(engine, pieces.Skip(1).ToList());
        if (rest.IsFailure) return rest.Error;

        Result<(double Volume, IMesh? Shared)> Overlap(double distance)
        {
            var moved = engine.Transforms.Translate(moving, axis * distance);
            if (moved.IsFailure) return moved.Error;

            var shared = engine.Booleans.Intersect(moved.Value, rest.Value);
            if (shared.IsFailure) return shared.Error;
            (double Volume, IMesh? Shared) found = (0.0, null);
            if (shared.Value.IsEmpty) return found;

            // Slid back by the pull, so it lies on the piece left behind where the fouling starts.
            var back = engine.Transforms.Translate(shared.Value, axis * -distance);
            found = (PartingPieces.Volume(shared.Value), back.IsSuccess ? back.Value : shared.Value);
            return found;
        }

        var up = Overlap(1.0);
        if (up.IsFailure) return up.Error;
        var down = Overlap(-1.0);
        if (down.IsFailure) return down.Error;

        double along = up.Value.Volume <= down.Value.Volume ? 1.0 : -1.0;

        double worst = 0.0, at = 0.0;
        IMesh? overlap = null;
        foreach (double distance in PullDistancesMm)
        {
            var shared = Overlap(along * distance);
            if (shared.IsFailure) return shared.Error;
            if (shared.Value.Volume <= worst) continue;

            worst = shared.Value.Volume;
            at = distance;
            overlap = shared.Value.Shared;
        }

        return new PullApart(worst, at, along, overlap);
    }

    private static Result<IMesh> Join(IGeometryEngine engine, IReadOnlyList<IMesh> pieces)
    {
        var vertices = ImmutableArray.CreateBuilder<Vector3>(pieces.Sum(p => p.VertexCount));
        var triangles = ImmutableArray.CreateBuilder<int>(pieces.Sum(p => p.Triangles.Length));
        foreach (var piece in pieces)
        {
            int offset = vertices.Count;
            vertices.AddRange(piece.Vertices);
            foreach (int corner in piece.Triangles) triangles.Add(corner + offset);
        }

        return engine.CreateMesh(vertices.MoveToImmutable(), triangles.MoveToImmutable(), pieces[0].Metadata);
    }
}
