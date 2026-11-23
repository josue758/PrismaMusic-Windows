using System;
using System.IO;
using System.Linq;

namespace PrismaMusic;

internal static class FlacTagWriter
{
    internal static void Write(string path, LibraryTagEdit edit, byte[] cover = null)
    {
        var source = new FileInfo(path);
        if ((source.Attributes & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) != 0)
            throw new IOException("El archivo no permite escritura directa.");
        long length = source.Length;
        DateTime modified = source.LastWriteTimeUtc;
        string temporary = Path.Combine(source.DirectoryName, $".prisma-tag-{Guid.NewGuid():N}.flac");
        try
        {
            File.Copy(source.FullName, temporary);
            using (var file = AudioMetadata.Open(temporary))
            {
                if (file is not TagLib.Flac.File) throw new IOException("El archivo no contiene audio FLAC.");
                var tag = file.Tag;
                if (edit.Title != null) tag.Title = edit.Title;
                if (edit.Artist != null) tag.Performers = Values(edit.Artist);
                if (edit.Album != null) tag.Album = edit.Album;
                if (edit.AlbumArtist != null) tag.AlbumArtists = Values(edit.AlbumArtist);
                if (edit.Genre != null) tag.Genres = Values(edit.Genre);
                if (edit.Year.HasValue) tag.Year = edit.Year.Value;
                if (edit.Disc.HasValue) tag.Disc = edit.Disc.Value;
                if (edit.Number.HasValue) tag.Track = edit.Number.Value;
                if (cover != null)
                {
                    var picture = new TagLib.Picture(new TagLib.ByteVector(cover)) { Type = TagLib.PictureType.FrontCover };
                    tag.Pictures = new TagLib.IPicture[] { picture }
                        .Concat(tag.Pictures.Where(existing => existing.Type != TagLib.PictureType.FrontCover)).ToArray();
                }
                file.Save();
            }
            source.Refresh();
            if (source.Length != length || source.LastWriteTimeUtc != modified)
                throw new IOException("El archivo cambió durante la edición.");
            File.Replace(temporary, source.FullName, null);
        }
        finally { File.Delete(temporary); }
    }

    private static string[] Values(string value) => value.Length == 0 ? Array.Empty<string>() : new[] { value };
}
