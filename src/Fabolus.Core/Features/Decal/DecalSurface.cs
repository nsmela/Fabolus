using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using BasicResults;
using Fabolus.Core.Geometry;
using GE = GeometryEngine.Core.Geometry;

namespace Fabolus.Core.Features.Decal;

/// <summary>
/// Everything a decal needs from the mesh it is placed on, prepared once per mesh: the spatial
/// index prisms are contoured against, and the prisms already built on it.
/// </summary>
/// <remarks>
/// <para>
/// This restores what 29413a7 added to the adapter and e75c9cd lost when the adapter was deleted.
/// Since then every prism, every preset raycast (79 of them for a mould) and every drag frame has
/// rebuilt the index of the whole mesh from scratch.
/// </para>
/// <para>
/// Meshes are immutable, so the instance identifies the surface. The table is weak on the mesh:
/// when nothing else holds a mesh, its index and its prisms go with it, and nothing has to be
/// invalidated by hand. A surface is never disposed, because a build on another thread may still
/// be reading it; it holds managed memory the collector reclaims.
/// </para>
/// </remarks>
public sealed class DecalSurface
{
    /// <summary>
    /// Prisms kept per surface. Editing a label (typing, dragging a slider) produces a new
    /// request per step, so the cache is bounded; when it fills it is cleared, which costs one
    /// rebuild of each decal still on screen.
    /// </summary>
    private const int MaxCachedPrisms = 128;

    private static readonly ConditionalWeakTable<IMesh, Lazy<Result<DecalSurface>>> Prepared = new();

    private readonly ConcurrentDictionary<DecalPrismRequest, IMesh> _prisms = new();

    public IMesh Mesh { get; }
    public GE.ISpatialIndex Index { get; }

    private DecalSurface(IMesh mesh, GE.ISpatialIndex index)
    {
        Mesh = mesh;
        Index = index;
    }

    /// <summary>
    /// The prepared surface for <paramref name="mesh"/>, building its index on first use. Safe to
    /// call from several threads at once; the index is built only once.
    /// </summary>
    public static Result<DecalSurface> For(IGeometryEngine engine, IMesh mesh)
    {
        if (mesh is null)
            return MeshErrors.NullSource;

        var lazy = Prepared.GetValue(mesh, m => new Lazy<Result<DecalSurface>>(
            () => Prepare(engine, m),
            LazyThreadSafetyMode.ExecutionAndPublication));

        var result = lazy.Value;

        // Don't remember a failure: the next call gets to try again.
        if (result.IsFailure)
            Prepared.Remove(mesh);

        return result;
    }

    private static Result<DecalSurface> Prepare(IGeometryEngine engine, IMesh mesh)
    {
        var index = engine.Spatial.BuildIndex(mesh);
        if (index.IsFailure)
            return index.Error;

        return Result.Success(new DecalSurface(mesh, index.Value));
    }

    /// <summary>
    /// The text prism described by <paramref name="request"/>, contoured to this surface. Built
    /// once per distinct request; every later call for the same text, placement and extrusion is
    /// a dictionary lookup.
    /// </summary>
    public Result<IMesh> GetOrBuildPrism(IGeometryEngine engine, IGlyphOutlineSource outlineSource, DecalPrismRequest request)
    {
        if (_prisms.TryGetValue(request, out var cached))
            return Result.Success(cached);

        var outlines = outlineSource.GetOutlines(request.Text, request.Font, request.CapHeight, request.Tracking);
        if (outlines.IsFailure)
            return outlines.Error;

        if (outlines.Value.Count == 0)
            return DecalErrors.EmptyOutlines;

        var prism = BuildPrism(engine, outlines.Value, request.Frame, request.Depth, request.Sink, request.Overshoot, request.MaxEdgeLength);
        if (prism.IsFailure)
            return prism;

        if (_prisms.Count >= MaxCachedPrisms)
            _prisms.Clear();

        _prisms[request] = prism.Value;
        return prism;
    }

    /// <summary>
    /// Builds a prism from arbitrary outlines against this surface, without caching it. For
    /// one-off shapes such as the drag patch, whose placement changes every frame.
    /// </summary>
    public Result<IMesh> BuildPrism(
        IGeometryEngine engine,
        IReadOnlyList<Polygon2D> outlines,
        DecalFrame frame,
        float depth,
        float sink,
        float overshoot,
        float maxEdgeLength)
    {
        var spec = new GE.DecalPrismSpec(
            [.. outlines],
            new GE.SurfaceFrame(ToVec3(frame.Origin), ToVec3(frame.U), ToVec3(frame.V), ToVec3(frame.N)),
            Depth: depth,
            Sink: sink,
            Overshoot: overshoot,
            MaxEdgeLength: maxEdgeLength,
            SurfaceIndex: Maybe<GE.ISpatialIndex>.Some(Index));

        return engine.Decals.BuildPrism(spec);
    }

    private static GE.Primitives.Vec3 ToVec3(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}

/// <summary>
/// Everything that decides the shape of a text prism, and so the key it is cached under. The
/// target surface is not part of it: prisms are cached per <see cref="DecalSurface"/>.
/// </summary>
public readonly record struct DecalPrismRequest(
    string Text,
    DecalFont Font,
    float CapHeight,
    float Tracking,
    Vector3 Anchor,
    Vector3 AnchorNormal,
    float RotationDeg,
    float Depth,
    float Sink,
    float Overshoot,
    float MaxEdgeLength)
{
    private const float EmbossSinkOffset = -0.25f;
    private const float EmbossOvershootOffset = 0.0f;
    private const float EngraveOvershootOffset = 0.5f;
    private const float MinMaxEdgeLength = 0.4f;
    private const float CapHeightToEdgeLengthDivisor = 8.0f;

    public DecalFrame Frame => DecalFrame.FromHit(
        new System.Numerics.Vector3((float)Anchor.X, (float)Anchor.Y, (float)Anchor.Z),
        new System.Numerics.Vector3((float)AnchorNormal.X, (float)AnchorNormal.Y, (float)AnchorNormal.Z),
        RotationDeg);

    /// <summary>
    /// The prism that is actually cut into or joined onto the mesh: an emboss sinks a little
    /// below the surface so the union welds, an engrave reaches down its full depth and pokes
    /// out above so the subtraction leaves no skin.
    /// </summary>
    public static DecalPrismRequest ForApply(TextDecal decal)
    {
        bool emboss = decal.Operation == EmbossOperation.Emboss;
        return new DecalPrismRequest(
            decal.Text,
            decal.Font,
            decal.CapHeight,
            decal.Tracking,
            decal.Anchor,
            decal.AnchorNormal,
            decal.RotationDeg,
            decal.Depth,
            Sink: emboss ? EmbossSinkOffset : -decal.Depth,
            Overshoot: emboss ? EmbossOvershootOffset : EngraveOvershootOffset,
            MaxEdgeLength: Math.Max(MinMaxEdgeLength, decal.CapHeight / CapHeightToEdgeLengthDivisor));
    }
}
