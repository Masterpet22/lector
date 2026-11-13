using System.IO.Compression;
using NebulaReader.Services;
using SkiaSharp;
using Xunit;

namespace NebulaReader.Tests;

public class CoreAndStressTests
{
    [Fact]
    public void NaturalSortComparer_OrdersNumericChunksBeforeLexicographic()
    {
        var input = new[]
        {
            "cap1/pagina_10.jpg",
            "cap1/pagina_2.jpg",
            "cap1/pagina_1.jpg",
            "cap1/pagina_02.jpg",
            "cap1/pagina_100.jpg"
        };

        var sorted = input.OrderBy(x => x, NaturalSortComparer.Instance).ToArray();

        Assert.Equal(
            [
                "cap1/pagina_1.jpg",
                "cap1/pagina_2.jpg",
                "cap1/pagina_02.jpg",
                "cap1/pagina_10.jpg",
                "cap1/pagina_100.jpg"
            ],
            sorted);
    }

    [Fact]
    public async Task CbzBookSource_FiltersHiddenAndMacOsx_AndReadsNaturallySortedPages()
    {
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "__MACOSX/._pagina_1.jpg", [1, 2, 3]);
            AddEntry(archive, ".DS_Store", [4, 5, 6]);
            AddEntry(archive, "ComicInfo.xml", "<ComicInfo />"u8.ToArray());
            AddEntry(archive, "pagina_10.png", [10]);
            AddEntry(archive, "pagina_2.jpg", [2]);
            AddEntry(archive, "pagina_1.webp", [1]);
        }

        zipStream.Position = 0;
        using var source = new CbzBookSource("TestComic", zipStream);

        Assert.Equal(3, source.PageCount);

        await using var s0 = await source.GetPageStreamAsync(0);
        await using var s1 = await source.GetPageStreamAsync(1);
        await using var s2 = await source.GetPageStreamAsync(2);

        Assert.Equal(1, s0.ReadByte());
        Assert.Equal(2, s1.ReadByte());
        Assert.Equal(10, s2.ReadByte());
    }

    [Fact]
    public void PageCacheService_SlidingWindow_DisposesEvicted4KBitmapsAcross320Pages()
    {
        using var cache = new PageCacheService();
        const string bookId = "stress-book-320";
        const int totalPages = 320;
        var createdBitmaps = new List<SKBitmap>(totalPages);

        for (var i = 0; i < totalPages; i++)
        {
            // Simular lectura secuencial con ventana [i - 1, i, i + 1] y decodificación de bitmaps nativos SkiaSharp
            var bitmap = new SKBitmap(3840, 2160, SKColorType.Rgba8888, SKAlphaType.Premul);
            createdBitmaps.Add(bitmap);

            cache.StoreBitmap(bookId, i, bitmap, centerPageIndex: i, radius: 1);

            if (i >= 2)
            {
                // La página (i - 2) debe haber sido expulsada y liberada deterministamente
                Assert.False(cache.ContainsPage(bookId, i - 2));
                Assert.Equal(IntPtr.Zero, createdBitmaps[i - 2].Handle);
            }

            Assert.True(cache.ContainsPage(bookId, i));
        }

        cache.ClearBook(bookId);

        foreach (var bitmap in createdBitmaps)
        {
            Assert.Equal(IntPtr.Zero, bitmap.Handle);
        }
    }

    [Fact]
    public async Task CbzBookSource_StressReads320PageArchiveSequentially()
    {
        using var zipStream = new MemoryStream();
        byte[] samplePngBytes;
        using (var bmp = new SKBitmap(64, 64))
        {
            bmp.Erase(SKColors.CornflowerBlue);
            using var img = SKImage.FromBitmap(bmp);
            using var encoded = img.Encode(SKEncodedImageFormat.Png, 100);
            samplePngBytes = encoded.ToArray();
        }

        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var i = 1; i <= 320; i++)
            {
                AddEntry(archive, $"page_{i}.png", samplePngBytes);
            }
        }

        zipStream.Position = 0;
        using var source = new CbzBookSource("Stress320", zipStream);
        Assert.Equal(320, source.PageCount);

        for (var i = 0; i < source.PageCount; i++)
        {
            await using var pageStream = await source.GetPageStreamAsync(i);
            using var decoded = SKBitmap.Decode(pageStream);
            Assert.NotNull(decoded);
            Assert.Equal(64, decoded.Width);
        }
    }

    private static void AddEntry(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var stream = entry.Open();
        stream.Write(content, 0, content.Length);
    }
}

