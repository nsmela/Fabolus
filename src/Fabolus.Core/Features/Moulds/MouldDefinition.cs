using BasicResults;
using Fabolus.Core.Features.AirChannels;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Moulds;

public abstract record MouldDefinition : IMeshCommand
{
    public Guid TargetMeshId { get; init; }
    public IReadOnlyList<AirChannelModel> AirChannels { get; init; } = Array.Empty<AirChannelModel>();

    /// <summary>
    /// How deep the trough - the basin excess silicone pools in while the mould fills - is
    /// recessed into the top of the mould. 0 leaves the top face solid.
    /// </summary>
    public double TroughHeight { get; init; }

    /// <summary>
    /// For <see cref="TroughShapeType.Footprint"/>, how far the trough stops short of the
    /// mould wall - the thickness of the rim holding the silicone in. For
    /// <see cref="TroughShapeType.Channels"/>, how far it spreads past the channel exits
    /// (and, as with any trough, how close it may come to the wall).
    /// </summary>
    public double TroughOffset { get; init; } = 2.5;

    public TroughShapeType TroughShape { get; init; } = TroughShapeType.Footprint;

    /// <summary>
    /// A channel trough with nothing to pool around is treated as no trough at all, so the
    /// mould doesn't silently grow taller for a basin that can't be carved.
    /// </summary>
    private bool HasTrough =>
        CarvesTrough
        && TroughHeight > 0
        && (TroughShape != TroughShapeType.Channels || AirChannels.Count > 0);

    /// <summary>
    /// Whether this shape has a flat top to recess a basin into at all.
    /// </summary>
    protected virtual bool CarvesTrough => true;

    /// <summary>
    /// The XY outline the mould body is extruded from, already grown by the wall thickness and
    /// taking the air channels in. The trough is cut from this, so measuring the basin and
    /// carving it work from the same polygon rather than two that could drift apart.
    /// </summary>
    protected abstract Result<Polygon2D> BuildFootprint(IGeometryEngine engine, IMesh mesh);

    /// <summary>
    /// How much silicone the trough holds, in cubic millimetres, or zero when this mould has no
    /// trough - the depth is zero, the shape has no top to cut into, or a channel trough has no
    /// channels to pool around.
    /// </summary>
    public Result<double> TroughCapacity(IGeometryEngine engine, IMesh mesh)
    {
        if (!HasTrough) return 0.0;

        var footprintResult = BuildFootprint(engine, mesh);
        if (footprintResult.IsFailure) return footprintResult.Error;

        return MouldTrough.Capacity(engine, footprintResult.Value, this);
    }

    public int Priority => CommandPriority.Mould;

    /// <summary>
    /// Generates just the mould shell shape - the bolus and air channels aren't subtracted
    /// yet, so it stays cheap enough for live preview while the user is still adjusting
    /// settings. (A trough does cut the shell here, but only against a simple prism.)
    /// </summary>
    public abstract Result<IMesh> Generate(IGeometryEngine engine, IMesh mesh);

    /// <summary>
    /// The shell for a shape, with its walls and base set. Trough settings and air channels are
    /// applied by the caller with a `with` expression, since not every caller has both.
    ///
    /// Anything not a shape this build knows falls back to concave, which is the shipped default.
    /// </summary>
    public static MouldDefinition OfShape(MouldShapeType shape, double wallThickness, double baseHeight) =>
        shape switch
        {
            MouldShapeType.Convex => new ConvexMouldDefinition(wallThickness, baseHeight, baseHeight),
            MouldShapeType.Contoured => new ContouredMouldDefinition(wallThickness),
            _ => new ConcaveMouldDefinition(wallThickness, baseHeight, baseHeight)
        };

    public string Describe()
    {
        var shape = this switch
        {
            ConvexMouldDefinition => "Convex",
            ConcaveMouldDefinition => "Concave",
            ContouredMouldDefinition => "Contoured",
            _ => "2-part"
        };

        return $"Mould ({shape})";
    }

