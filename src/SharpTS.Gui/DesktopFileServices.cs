using System.Text;

namespace SharpTS.Gui;

public static partial class DesktopBridge
{
    /// <summary>Writes UTF-8 to a sibling temporary file, flushes, then replaces the destination.</summary>
    public static Task<object?> WriteTextFileAtomicAsync(string path, string text)
    {
        EnsureOwnerThread();
        return AsGuestCompletionAsync(Task.Run(() => WriteAtomicAsync(path, text)));
    }

    internal static async Task WriteAtomicAsync(string path, string text)
    {
        string target = Path.GetFullPath(path);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(target)) File.Replace(temporary, target, null);
            else File.Move(temporary, target);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static Task<object?> ReadTextFileAsync(string path, double maximumBytes)
    {
        EnsureOwnerThread();
        return AsGuestResultAsync(Task.Run(() => ReadBoundedAsync(path, ToInteger(maximumBytes, nameof(maximumBytes)))));
    }

    private static async Task<string> ReadBoundedAsync(string path, int maximumBytes)
    {
        if (maximumBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > maximumBytes) throw new IOException($"File exceeds the {maximumBytes} byte limit.");
        using var contents = new MemoryStream((int)Math.Min(stream.Length, maximumBytes));
        byte[] buffer = new byte[Math.Min(65536, maximumBytes)];
        int count;
        while ((count = await stream.ReadAsync(buffer)) != 0)
        {
            // Enforce the limit during reading as well, including on platforms
            // where another process can grow an already-open file.
            if (contents.Length + count > maximumBytes)
                throw new IOException($"File exceeds the {maximumBytes} byte limit.");
            contents.Write(buffer, 0, count);
        }
        contents.Position = 0;
        using var reader = new StreamReader(contents, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync();
    }
}
