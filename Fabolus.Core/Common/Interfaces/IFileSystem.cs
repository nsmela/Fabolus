namespace Radaidant.Core.Common.Interfaces;

/// <summary>
/// Named architectural seam for file system access.
/// Never call System.IO static methods directly outside of Infrastructure/.
/// Implementations live in Infrastructure/FileSystem.cs.
/// </summary>
public interface IFileSystem
{
    /// <summary>
    /// Returns true if the file at the given path exists.
    /// </summary>
    bool Exists(string path);

    /// <summary>
    /// Reads the entire content of a file as a string.
    /// </summary>
    string ReadAllText(string path);

    /// <summary>
    /// Writes the given content to a file, creating or overwriting it.
    /// </summary>
    void WriteAllText(string path, string content);

    /// <summary>
    /// Reads the entire content of a file as a byte array.
    /// </summary>
    byte[] ReadAllBytes(string path);

    /// <summary>
    /// Writes a byte array to a file, creating or overwriting it.
    /// </summary>
    void WriteAllBytes(string path, byte[] data);

    /// <summary>
    /// Returns the paths of all files in the given directory matching the search pattern.
    /// </summary>
    string[] GetFiles(string directory, string searchPattern);

    /// <summary>Returns true if the given directory path exists.</summary>
    bool DirectoryExists(string path);

    /// <summary>Creates all directories in the given path if they do not already exist.</summary>
    void CreateDirectory(string path);
}
