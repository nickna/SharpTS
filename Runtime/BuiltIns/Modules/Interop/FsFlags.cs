namespace SharpTS.Runtime.BuiltIns.Modules.Interop;

/// <summary>
/// Parses Node.js file flags (string or numeric) into .NET FileMode, FileAccess, and FileShare.
/// </summary>
/// <remarks>
/// Node.js supports both string flags ('r', 'w', 'a', 'r+', etc.) and numeric flags
/// (O_RDONLY, O_WRONLY, O_RDWR, O_CREAT, O_EXCL, O_TRUNC, O_APPEND).
/// This class converts them to equivalent .NET parameters for FileStream.
/// </remarks>
public static class FsFlags
{
    // Node.js numeric flags (matches fs.constants)
    public const int O_RDONLY = 0;
    public const int O_WRONLY = 1;
    public const int O_RDWR = 2;
    public const int O_CREAT = 64;
    public const int O_EXCL = 128;
    public const int O_TRUNC = 512;
    public const int O_APPEND = 1024;

    /// <summary>
    /// Parses Node.js file flags into .NET parameters.
    /// </summary>
    /// <param name="flags">String flags ('r', 'w', 'a', etc.) or numeric flags.</param>
    /// <returns>A tuple of (FileMode, FileAccess, FileShare).</returns>
    public static (FileMode Mode, FileAccess Access, FileShare Share) Parse(object? flags)
    {
        if (flags is double d)
        {
            return ParseNumeric((int)d);
        }

        if (flags is int i)
        {
            return ParseNumeric(i);
        }

        var flagStr = flags?.ToString() ?? "r";
        return ParseString(flagStr);
    }

    /// <summary>
    /// Parses string-based flags.
    /// </summary>
    private static (FileMode Mode, FileAccess Access, FileShare Share) ParseString(string flags)
    {
        // Reference: https://nodejs.org/api/fs.html#file-system-flags
        return flags switch
        {
            // Read flags
            "r" => (FileMode.Open, FileAccess.Read, FileShare.Read),
            "rs" or "sr" => (FileMode.Open, FileAccess.Read, FileShare.Read),

            // Read-write flags
            "r+" => (FileMode.Open, FileAccess.ReadWrite, FileShare.Read),
            "rs+" or "sr+" => (FileMode.Open, FileAccess.ReadWrite, FileShare.Read),

            // Write flags (truncate)
            "w" => (FileMode.Create, FileAccess.Write, FileShare.None),
            "wx" or "xw" => (FileMode.CreateNew, FileAccess.Write, FileShare.None),

            // Write-read flags (truncate)
            "w+" => (FileMode.Create, FileAccess.ReadWrite, FileShare.None),
            "wx+" or "xw+" => (FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None),

            // Append flags
            "a" => (FileMode.Append, FileAccess.Write, FileShare.None),
            "ax" or "xa" => (FileMode.Append, FileAccess.Write, FileShare.None),

            // Append-read flags
            "a+" => (FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None),
            "ax+" or "xa+" => (FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None),

            // Default to read
            _ => (FileMode.Open, FileAccess.Read, FileShare.Read)
        };
    }

    /// <summary>
    /// Parses numeric-based flags (O_RDONLY, O_WRONLY, etc. combined with bitwise OR).
    /// </summary>
    private static (FileMode Mode, FileAccess Access, FileShare Share) ParseNumeric(int flags)
    {
        // Determine access mode
        var accessBits = flags & 3; // O_RDONLY=0, O_WRONLY=1, O_RDWR=2
        var access = accessBits switch
        {
            O_WRONLY => FileAccess.Write,
            O_RDWR => FileAccess.ReadWrite,
            _ => FileAccess.Read // O_RDONLY
        };

        // Determine file mode based on creation/truncation flags
        FileMode mode;
        if ((flags & O_CREAT) != 0)
        {
            if ((flags & O_EXCL) != 0)
            {
                mode = FileMode.CreateNew;
            }
            else if ((flags & O_TRUNC) != 0)
            {
                mode = FileMode.Create;
            }
            else
            {
                mode = FileMode.OpenOrCreate;
            }
        }
        else if ((flags & O_TRUNC) != 0)
        {
            mode = FileMode.Truncate;
        }
        else if ((flags & O_APPEND) != 0)
        {
            mode = FileMode.Append;
        }
        else
        {
            mode = FileMode.Open;
        }

        // Use ReadWrite share for basic operations
        var share = FileShare.ReadWrite;

        return (mode, access, share);
    }

    /// <summary>
    /// Validates that the flags are compatible with the specified operation.
    /// </summary>
    /// <param name="flags">The flags to validate.</param>
    /// <param name="forReading">True if the operation requires read access.</param>
    /// <param name="forWriting">True if the operation requires write access.</param>
    /// <returns>True if the flags are compatible.</returns>
    public static bool ValidateAccess(object? flags, bool forReading, bool forWriting)
    {
        var (_, access, _) = Parse(flags);

        if (forReading && access == FileAccess.Write)
            return false;

        if (forWriting && access == FileAccess.Read)
            return false;

        return true;
    }
}
