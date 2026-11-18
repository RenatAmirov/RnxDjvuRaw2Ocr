using RnxDjvuRaw2Ocr.Core;

namespace RnxDjvuRaw2Ocr.Tests;

public sealed class TextLayerTests
{
    private const string Header = "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n";

    [Fact]
    public void TsvPreservesWordsReadingOrderAndLineBoundaries()
    {
        var page = TesseractTsvParser.Parse("\uFEFF" + Header +
            "1\t1\t0\t0\t0\t0\t0\t0\t1000\t2000\t-1\t\n" +
            "5\t1\t1\t1\t1\t1\t10\t20\t100\t30\t96.4\tПривет\n" +
            "5\t1\t1\t1\t1\t2\t120\t20\t70\t30\t90\tмир\n" +
            "5\t1\t1\t1\t2\t1\t10\t60\t100\t30\t89\tДальше\n");
        Assert.Equal(1000, page.Width);
        Assert.Equal(2000, page.Height);
        Assert.Equal(3, page.Words.Count);
        Assert.Equal($"Привет мир{Environment.NewLine}Дальше", page.PlainText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unrecognized header")]
    [InlineData(Header)]
    public void InvalidTsvFailsInsteadOfProducingAnEmptyLayer(string tsv) => Assert.Throws<FormatException>(() => TesseractTsvParser.Parse(tsv));

    [Theory]
    [InlineData(0, 100, 200, "10 150 40 180")]
    [InlineData(1, 200, 100, "50 160 80 190")]
    [InlineData(2, 100, 200, "60 20 90 50")]
    [InlineData(3, 200, 100, "20 10 50 40")]
    public void TextBoxesMapBackToUnrotatedDjvuCoordinates(int rotation, int width, int height, string expected)
    {
        var page = new OcrPage(width, height, [new OcrWord(1, 1, 1, 10, 20, 30, 30, "word")]);
        var layer = DjvuTextWriter.Write(page, new PageGeometry(100, 200, rotation));
        Assert.Contains($"(word {expected} \"word\")", layer);
        Assert.StartsWith("(page 0 0 100 200", layer);
    }

    [Fact]
    public void Utf8AndSyntaxAreEscapedAsPortableAscii()
    {
        var quoted = DjvuTextWriter.Quote("Я\"\\\n");
        Assert.Equal("\"\\320\\257\\\"\\\\\\012\"", quoted);
        Assert.All(quoted, c => Assert.InRange((int)c, 32, 126));
    }

    [Fact]
    public void BlankPageGetsAnExplicitEmptyLayer()
    {
        Assert.Contains("\"\"", DjvuTextWriter.Write(new OcrPage(10, 20, []), new PageGeometry(10, 20)));
    }

    [Fact]
    public async Task InputCannotBeOverwrittenEvenWithOverwriteEnabled()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".djvu");
        await File.WriteAllTextAsync(path, "original");
        try
        {
            var tools = new ToolPaths("missing", "missing", "missing");
            await Assert.ThrowsAsync<ArgumentException>(() => new OcrService().RunAsync(new OcrRequest(path, path, tools, OverwriteOutput: true)));
            Assert.Equal("original", await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ProcessCancellationKillsTheChildPromptly()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(350));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ProcessRunner().RunAsync(
            Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            ["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 60"], Path.GetTempPath(), cancel.Token));
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10));
    }
}
