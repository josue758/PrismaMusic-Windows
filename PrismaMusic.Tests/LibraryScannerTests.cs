using System;
using System.IO;
using System.Threading;
using PrismaMusic;
using Xunit;

namespace PrismaMusic.Tests;

public sealed class LibraryScannerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "prisma-scan-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    [Fact]
    public void ScannerStopsBeforeEnumeratingWhenCancelled()
    {
        Directory.CreateDirectory(directory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => LibraryScanner.ReadFolder(directory, cancellation.Token));
    }

    [Fact]
    public void ScannerIgnoresUnsupportedFiles()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "notes.txt"), "not music");

        var result = LibraryScanner.ReadFolder(directory);

        Assert.Empty(result.Tracks);
        Assert.Equal(0, result.Errors);
    }
}
