using GeometryEngine.Core.Geometry;

namespace Fabolus.Core.Features.MeshIO;

/// <summary>
/// How Fabolus marks up a 3MF package: a vendor namespace for the metadata it adds, and the role
/// it tags the second, non-printable object with.
///
/// These values are a file-format contract, not a preference - every .3mf Fabolus has written
/// carries them, and changing one orphans that history in existing files. A 3MF reader that does
/// not know the namespace ignores both the metadata and the extra object, which is what keeps the
/// file valid in a slicer.
/// </summary>
public static class FabolusPackage {
    public static readonly PackageVendor Vendor =
        new("fab", "http://fabolus.io/2026/metadata", "basemesh");

    /// <summary>The metadata entry holding the entry's command history, as JSON.</summary>
    public const string CommandsKey = "fab:Commands";

    // The entry's name is deliberately not stored. A reopened file is named after the file, which
    // is what the user last chose and can see - carrying an internal name would mean renaming a
    // file on disk had no effect on what the mesh list shows.

    public static bool IsPackage(string filePath) =>
        string.Equals(Path.GetExtension(filePath), ".3mf", StringComparison.OrdinalIgnoreCase);
}
