using System.Text.Json.Serialization;
using BasicResults;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Smoothing;

/// <summary>
/// How a smoothing command rounds the mesh. Save files store the member name, so a member can be
/// added but never renamed.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SmoothingMethod {
    /// <summary>
    /// Out by the intensity and back, re-meshing after every step, then the inflation as a third
    /// offset: 2N + 1 meshings for N iterations. What every project saved before
    /// <see cref="Grid"/> existed was smoothed with, so it stays the default for a command read
    /// back without a method.
    /// </summary>
    Offsets = 0,

    /// <summary>
    /// The same closing held on one sampled distance field, with the inflation applied there too,
    /// and meshed once. Several times faster on a typical bolus, and the volume no longer drifts
    /// down as iterations are added. A scan too large for the engine's grid at the requested
    /// resolution falls back to <see cref="Offsets"/>.
    /// </summary>
    Grid = 1,
}

public record SmoothSettings(
    int Iterations = 1,
    float Intensity = 1.0f,
    float Inflation = 0.1f,
    float RemeshRatio = 2.0f,
    float Resolution = 1.0f,
    SmoothingMethod Method = SmoothingMethod.Offsets) : IMeshCommand {
    /// <summary>The method new smoothing uses. The constructor's default stays the one old saves need.</summary>
    public const SmoothingMethod CurrentMethod = SmoothingMethod.Grid;

    /// <summary>
    /// The engine's refusal when its grid cannot resolve the intensity at the requested cell -
    /// the cell coarsened past it to fit a large scan, or an intensity below the resolution.
    /// </summary>
    private const string GridTooCoarse = "Modifiers.DistanceBelowCell";

    public int Priority => CommandPriority.Transform;

    /// <summary>
    /// Runs the volumetric Erosion-Dilation-Resize smoothing pipeline against <paramref name="mesh"/>.
    /// Does not take ownership of <paramref name="mesh"/>; intermediates it creates along the
    /// way are disposed here. Engine modifiers never return their input instance, so every
    /// stage's output is a fresh mesh this pipeline owns until it's replaced or returned.
    /// </summary>
    public string Describe() => $"Smoothing ({Intensity:F2} mm · {Iterations}x)";

    public Result<IMesh> Apply(IGeometryEngine engine, IMesh mesh) {
        int baseTriangleCount = mesh.TriangleCount;

        var closed = Method == SmoothingMethod.Grid ? CloseOnGrid(engine, mesh) : CloseByOffsets(engine, mesh);
        if (closed.IsFailure) return closed.Error;

        // Resize (Decimation)
        int targetTriangleCount = (int)(baseTriangleCount * Math.Max(RemeshRatio, 1.0));
        var resizeResult = engine.Modifiers.Decimate(closed.Value, targetTriangleCount);

        return resizeResult;
    }

    /// <summary>
    /// The closing and inflation on one grid, meshed once. Where the grid cannot resolve the
    /// intensity the offsets route runs instead - decided by the mesh and settings alone, so a
    /// replay takes the same route the first run did.
    /// </summary>
    private Result<IMesh> CloseOnGrid(IGeometryEngine engine, IMesh mesh) {
        var closed = engine.Modifiers.OffsetSmooth(mesh, Intensity, Iterations, Resolution, Inflation);
        if (closed.IsFailure && closed.Error.Code == GridTooCoarse) {
            return CloseByOffsets(engine, mesh);
        }

        if (closed.IsFailure) return closed.Error;
        if (closed.Value.TriangleCount == 0) return OverEroded;

        return closed;
    }

    private Result<IMesh> CloseByOffsets(IGeometryEngine engine, IMesh mesh) {
        // Erosion through offset cycle
        var offsetResult = engine.Modifiers.DoubleOffset(mesh, Intensity, Iterations, Resolution);
        if (offsetResult.IsFailure) return offsetResult.Error;

        var currentMesh = offsetResult.Value;

        if (currentMesh.TriangleCount == 0) {
            return OverEroded;
        }

        // optional inflation
        if (Math.Abs(Inflation) > 0.001) {
            var inflationResult = engine.Modifiers.Offset(currentMesh, Inflation, Resolution);
            if (inflationResult.IsFailure) return inflationResult.Error;
            currentMesh = inflationResult.Value;
        }

        return Result<IMesh>.Success(currentMesh);
    }

    private static Error OverEroded =>
        new("Smoothing.OverEroded", "The mesh collapsed due to high intensity. Try reducing Iterations or Intensity.");
}
