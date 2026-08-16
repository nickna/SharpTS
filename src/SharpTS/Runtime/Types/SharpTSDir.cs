using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents a directory handle returned by fs.opendirSync().
/// Provides an iterator interface for reading directory entries one at a time.
/// </summary>
/// <remarks>
/// This class wraps an IEnumerator over directory entries and provides:
/// - path: The path to the directory
/// - readSync(): Returns the next Dirent or null when done
/// - closeSync(): Closes the directory handle
/// </remarks>
public sealed class SharpTSDir : ISharpTSPropertyAccessor
{
    private readonly string _path;
    private readonly IEnumerator<string> _enumerator;
    private bool _closed;

    /// <summary>
    /// Creates a new Dir instance for the specified path.
    /// </summary>
    /// <param name="path">The directory path.</param>
    public SharpTSDir(string path)
    {
        _path = path;
        _enumerator = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
        _closed = false;
    }

    /// <summary>
    /// Gets the path of this directory.
    /// </summary>
    public string Path => _path;

    /// <summary>
    /// Reads the next directory entry synchronously.
    /// </summary>
    /// <returns>A Dirent object, or null if no more entries.</returns>
    public object? ReadSync()
    {
        if (_closed)
        {
            throw new InvalidOperationException("Directory handle is closed");
        }

        if (_enumerator.MoveNext())
        {
            return CreateDirent(_enumerator.Current);
        }

        return null;
    }

    /// <summary>
    /// Closes the directory handle.
    /// </summary>
    public void CloseSync()
    {
        if (!_closed)
        {
            _enumerator.Dispose();
            _closed = true;
        }
    }

    /// <summary>
    /// Creates a Dirent-like object for a directory entry.
    /// </summary>
    private static SharpTSObject CreateDirent(string fullPath)
    {
        var name = System.IO.Path.GetFileName(fullPath);
        var isFile = File.Exists(fullPath);
        var isDir = Directory.Exists(fullPath);
        var isSymlink = false;

        // Check for symbolic link
        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Exists || Directory.Exists(fullPath))
        {
            isSymlink = fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }

        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["name"] = name,
            ["isFile"] = isFile && !isDir,
            ["isDirectory"] = isDir,
            ["isSymbolicLink"] = isSymlink,
            ["isBlockDevice"] = false,
            ["isCharacterDevice"] = false,
            ["isFIFO"] = false,
            ["isSocket"] = false,
        });
    }

    #region ISharpTSPropertyAccessor Implementation

    /// <summary>
    /// Gets a property value by name.
    /// </summary>
    public object? GetProperty(string name)
    {
        return name switch
        {
            "path" => _path,
            "readSync" => BuiltInMethod.CreateV2("readSync", 0, 0, (_, _, _) => RuntimeValue.FromBoxed(ReadSync())),
            "closeSync" => BuiltInMethod.CreateV2("closeSync", 0, 0, (_, _, _) => { CloseSync(); return RuntimeValue.Null; }),
            _ => null
        };
    }

    /// <summary>
    /// Sets a property value by name.
    /// </summary>
    public void SetProperty(string name, object? value)
    {
        // Dir properties are read-only
    }

    /// <summary>
    /// Checks if a property exists.
    /// </summary>
    public bool HasProperty(string name)
    {
        return name is "path" or "readSync" or "closeSync";
    }

    /// <summary>
    /// Gets all property names.
    /// </summary>
    public IEnumerable<string> PropertyNames => ["path", "readSync", "closeSync"];

    #endregion
}
