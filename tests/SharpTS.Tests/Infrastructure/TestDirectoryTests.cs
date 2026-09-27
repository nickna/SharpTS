using SharpTS.Testing;
using Xunit;

namespace SharpTS.Tests.Infrastructure;

public sealed class TestDirectoryTests
{
    [SkippableFact]
    public async Task LockedFileIsRetainedWithDiagnosticsWithoutMaskingPrimaryFailure()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows file sharing semantics");
        string directory = Path.Combine(Path.GetTempPath(), $"sharpts-cleanup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var notes = new List<string>();
        try
        {
            using (var file = File.Open(Path.Combine(directory, "locked.dll"), FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
            {
                var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                {
                    try { throw new InvalidOperationException("primary failure"); }
                    finally { await TestDirectory.TryDeleteAsync(directory, notes.Add, TimeSpan.FromMilliseconds(100)); }
                });
                Assert.Equal("primary failure", exception.Message);
                Assert.Contains(directory, Assert.Single(notes));
                Assert.True(Directory.Exists(directory));
            }
            Assert.True(await TestDirectory.TryDeleteAsync(directory, notes.Add));
            Assert.False(Directory.Exists(directory));
        }
        finally { await TestDirectory.TryDeleteAsync(directory, notes.Add); }
    }

    [SkippableFact]
    public async Task RetriesUntilWindowsFileLockIsReleased()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows file sharing semantics");
        string directory = Path.Combine(Path.GetTempPath(), $"sharpts-cleanup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        using var file = File.Open(Path.Combine(directory, "locked.dll"), FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        var notes = new List<string>();
        Task<bool> cleanup = TestDirectory.TryDeleteAsync(directory, notes.Add);
        try
        {
            Assert.False(cleanup.IsCompleted);
            await file.DisposeAsync();
            Assert.True(await cleanup);
            Assert.Empty(notes);
        }
        finally
        {
            await file.DisposeAsync();
            await TestDirectory.TryDeleteAsync(directory, notes.Add);
        }
    }
}
