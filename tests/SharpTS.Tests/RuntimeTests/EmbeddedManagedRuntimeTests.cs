using System.Text;
using SharpTS.Runtime;
using Xunit;

namespace SharpTS.Tests.RuntimeTests;

/// <summary>
/// Pins the atomic-extraction behavior of the Native SKU's embedded managed
/// runtime payload (#1324). CI's native smoke proves the end-to-end path; these
/// tests pin the write discipline (temp + rename, overwrite, cleanup) that only
/// misbehaves in conditions the smoke never creates.
/// </summary>
public class EmbeddedManagedRuntimeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sharpts-emr-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static MemoryStream Payload(string content) =>
        new(Encoding.UTF8.GetBytes(content));

    [Fact]
    public void Extracts_payload_bytes_to_destination()
    {
        string dest = Path.Combine(_dir, "SharpTS.dll");

        bool ok = EmbeddedManagedRuntime.TryExtractTo(Payload("payload-v1"), dest, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal("payload-v1", File.ReadAllText(dest));
    }

    [Fact]
    public void Overwrites_a_stale_destination()
    {
        string dest = Path.Combine(_dir, "SharpTS.dll");
        File.WriteAllText(dest, "stale-old-version");

        bool ok = EmbeddedManagedRuntime.TryExtractTo(Payload("fresh"), dest, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal("fresh", File.ReadAllText(dest));
    }

    [Fact]
    public void Creates_missing_destination_directories()
    {
        string dest = Path.Combine(_dir, "nested", "deeper", "SharpTS.dll");

        bool ok = EmbeddedManagedRuntime.TryExtractTo(Payload("x"), dest, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.True(File.Exists(dest));
    }

    [Fact]
    public void Leaves_no_temp_file_behind_on_success()
    {
        string dest = Path.Combine(_dir, "SharpTS.dll");

        Assert.True(EmbeddedManagedRuntime.TryExtractTo(Payload("x"), dest, out _));

        Assert.Equal([dest], Directory.GetFiles(_dir));
    }

    [Fact]
    public void Failed_extraction_reports_error_and_cleans_up_its_temp_file()
    {
        string dest = Path.Combine(_dir, "SharpTS.dll");
        File.WriteAllText(dest, "in-use");

        // Windows: an open handle without FileShare.Delete makes the final
        // File.Move fail — the case a running compiled program creates.
        using (File.Open(dest, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            bool ok = EmbeddedManagedRuntime.TryExtractTo(Payload("new"), dest, out var error);

            if (OperatingSystem.IsWindows())
            {
                Assert.False(ok);
                Assert.NotNull(error);
            }
            else
            {
                // POSIX rename over an open file succeeds; both outcomes must
                // leave no temp litter, asserted below.
                Assert.True(ok);
            }
        }

        Assert.Equal([dest], Directory.GetFiles(_dir));
    }

    [Fact]
    public void Missing_embedded_resource_is_a_named_error()
    {
        // Ordinary managed test builds embed no payload (it is a Native AOT
        // publish input), so the resource-based overload reports exactly that.
        string dest = Path.Combine(_dir, "SharpTS.dll");

        bool ok = EmbeddedManagedRuntime.TryExtractTo(dest, out var error);

        Assert.False(ok);
        Assert.Contains("SharpTS.ManagedRuntime.dll", error);
        Assert.Contains("not present", error);
        Assert.False(File.Exists(dest));
    }
}
