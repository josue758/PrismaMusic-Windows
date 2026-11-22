using System;
using System.Collections.Generic;
using System.Threading;
using PrismaMusic;
using Xunit;

namespace PrismaMusic.Tests;

public sealed class LibraryRelocationTests
{
    [Fact]
    public void VerificationReportsOnlyTheTracksThatBelongToTheSource()
    {
        var progress = new Counter();
        var mapping = LibraryRelocation.Verify(@"C:\Music", @"D:\New",
            new[] { @"C:\Music\Disc 1\a.flac", @"C:\Music\Disc 2\b.flac", @"C:\MusicOther\c.flac" },
            _ => true, progress: progress);

        Assert.Equal(2, mapping.Count);
        Assert.Equal(@"D:\New\Disc 2\b.flac", mapping[@"C:\Music\Disc 2\b.flac"]);
        Assert.Equal((0, 2), progress.Values[0]);
        Assert.Equal((2, 2), progress.Values[^1]);
    }

    [Fact]
    public void CancellationStopsVerificationBetweenFiles()
    {
        using var cancellation = new CancellationTokenSource();
        int checkedFiles = 0;
        Assert.Throws<OperationCanceledException>(() => LibraryRelocation.Verify(@"C:\Old", @"D:\New",
            new[] { @"C:\Old\a.flac", @"C:\Old\b.flac" }, _ =>
            {
                checkedFiles++;
                cancellation.Cancel();
                return true;
            }, cancellation.Token));
        Assert.Equal(1, checkedFiles);
    }

    private sealed class Counter : IProgress<(int Verified, int Total)>
    {
        internal List<(int Verified, int Total)> Values { get; } = new();
        public void Report((int Verified, int Total) value) => Values.Add(value);
    }
}
