using System.Linq;
using Xunit;

namespace PrismaMusic.Tests;

public sealed class LibraryTagReviewTests
{
    [Fact]
    public void FindsMissingTagsAndAlbumVariants()
    {
        var clean = Song("one.flac");
        var split = clean with { Path = "two.flac", Album = "  RÍO  ", AlbumArtist = "Prisma", Number = 2 };
        var missing = clean with { Path = "three.flac", Artist = "Artista desconocido", Album = "Sin álbum" };
        var findings = LibraryTagReview.Find(new[] { clean, split, missing });
        Assert.Equal(2, findings.Count(item => item.Kind == TagIssueKind.Missing));
        var album = Assert.Single(findings, item => item.Kind == TagIssueKind.SplitAlbum);
        Assert.Equal(new[] { clean, split }, album.Tracks);
    }

    [Fact]
    public void DuplicateCandidatesRequireSeparateFilesAndCompatibleRecordingTags()
    {
        var first = Song("one.flac");
        var same = first with { Path = "two.m4a", Name = " ola ", Seconds = 182 };
        var live = first with { Path = "live.flac", Seconds = 240 };
        var otherTrack = first with { Path = "three.flac", Number = 3 };
        var otherAlbum = first with { Path = "other.flac", Album = "Marea" };
        var result = LibraryTagReview.Find(new[] { first, same, first, live, otherTrack, otherAlbum });
        var candidate = Assert.Single(result, item => item.Kind == TagIssueKind.PossibleDuplicate);
        Assert.Equal(new[] { first, same }, candidate.Tracks);
    }

    private static Track Song(string path) => new(path, "Ola", "Prisma", "Río", "", "Jazz", 2026, 1, 1, 180);
}
