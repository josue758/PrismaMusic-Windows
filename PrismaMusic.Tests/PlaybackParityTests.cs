using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;
using Xunit;

namespace PrismaMusic.Tests;

public sealed class PlaybackParityTests
{
    private static Track Song(string path = "song.flac") =>
        new(path, "Song", "Artist", "Album", "Artist", "Rock", 2024, 1, 1, 10, "FLAC", 96000);

    [Fact]
    public void IntelligentPlaylistIntersectsRulesAndKeepsUnknownDatesOut()
    {
        var library = new Library();
        var now = DateTimeOffset.Now;
        var song = Song() with { AddedAt = now };
        library.Favorites.Add(song.Path);
        var rules = new SmartPlaylistRules(Artist: "artist", Genre: "rock", Codec: "FLAC", SampleRate: 96000,
            Favorites: true, Unplayed: true, AddedThisMonth: true);
        var history = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.True(rules.Matches(song, library, history, now));
        Assert.False(rules.Matches(song with { AddedAt = null }, library, history, now));
        Assert.False(rules.Matches(song with { SampleRate = 48000 }, library, history, now));
        history.Add("SONG.FLAC");
        Assert.False(rules.Matches(song, library, history, now));
    }

    [Fact]
    public void ReplayGainUsesPeakAndEqHeadroomAndFallsBackToTrack()
    {
        var tags = new ReplayGainTags(4, .8, null, null);
        Assert.Equal(0, tags.Decibels(ReplayGainMode.Off, 4));
        Assert.Equal(4, new ReplayGainTags(null, null, 4, .1).Decibels(ReplayGainMode.Album, 0));
        Assert.Equal(-20 * Math.Log10(.8) - 2, tags.Decibels(ReplayGainMode.Album, 2), 8);
        Assert.Equal(-7.25, ReplayGainTags.Parse(" -7.25 dB "));
        Assert.Null(ReplayGainTags.Parse("NaN"));
    }

    [Fact]
    public void AReadSpansBothDecodersWithoutSilenceOrDiscardedSamples()
    {
        var first = Stream(48000, [0, 32, 0, 64]);
        var second = Stream(48000, [0, 96, 0, 128]);
        var next = Song();
        var transitions = new List<Track>();
        using var source = new ContinuousAudioSource(first, AudioEngine.ToPlaybackSamples(first), (_, track, _) => transitions.Add(track));
        var prebuffered = new BufferedPlaybackSamples(AudioEngine.ToPlaybackSamples(second));
        Assert.True(source.TryStage(second, prebuffered, next, null));
        var samples = new float[5];
        Assert.Equal(3, source.Read(samples, 1, 3));
        Assert.Equal(new[] { 0f, .25f, .5f, .75f, 0f }, samples);
        Assert.Equal(next, Assert.Single(transitions));
        Assert.Equal(1, source.Read(samples, 0, samples.Length));
        Assert.Equal(-1f, samples[0]);
        Assert.Same(second, source.Current);
    }

    [Fact]
    public void DifferentRatesOrAReorderedQueueUseTheNormalTransition()
    {
        var first = Stream(48000, [0, 32]);
        using var incompatible = Stream(96000, [0, 64]);
        var discarded = Stream(48000, [0, 96]);
        using var source = new ContinuousAudioSource(first, AudioEngine.ToPlaybackSamples(first),
            (_, _, _) => throw new InvalidOperationException("No transition expected."));
        Assert.False(source.TryStage(incompatible, AudioEngine.ToPlaybackSamples(incompatible), Song(), null));
        Assert.True(source.TryStage(discarded, AudioEngine.ToPlaybackSamples(discarded), Song(), null));
        source.ClearStaged();
        Assert.Equal(1, source.Read(new float[2], 0, 2));
        Assert.Equal(0, source.Read(new float[2], 0, 2));
    }

    private static WaveStream Stream(int rate, byte[] data) =>
        new RawSourceWaveStream(new MemoryStream(data), new WaveFormat(rate, 16, 1));
}
