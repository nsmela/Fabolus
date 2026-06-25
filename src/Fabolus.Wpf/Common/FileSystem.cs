using Fabolus.Core.Common.Interfaces;
using System.IO;

namespace Fabolus.Wpf.Common;

/// <summary>
/// Concrete implementation of IFileSystem. Delegates to System.IO.
/// Never call System.IO static methods directly outside of this class.
/// </summary>
public sealed class FileSystem : IFileSystem {
    public bool Exists(string path) => File.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public void WriteAllText(string path, string content) => File.WriteAllText(path, content);

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public void WriteAllBytes(string path, byte[] data) => File.WriteAllBytes(path, data);

    public string[] GetFiles(string directory, string searchPattern) =>
        Directory.GetFiles(directory, searchPattern);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
}
