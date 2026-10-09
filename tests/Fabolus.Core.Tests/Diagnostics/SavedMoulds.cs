using System.Runtime.CompilerServices;
using BasicResults;
using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// Opens the saved moulds in tests/files/3mf the way the app opens them, for diagnostics written when
/// a mesh carried its own history. A mould's history now lives on its workspace entry, and only
/// <see cref="ImportMesh"/> restores that entry - the engine's own import hands back the geometry and
/// nothing else, so a mould opened that way is no longer a mould.
/// </summary>
/// <remarks>
/// The entry is remembered against the mesh it came with, weakly, so a diagnostic can keep its
/// "import, then make the mould" shape: <see cref="Open"/> for the first, <see cref="Mould"/> for the
/// second.
/// </remarks>
internal static class SavedMoulds
{
    private static readonly ConditionalWeakTable<IMesh, MeshRecord> Entries = new();

    /// <summary>The file's mesh, opened into a workspace of its own as the app would open it.</summary>
    public static Result<IMesh> Open(IGeometryEngine engine, string path)
    {
        var opened = new ImportMesh(engine).Execute(Workspace.CreateEmpty(), path);
        if (opened.IsFailure) return opened.Error;

        var mesh = opened.Value.GetActiveMesh();
        var record = opened.Value.GetActiveRecord();
        if (mesh.IsFailure) return mesh.Error;
        if (record.IsFailure) return record.Error;

        Entries.AddOrUpdate(mesh.Value, record.Value);
        return mesh;
    }

    /// <summary>The mould a mesh from <see cref="Open"/> is, from the entry it was opened with.</summary>
    public static Result<MouldMesh> Mould(IMesh mesh) =>
        Entries.TryGetValue(mesh, out var record)
            ? MouldMesh.Create(mesh, record)
            : MouldMeshErrors.NoMouldMetadata;
}
