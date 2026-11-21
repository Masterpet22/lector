using System.IO.Compression;
using System.Text;
using NebulaReader.Models;
using NebulaReader.Services;
using SkiaSharp;
using Xunit;

namespace NebulaReader.Tests;

public class CoverDetectionTests
{
    [Fact]
    public async Task CbzUsesComicInfoFrontCoverWithoutChangingReadingOrder()
    {
        using var archive = CreateArchive(
            ("page10.png", [10]), ("page2.png", [2]), ("page1.png", [1]),
            ("__MACOSX/._cover.png", [99]),
            ("ComicInfo.xml", Encoding.UTF8.GetBytes(
                "<ComicInfo><Pages><Page Image='1' Type='FrontCover'/></Pages></ComicInfo>")));
        using var source = new CbzBookSource("Comic", archive);

        await using var cover = await source.GetCoverThumbnailAsync();
        Assert.Equal(2, cover.ReadByte());
        Assert.Equal(3, source.PageCount);
        await using var firstPage = await source.GetPageStreamAsync(0);
        Assert.Equal(1, firstPage.ReadByte());
        await using var lastPage = await source.GetPageStreamAsync(2);
        Assert.Equal(10, lastPage.ReadByte());
    }

    [Fact]
    public async Task ComicInfoTakesPriorityOverAnImageNamedCover()
    {
        using var archive = CreateArchive(("cover.png", [1]), ("page.png", [2]),
            ("ComicInfo.xml", Encoding.UTF8.GetBytes(
                "<ComicInfo><Pages><Page Image='1' Type='FrontCover Story'/></Pages></ComicInfo>")));
        using var source = new CbzBookSource("Comic", archive);
        await using var cover = await source.GetCoverThumbnailAsync();
        Assert.Equal(2, cover.ReadByte());
    }

    [Theory]
    [InlineData("99-COVER.jpg")]
    [InlineData("99-front.jpg")]
    [InlineData("99-front_cover.jpg")]
    [InlineData("99-frontcover.jpg")]
    [InlineData("99-portada.png")]
    [InlineData("99-Carátula.png")]
    [InlineData("99-cubierta.webp")]
    [InlineData("folder\\99-cover001.jpeg")]
    public void DetectsCoverNamesAndSkipsBackAndInnerCovers(string coverName)
    {
        string[] names = ["00-advertisement.jpg", "01-back-cover.jpg", "02-contraportada.jpg",
            "03-cover-back.jpg", "04-portada-interior.jpg", "05-innercover.jpg", coverName];
        Assert.Equal(6, ComicCoverDetector.FindCoverPage(names, () => null));
    }

    [Theory]
    [InlineData("<ComicInfo><Pages><Page Image='999' Type='FrontCover'/></Pages></ComicInfo>")]
    [InlineData("<ComicInfo><Pages><Page Image='-1' Type='FrontCover'/></Pages></ComicInfo>")]
    [InlineData("<ComicInfo><Pages><Page Image='x' Type='FrontCover'/></Pages></ComicInfo>")]
    [InlineData("<ComicInfo><Pages><Page Image='0' Type='BackCover'/></Pages></ComicInfo>")]
    [InlineData("<ComicInfo><Pages><Page Type='FrontCover'/></Pages></ComicInfo>")]
    [InlineData("<Other><Pages><Page Image='0' Type='FrontCover'/></Pages></Other>")]
    [InlineData("<ComicInfo>malformed")]
    [InlineData("<!DOCTYPE ComicInfo [<!ENTITY test 'data'>]><ComicInfo>&test;</ComicInfo>")]
    public void InvalidOrMissingCoverMetadataFallsBackToFileNames(string xml)
    {
        Assert.Equal(1, ComicCoverDetector.FindCoverPage(["page1.png", "cover.png"],
            () => new MemoryStream(Encoding.UTF8.GetBytes(xml))));
    }

    [Fact]
    public void HandlesNamespacedMetadataAndSkipsInvalidDeclarations()
    {
        var xml = "<ComicInfo xmlns='urn:comicinfo'><Pages>" +
            "<Page Image='-1' Type='FrontCover'/><Page Image='2' Type='FrontCover'/>" +
            "</Pages></ComicInfo>";
        Assert.Equal(2, ComicCoverDetector.FindCoverPage(["1.png", "2.png", "3.png"],
            () => new MemoryStream(Encoding.UTF8.GetBytes(xml))));
    }

    [Fact]
    public void OversizedXmlAndUnreadableMetadataFallBackSafely()
    {
        var xml = "<ComicInfo><Notes>" + new string('x', (int)ComicCoverDetector.MaxComicInfoBytes) +
            "</Notes></ComicInfo>";
        Assert.Equal(1, ComicCoverDetector.FindCoverPage(["1.png", "cover.png"],
            () => new MemoryStream(Encoding.UTF8.GetBytes(xml))));
        Assert.Equal(1, ComicCoverDetector.FindCoverPage(["1.png", "cover.png"],
            () => throw new InvalidDataException("Bad archive entry")));
    }