    /// <summary>
    /// The full committed pipeline: the shell from <see cref="Generate"/>, less the target mesh
    /// and every air channel. They all come out in one batch subtraction, which reads the shell
    /// once, rather than one subtraction each against a shell that changes every time.
    /// </summary>
    public Result<IMesh> Apply(IGeometryEngine engine, IMesh mesh)
    {
        var generateResult = Generate(engine, mesh);
        if (generateResult.IsFailure) return generateResult.Error;

        var cavities = new List<IMesh>(AirChannels.Count + 1) { mesh };

        foreach (var channel in AirChannels)
        {
            // Pass the target mesh so channels that snap to the surface (painted paths)
            // bake with the same raycast-fitted bottom the live preview showed.
            var channelMeshResult = channel.DomainModel.Generate(engine, AirChannelRenderMode.Full, mesh);
            if (channelMeshResult.IsFailure)
            {
                return channelMeshResult.Error;
            }

            cavities.Add(channelMeshResult.Value);
        }

        return engine.Booleans.Subtract(generateResult.Value, [.. cavities]);
    }

    /// <summary>
    /// Extrudes the mould body from its footprint, then recesses the trough into the top of
    /// it. <paramref name="coverTopZ"/> is where the body would have ended without a trough
    /// - the body grows upwards by <see cref="TroughHeight"/> so the cover over the bolus
    /// keeps its full thickness and becomes the floor of the basin.
    /// </summary>
    protected Result<IMesh> ExtrudeBody(IGeometryEngine engine, Polygon2D footprint, float zMin, float coverTopZ)
    {
        var bodyTopZ = HasTrough ? coverTopZ + (float)TroughHeight : coverTopZ;

        var body = engine.Polygons.Extrude(footprint, zMin, bodyTopZ);
        if (body.IsFailure || !HasTrough) return body;

        return MouldTrough.Carve(engine, body.Value, footprint, coverTopZ, bodyTopZ, this);
    }
}

public sealed record ConvexMouldDefinition(double OffsetXY = 2.0, double OffsetBottom = 2.0, double OffsetTop = 2.0) : MouldDefinition
{
    protected override Result<Polygon2D> BuildFootprint(IGeometryEngine engine, IMesh mesh)
    {
        var hull = engine.Polygons.ProjectConvexHull(mesh);
        if (hull.IsFailure) return hull.Error;

        return MouldFootprint.Build(engine, hull.Value, OffsetXY, AirChannels);
    }

    public override Result<IMesh> Generate(IGeometryEngine engine, IMesh mesh)
    {
        var statsResult = engine.Evaluators.GetStatistics(mesh);
        if (statsResult.IsFailure)
            return statsResult.Error;

        var bounds = statsResult.Value;

        var footprint = BuildFootprint(engine, mesh);
        if (footprint.IsFailure) return footprint.Error;

        return ExtrudeBody(engine, footprint.Value,
            (float)bounds.BoundsMin.Z - (float)OffsetBottom,
            (float)bounds.BoundsMax.Z + (float)OffsetTop);
    }
}

public sealed record ConcaveMouldDefinition(double OffsetXY = 2.0, double OffsetBottom = 2.0, double OffsetTop = 2.0) : MouldDefinition
{
    protected override Result<Polygon2D> BuildFootprint(IGeometryEngine engine, IMesh mesh)
    {
        var shadow = engine.Polygons.ProjectOutline(mesh);
        if (shadow.IsFailure) return shadow.Error;

        return MouldFootprint.Build(engine, shadow.Value, OffsetXY, AirChannels);
    }

    public override Result<IMesh> Generate(IGeometryEngine engine, IMesh mesh)
    {
        var statsResult = engine.Evaluators.GetStatistics(mesh);
        if (statsResult.IsFailure)
            return statsResult.Error;

        var bounds = statsResult.Value;

        var footprint = BuildFootprint(engine, mesh);
        if (footprint.IsFailure) return footprint.Error;

        return ExtrudeBody(engine, footprint.Value,
            (float)bounds.BoundsMin.Z - (float)OffsetBottom,
            (float)bounds.BoundsMax.Z + (float)OffsetTop);
    }
}

public sealed record ContouredMouldDefinition(double OffsetXY = 2.0) : MouldDefinition
{
    // No trough here: this shell follows the bolus surface, so there's no flat top face to
    // recess a basin into - a cut would just open a hole through the shell.
    protected override bool CarvesTrough => false;

    // Never reached while CarvesTrough is false, and there is no honest answer to give: this
    // shell is offset straight off the mesh rather than extruded from any outline.
    protected override Result<Polygon2D> BuildFootprint(IGeometryEngine engine, IMesh mesh) =>
        TroughErrors.ContouredHasNoFootprint;

    public override Result<IMesh> Generate(IGeometryEngine engine, IMesh mesh)
    {
        return engine.Modifiers.Offset(mesh, (float)OffsetXY, 0);
    }
}
