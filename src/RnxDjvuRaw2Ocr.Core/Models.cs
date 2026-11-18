namespace RnxDjvuRaw2Ocr.Core;

public sealed record ToolPaths(string DjvuDirectory, string TesseractExecutable, string TessdataDirectory)
{
    public string Djvused => Path.Combine(DjvuDirectory, "djvused.exe");
    public string Ddjvu => Path.Combine(DjvuDirectory, "ddjvu.exe");

    public void Validate()
    {
        foreach (var file in new[] { Djvused, Ddjvu, TesseractExecutable })
            if (!File.Exists(file))
                throw new FileNotFoundException($"Не найдена программа: {file}. Укажите путь в разделе «Инструменты».", file);
        if (!Directory.Exists(TessdataDirectory))
            throw new DirectoryNotFoundException($"Не найдена папка языков Tesseract: {TessdataDirectory}");
    }
}

public sealed record OcrRequest(string InputPath, string OutputPath, ToolPaths Tools,
    string Languages = "rus+eng", int PageSegmentationMode = 3, bool OverwriteOutput = false);

public sealed record OcrProgress(int CompletedPages, int TotalPages, string Message, string? PageText = null)
{
    public double Percent => TotalPages == 0 ? 0 : CompletedPages * 100.0 / TotalPages;
}

public sealed record OcrResult(string OutputPath, int Pages, int Words, int EmptyPages, TimeSpan Elapsed);
public sealed record PageGeometry(int Width, int Height, int Rotation = 0);
public sealed record OcrWord(int Block, int Paragraph, int Line, int Left, int Top, int Width, int Height, string Text);
public sealed record OcrPage(int Width, int Height, IReadOnlyList<OcrWord> Words)
{
    public string PlainText => string.Join(Environment.NewLine,
        Words.GroupBy(w => (w.Block, w.Paragraph, w.Line)).Select(line => string.Join(" ", line.Select(w => w.Text))));
}