    [Theory]
    [InlineData("__MACOSX/ComicInfo.xml")]
    [InlineData(".hidden/ComicInfo.xml")]
    public async Task IgnoresMetadataInHiddenDirectories(string metadataPath)
    {
        using var archive = CreateArchive(("1.png", [1]), ("cover.png", [2]),
            (metadataPath, Encoding.UTF8.GetBytes(
                "<ComicInfo><Pages><Page Image='0' Type='FrontCover'/></Pages></ComicInfo>")));
        using var source = new CbzBookSource("Comic", archive);
        await using var cover = await source.GetCoverThumbnailAsync();
        Assert.Equal(2, cover.ReadByte());
    }

    [Fact]
    public async Task RootMetadataTakesPriorityOverNestedMetadata()
    {
        using var archive = CreateArchive(("1.png", [1]), ("2.png", [2]),
            ("nested/ComicInfo.xml", Encoding.UTF8.GetBytes(
                "<ComicInfo><Pages><Page Image='0' Type='FrontCover'/></Pages></ComicInfo>")),
            ("COMICINFO.XML", Encoding.UTF8.GetBytes(
                "<ComicInfo><Pages><Page Image='1' Type='FrontCover'/></Pages></ComicInfo>")));
        using var source = new CbzBookSource("Comic", archive);
        await using var cover = await source.GetCoverThumbnailAsync();
        Assert.Equal(2, cover.ReadByte());
    }

    [Fact]
    public async Task FallsBackToFirstPageAndHandlesEmptyArchives()
    {
        using var archive = CreateArchive(("page2.jpg", [2]), ("page1.jpg", [1]));
        using var source = new CbzBookSource("Comic", archive);
        await using var cover = await source.GetCoverThumbnailAsync();
        Assert.Equal(1, cover.ReadByte());

        using var emptyArchive = CreateArchive(("readme.txt", [0]));
        using var emptySource = new CbzBookSource("Empty", emptyArchive);
        await using var emptyCover = await emptySource.GetCoverThumbnailAsync();
        Assert.Equal(0, emptySource.PageCount);
        Assert.Equal(0, emptyCover.Length);
    }

    [Fact]
    public async Task RegeneratesLegacyThumbnailUsingDetectedCoverAndReusesVersionedCache()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"nebula_covers_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var bookPath = Path.Combine(directory, "book.cbz");
            using (var data = CreateArchive(("01-intro.png", CreateImage(SKColors.Red)),
                ("99-cover.png", CreateImage(SKColors.Lime)) ))
            {
                File.WriteAllBytes(bookPath, data.ToArray());
            }
            var legacyPath = Path.Combine(directory, "book.jpg");
            File.WriteAllBytes(legacyPath, CreateImage(SKColors.Red));
            var factory = new CoverFactory();
            var service = new CoverThumbnailService(factory, directory);
            Assert.True(service.NeedsRefresh("book", legacyPath));

            var (path, count) = await service.GenerateCoverAsync("book", bookPath);
            Assert.Equal(2, count);
            Assert.NotNull(path);
            Assert.NotEqual(legacyPath, path);
            Assert.False(service.NeedsRefresh("book", path));
            using (var bitmap = SKBitmap.Decode(path))
            {
                var color = bitmap.GetPixel(10, 10);
                Assert.True(color.Green > 230 && color.Red < 20 && color.Blue < 20);
            }

            var firstWrite = File.GetLastWriteTimeUtc(path);
            var cached = await service.GenerateCoverAsync("book", bookPath);
            Assert.Equal(path, cached.ThumbnailPath);
            Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(path));
            service.Invalidate("book");
            Assert.False(File.Exists(path));
            Assert.False(File.Exists(legacyPath));
            Assert.True(service.NeedsRefresh("book", path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static MemoryStream CreateArchive(params (string Name, byte[] Data)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data) in entries)
            {
                using var entry = archive.CreateEntry(name).Open();
                entry.Write(data);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static byte[] CreateImage(SKColor color)
    {
        using var bitmap = new SKBitmap(60, 90);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private sealed class CoverFactory : IBookSourceFactory
    {
        public Task<IBookSource> CreateAsync(string path, CancellationToken ct = default) =>
            Task.FromResult<IBookSource>(new CbzBookSource(path));
        public Task<BookFormat> DetectFormatAsync(string path, CancellationToken ct = default) => Task.FromResult(BookFormat.Cbz);
        public BookFormat DetectFormat(ReadOnlySpan<byte> bytes, string? name = null) => BookFormat.Cbz;
        public Task<BookMetadata> ExtractMetadataAsync(string path, CancellationToken ct = default) =>
            Task.FromResult(new BookMetadata(null, []));
    }
}
