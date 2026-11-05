using BasicResults;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.MeshIO;

/// <summary>
/// Writes a file so that a failure part-way through leaves whatever was there before untouched.
/// </summary>
/// <remarks>
/// File.WriteAllBytes truncates the target before it writes, so a save that fails part-way - a
/// full disk, a pulled USB stick, a crash - leaves a project that was fine a moment ago cut short
/// and unopenable. Here the bytes go to a temporary file beside the target first, are flushed to
/// the disk, and only then take the target's name. The temporary sits in the same folder so the
/// rename never crosses a volume and stays a single replace rather than a copy.
/// </remarks>
internal static class SafeFile {
    public static Result WriteAllBytes(string filePath, byte[] data) =>
        Write(filePath, stream => stream.Write(data));

    /// <summary>
    /// Has <paramref name="write"/> fill a temporary file, then moves it over
    /// <paramref name="filePath"/>. If anything fails, the temporary is removed and the target is
    /// left as it was.
    /// </summary>
    public static Result Write(string filePath, Action<Stream> write) {
        string? temporary = null;

        try {
            var fullPath = Path.GetFullPath(filePath);
            var directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
            temporary = Path.Combine(directory, $"{Path.GetFileName(fullPath)}.{Path.GetRandomFileName()}.tmp");

            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                write(stream);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, fullPath, overwrite: true);
            return Result.Success();
        } catch (Exception failure) when (failure is IOException or UnauthorizedAccessException
                                              or ArgumentException or NotSupportedException) {
            if (temporary is not null) TryDelete(temporary);
            return MeshErrors.ExportFailed(failure.Message);
        }
    }

    private static void TryDelete(string path) {
        try {
            File.Delete(path);
        } catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) {
            // A stray .tmp beside the project is untidy, not harmful, and the save has already
            // failed with the reason that matters.
        }
    }
}
