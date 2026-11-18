using RnxDjvuRaw2Ocr.Core;

namespace RnxDjvuRaw2Ocr.Tests;

public sealed class FailureTests
{
    [Theory]
    [InlineData("ocr-failure")]
    [InlineData("cancel")]
    [InlineData("verification")]
    public async Task FailedOrCancelledRunsKeepExistingOutputAndCleanWorkspace(string failure)
    {
        var root = Path.Combine(Path.GetTempPath(), "RnxOcrTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var input = Path.Combine(root, "input.djvu");
        var output = Path.Combine(root, "output.djvu");
        await File.WriteAllTextAsync(input, "source");
        await File.WriteAllTextAsync(output, "previous result");
        foreach (var name in new[] { "ddjvu.exe", "djvused.exe", "tesseract.exe" })
            await File.WriteAllTextAsync(Path.Combine(root, name), "placeholder");
        var tools = new ToolPaths(root, Path.Combine(root, "tesseract.exe"), root);
        using var cancellation = new CancellationTokenSource();
        var fake = new FailingRunner(failure, cancellation);
        try
        {
            var task = new OcrService(fake).RunAsync(new OcrRequest(input, output, tools, "eng", OverwriteOutput: true), cancellationToken: cancellation.Token);
            if (failure == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            else if (failure == "verification") await Assert.ThrowsAsync<InvalidDataException>(() => task);
            else await Assert.ThrowsAsync<InvalidOperationException>(() => task);
            Assert.Equal("source", await File.ReadAllTextAsync(input));
            Assert.Equal("previous result", await File.ReadAllTextAsync(output));
            Assert.False(Directory.Exists(fake.Workspace));
            Assert.Empty(Directory.GetFiles(root, ".rnx-ocr-*"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class FailingRunner(string failure, CancellationTokenSource cancellation) : IProcessRunner
    {
        public string Workspace { get; private set; } = "";

        public async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> args, string workingDirectory,
            CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            if (args.Contains("--list-langs")) return new ProcessResult("List of available languages (1):\neng\n", "");
            Workspace = workingDirectory;
            if (args.Any(a => a.StartsWith("save-bundled", StringComparison.Ordinal)))
                await File.WriteAllTextAsync(Path.Combine(workingDirectory, "document.djvu"), "working copy", cancellationToken);
            if (args.Contains("n")) return new ProcessResult("1\n", "");
            if (args.Contains("size")) return new ProcessResult("width=100 height=200\n", "");
            if (Path.GetFileName(executable) == "tesseract.exe")
            {
                if (failure == "cancel") { cancellation.Cancel(); cancellationToken.ThrowIfCancellationRequested(); }
                if (failure == "ocr-failure") throw new InvalidOperationException("Simulated OCR error");
                await File.WriteAllTextAsync(Path.Combine(workingDirectory, "page.tsv"),
                    "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n" +
                    "1\t1\t0\t0\t0\t0\t0\t0\t100\t200\t-1\t\n" +
                    "5\t1\t1\t1\t1\t1\t10\t20\t30\t40\t99\texpected\n", cancellationToken);
            }
            if (args.Contains("select 1; print-pure-txt")) return new ProcessResult("wrong saved text", "");
            return new ProcessResult("", "");
        }
    }
}
