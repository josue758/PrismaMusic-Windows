using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NAudio.Wave;
using Xunit;

namespace PrismaMusic.Tests;

public sealed class NasScanTests : IDisposable
{
    private readonly string storage = Path.Combine(Path.GetTempPath(), "prisma-nas-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(storage)) Directory.Delete(storage, recursive: true);
    }

    [Fact]
    public void ResumesOnlyPendingDirectoriesAfterDisconnectionAndRestart()
    {
        string source = Path.Combine(storage, "source");
        string first = Path.Combine(source, "album1"), second = Path.Combine(source, "album2"), third = Path.Combine(source, "album3");
        foreach (string directory in new[] { first, second, third })
        {
            Directory.CreateDirectory(directory);
            using var wave = new WaveFileWriter(Path.Combine(directory, "track.wav"), new WaveFormat(8000, 16, 1));
            wave.Write(new byte[1600], 0, 1600);
        }
        var indexed = new Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();
        var initial = new NasScanRun(source, true, false, null);
        var result = initial.Read(default, null, indexed, 2, track => indexed.Add(track.Path, track), (path, _) => failures.Add(path),
            (directory, child) =>
            {
                if (directory == source)
                {
                    child(first);
                    child(second);
                    child(third);
                }
                if (directory == second) throw new IOException("Network name deleted", unchecked((int)0x80070040));
                return LibraryScanner.ReadFolder(directory, includeSubfolders: false, parallelReads: 2, cache: indexed);
            });
        Assert.Single(result.Item1);
        Assert.Equal(second, Assert.Single(failures));
        foreach (var track in result.Item1) indexed[track.Path] = track;
        NasScanCheckpoint.Write(storage, source, initial.Snapshot());
        var recovered = NasScanCheckpoint.Read(storage, source);
        Assert.Equal(new[] { second, third }, recovered.Pending);
        Assert.Contains(first, recovered.Completed);

        var reopened = new NasScanRun(source, recovered.Subfolders, recovered.OnlyNew, recovered);
        var finished = reopened.Read(default, null, indexed, 2, null, (_, error) => Assert.Fail(error));
        Assert.Equal(2, finished.Item1.Count);
        Assert.All(finished.Item1, song => Assert.False(indexed.ContainsKey(song.Path)));
        Assert.Empty(reopened.Snapshot().Pending);
        NasScanCheckpoint.Write(storage, source, reopened.Snapshot());
        Assert.Null(NasScanCheckpoint.Read(storage, source));
    }

    [Fact]
    public void CancellationKeepsCurrentDirectoryPendingAndRejectsPathsOutsideSource()
    {
        const string source = @"\\nas\music";
        string child = Path.Combine(source, "album");
        var run = new NasScanRun(source, true, false, null);
        using var cancel = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => run.Read(cancel.Token, null, new Dictionary<string, Track>(), 1, null,
            (_, error) => Assert.Fail(error), (_, addChild) =>
            {
                addChild(child);
                cancel.Cancel();
                return (new List<Track>(), 0);
            }));
        Assert.Equal(child, Assert.Single(run.Snapshot().Pending));
        NasScanCheckpoint.Write(storage, source, run.Snapshot());
        Assert.Equal(child, Assert.Single(NasScanCheckpoint.Read(storage, source).Pending));
        NasScanCheckpoint.Write(storage, source, run.Snapshot() with { Pending = new[] { @"\\nas\other\album" } });
        Assert.Null(NasScanCheckpoint.Read(storage, source));
    }
}
