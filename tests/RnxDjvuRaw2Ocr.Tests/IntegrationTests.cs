using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using RnxDjvuRaw2Ocr.Core;

namespace RnxDjvuRaw2Ocr.Tests;

[Trait("Category", "Integration")]
public sealed class IntegrationTests
{
    [Fact]
    public async Task RealOcrPreservesImagesAndAddsSearchableRussianAndEnglishOnRotatedAndBlankPages()
    {
        var tools = ToolDiscovery.Discover();
        tools.Validate();
        var root = FindRoot();
        var work = Path.Combine(root, "artifacts", "integration");
        Directory.CreateDirectory(work);
        var runner = new ProcessRunner();
        var source = Path.Combine(work, "source.djvu");
        for (var rotation = 0; rotation < 4; rotation++)
        {
            using var image = CreatePage();
            image.RotateFlip(rotation switch { 1 => RotateFlipType.Rotate90FlipNone, 2 => RotateFlipType.Rotate180FlipNone, 3 => RotateFlipType.Rotate270FlipNone, _ => RotateFlipType.RotateNoneFlipNone });
            image.Save(Path.Combine(work, $"page-{rotation}.jpg"), ImageFormat.Jpeg);
            await Run("c44.exe", "-dpi", "300", $"page-{rotation}.jpg", $"page-{rotation}.djvu");
            await Run("djvused.exe", $"page-{rotation}.djvu", "-e", $"set-rotation {rotation}", "-s");
        }
        using (var blank = new Bitmap(1600, 1000))
        {
            using var graphics = Graphics.FromImage(blank);
            graphics.Clear(Color.White);
            blank.Save(Path.Combine(work, "blank.jpg"), ImageFormat.Jpeg);
        }
        await Run("c44.exe", "blank.jpg", "blank.djvu");
        await Run("djvm.exe", "-c", "source.djvu", "page-0.djvu", "page-1.djvu", "page-2.djvu", "page-3.djvu", "blank.djvu");
        await File.WriteAllTextAsync(Path.Combine(work, "outline.txt"), "(bookmarks (\"Chapter one\" \"#1\"))");
        await Run("djvused.exe", "source.djvu", "-e", "set-outline outline.txt; select 1; set-page-title cover", "-s");
        var originalHash = await Hash(source);
        Assert.DoesNotContain("TXT", (await Run("djvudump.exe", "source.djvu")).StandardOutput);

        // Exercise Unicode file names, spaces and an indirect input document too.
        var unicodeDirectory = Path.Combine(work, "Книги для проверки");
        Directory.CreateDirectory(unicodeDirectory);
        var unicodeInput = Path.Combine(unicodeDirectory, "Исходная книга.djvu");
        File.Copy(source, unicodeInput, overwrite: true);
        var output = Path.Combine(unicodeDirectory, $"Книга с текстом-{Guid.NewGuid():N}.djvu");
        var progress = new List<OcrProgress>();
        var result = await new OcrService().RunAsync(new OcrRequest(unicodeInput, output, tools), new InlineProgress(progress.Add));
        Assert.Equal(5, result.Pages);
        Assert.Equal(1, result.EmptyPages);
        Assert.True(result.Words >= 20);
        Assert.Equal(originalHash, await Hash(source));
        Assert.Equal(originalHash, await Hash(unicodeInput));
        Assert.Contains(progress, p => p.CompletedPages == 5 && p.TotalPages == 5);
        var sourceOutline = (await Run("djvused.exe", source, "-e", "print-outline")).StandardOutput;
        var outputOutline = (await Run("djvused.exe", output, "-e", "print-outline")).StandardOutput;
        Assert.Equal(sourceOutline, outputOutline);
        Assert.Contains("cover", (await Run("djvused.exe", output, "-e", "ls")).StandardOutput);
        for (var page = 1; page <= 5; page++)
        {
            var text = (await Run("djvused.exe", output, "-u", "-e", $"select {page}; print-pure-txt")).StandardOutput;
            if (page <= 4)
            {
                Assert.Contains("Every word", text, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("распознавания", text, StringComparison.OrdinalIgnoreCase);
            }
            var layer = (await Run("djvused.exe", output, "-e", $"select {page}; print-txt")).StandardOutput;
            Assert.Contains("(page", layer);
            await Run("ddjvu.exe", "-format=ppm", "-1", $"-page={page}", source, "before.ppm");
            await Run("ddjvu.exe", "-format=ppm", "-1", $"-page={page}", output, "after.ppm");
            Assert.Equal(await Hash(Path.Combine(work, "before.ppm")), await Hash(Path.Combine(work, "after.ppm")));
            // Compressed image payloads must also remain byte-identical.
            await Run("djvused.exe", source, "-e", $"select {page}; save-page-with before.djvu");
            await Run("djvused.exe", output, "-e", $"select {page}; save-page-with after.djvu");
            Assert.Equal(ImageChunks(Path.Combine(work, "before.djvu")), ImageChunks(Path.Combine(work, "after.djvu")));
        }

        var indirectDir = Path.Combine(work, "indirect");
        Directory.CreateDirectory(indirectDir);
        await Run("djvused.exe", source, "-e", "save-indirect indirect/index.djvu");
        var indirectOutput = Path.Combine(work, $"indirect-ocr-{Guid.NewGuid():N}.djvu");
        var indirectResult = await new OcrService().RunAsync(new OcrRequest(Path.Combine(indirectDir, "index.djvu"), indirectOutput, tools));
        Assert.Equal(5, indirectResult.Pages);
        Assert.Contains("Every word", (await Run("djvused.exe", indirectOutput, "-u", "-e", "select 1; print-pure-txt")).StandardOutput);
        await File.WriteAllTextAsync(Path.Combine(work, "latest-output.txt"), output);

        Task<ProcessResult> Run(string name, params string[] arguments) => runner.RunAsync(Path.Combine(tools.DjvuDirectory, name), arguments, work, CancellationToken.None);
    }

    private static Bitmap CreatePage()
    {
        var image = new Bitmap(1600, 1000);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.White);
        using var font = new Font("Arial", 46, FontStyle.Regular, GraphicsUnit.Pixel);
        graphics.DrawString("SEARCHABLE BOOK PAGE", font, Brushes.Black, 100, 160);
        graphics.DrawString("Проверка распознавания текста", font, Brushes.Black, 100, 280);
        graphics.DrawString("Every word has a position. 12345", font, Brushes.Black, 100, 400);
        return image;
    }

    private static async Task<string> Hash(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    private static string ImageChunks(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var result = new List<string>();
        for (var offset = 16; offset + 8 <= bytes.Length;)
        {
            var id = System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
            var size = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset + 4, 4));
            if (id is not ("TXTa" or "TXTz"))
                result.Add(id + ":" + Convert.ToHexString(SHA256.HashData(bytes.AsSpan(offset + 8, size))));
            offset += 8 + size + (size & 1);
        }
        return string.Join(";", result);
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "RnxDjvuRaw2Ocr.sln"))) return dir.FullName;
        throw new DirectoryNotFoundException("Не найден корень проекта.");
    }

    private sealed class InlineProgress(Action<OcrProgress> report) : IProgress<OcrProgress>
    {
        public void Report(OcrProgress value) => report(value);
    }
}
