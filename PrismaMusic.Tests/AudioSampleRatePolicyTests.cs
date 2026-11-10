using System;
using PrismaMusic;
using Xunit;

namespace PrismaMusic.Tests;

public sealed class AudioSampleRatePolicyTests
{
    [Theory]
    [InlineData(44100)]
    [InlineData(48000)]
    [InlineData(96000)]
    [InlineData(192000)]
    public void AutomaticExclusiveOutputFollowsEachTrack(int trackRate)
    {
        Assert.Equal(trackRate, AudioSampleRatePolicy.SelectOutputRate(trackRate, 0, exclusive: true, strictDirect: false));
    }

    [Fact]
    public void ManualExclusiveOutputUsesTheRequestedRate()
    {
        Assert.Equal(96000, AudioSampleRatePolicy.SelectOutputRate(44100, 96000, exclusive: true, strictDirect: false));
    }

    [Fact]
    public void SharedOutputDoesNotApplyAnExclusiveRateRequest()
    {
        Assert.Equal(44100, AudioSampleRatePolicy.SelectOutputRate(44100, 96000, exclusive: false, strictDirect: false));
    }

    [Fact]
    public void DirectPcmAlwaysPreservesTheTrackRate()
    {
        Assert.Equal(44100, AudioSampleRatePolicy.SelectOutputRate(44100, 96000, exclusive: true, strictDirect: true));
    }

    [Theory]
    [InlineData(44100)]
    [InlineData(48000)]
    [InlineData(88200)]
    [InlineData(96000)]
    [InlineData(176400)]
    [InlineData(192000)]
    [InlineData(352800)]
    [InlineData(384000)]
    public void CommonRatesAreValidRequests(int sampleRate)
    {
        Assert.True(AudioSampleRatePolicy.IsValidRequest(sampleRate));
    }

    [Fact]
    public void AutomaticRateIsAValidRequest()
    {
        Assert.True(AudioSampleRatePolicy.IsValidRequest(0));
    }

    [Fact]
    public void UnsupportedRatesAreNotValidRequests()
    {
        Assert.False(AudioSampleRatePolicy.IsValidRequest(96001));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AudioSampleRatePolicy.SelectOutputRate(44100, 96001, exclusive: true, strictDirect: false));
    }

    [Fact]
    public void MissingTrackRateIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AudioSampleRatePolicy.SelectOutputRate(0, 0, exclusive: true, strictDirect: false));
    }
}
