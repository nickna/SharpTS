using System;
using System.IO;
using System.Linq;

namespace SharpTS.Compilation;

/// <summary>Portable filenames for the explicit native implementation-DLL cohort.</summary>
internal static class NativeReferenceFileName
{
    internal static bool IsSupported(string? name)
    {
        if (string.IsNullOrEmpty(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.IndexOfAny(['/', '\\', ':', ';', '%', '$', '<', '>', '"', '|', '?', '*']) >= 0 || name.Any(char.IsControl))
            return false;
        // Win32 reserves these device stems even when followed by an extension.
        // https://learn.microsoft.com/windows/win32/fileio/naming-a-file
        int separator = name.IndexOf('.');
        string stem = (separator < 0 ? name : name[..separator]).TrimEnd(' ');
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase))
            return false;
        return !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
            stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9' or '\u00b9' or '\u00b2' or '\u00b3');
    }
}
