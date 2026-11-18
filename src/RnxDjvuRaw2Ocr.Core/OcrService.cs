using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace RnxDjvuRaw2Ocr.Core;

public sealed partial class OcrService(IProcessRunner? processRunner = null)
{
    private readonly IProcessRunner runner = processRunner ?? new ProcessRunner();

    public async Task<IReadOnlyList<string>> GetLanguagesAsync(ToolPaths tools, CancellationToken cancellationToken = default)
    {
        tools.Validate();
        var result = await runner.RunAsync(tools.TesseractExecutable,
            ["--tessdata-dir", NativePath(tools.TessdataDirectory), "--list-langs"],
            tools.TessdataDirectory, cancellationToken, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        return result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().TrimStart('\uFEFF'))
            .Where(s => LanguageCode().IsMatch(s) && s != "osd").Order().ToArray();
    }

    public async Task<OcrResult> RunAsync(OcrRequest request, IProgress<OcrProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var input = Path.GetFullPath(request.InputPath);
        var output = Path.GetFullPath(request.OutputPath);
        ValidateRequest(request, input, output);
        request.Tools.Validate();
        var available = await GetLanguagesAsync(request.Tools, cancellationToken).ConfigureAwait(false);
        var missing = request.Languages.Split('+').Except(available).ToArray();
        if (missing.Length != 0)
            throw new InvalidOperationException($"Не установлены языки OCR: {string.Join(", ", missing)}. Добавьте соответствующие .traineddata в папку tessdata.");

        var clock = Stopwatch.StartNew();
        var work = Path.Combine(Path.GetTempPath(), "RnxDjvuOcr", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        // Publish on the same volume as the destination only after a complete, verified run.
        var pending = Path.Combine(Path.GetDirectoryName(output)!, $".rnx-ocr-{Guid.NewGuid():N}.tmp");
        try
        {
            progress?.Report(new(0, 0, "Подготовка копии документа…"));
            // save-bundled also resolves indirect DjVu components relative to the original index.
            await Djvused(input, $"save-bundled {DjvuTextWriter.Quote(Path.Combine(work, "document.djvu").Replace('\\', '/'))}");
            var countText = (await Djvused("document.djvu", "n")).StandardOutput.Trim().TrimStart('\uFEFF');
            if (!int.TryParse(countText, CultureInfo.InvariantCulture, out var count) || count <= 0)
                throw new FormatException("Не удалось определить число страниц DJVU.");
            var sizeText = (await Djvused("document.djvu", "size")).StandardOutput;
            var geometries = GeometryPattern().Matches(sizeText).Select(match => new PageGeometry(
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                match.Groups[3].Success ? int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) : 0)).ToArray();
            if (geometries.Length != count) throw new FormatException("Не удалось прочитать размеры всех страниц DJVU.");

            var script = new StringBuilder();
            var words = 0;
            var emptyPages = 0;
            var expectedTextHashes = new List<byte[]>();
            for (var pageNumber = 1; pageNumber <= count; pageNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new(pageNumber - 1, count, $"Страница {pageNumber} из {count}: подготовка изображения…"));
                await runner.RunAsync(request.Tools.Ddjvu,
                    ["-format=tiff", "-1", $"-page={pageNumber}", "document.djvu", "page.tif"], work, cancellationToken).ConfigureAwait(false);
                progress?.Report(new(pageNumber - 1, count, $"Страница {pageNumber} из {count}: распознавание…"));
                await runner.RunAsync(request.Tools.TesseractExecutable,
                    ["page.tif", "page", "--tessdata-dir", NativePath(request.Tools.TessdataDirectory),
                        "-l", request.Languages, "--oem", "1", "--psm", request.PageSegmentationMode.ToString(CultureInfo.InvariantCulture),
                        "-c", "tessedit_create_tsv=1"], work, cancellationToken).ConfigureAwait(false);
                var page = TesseractTsvParser.Parse(await File.ReadAllTextAsync(Path.Combine(work, "page.tsv"), Encoding.UTF8, cancellationToken).ConfigureAwait(false));
                expectedTextHashes.Add(TextHash(page.PlainText));
                var textFile = $"text-{pageNumber:D6}.txt";
                await File.WriteAllTextAsync(Path.Combine(work, textFile), DjvuTextWriter.Write(page, geometries[pageNumber - 1]),
                    Encoding.ASCII, cancellationToken).ConfigureAwait(false);
                script.AppendLine($"select {pageNumber}; set-txt {textFile}");
                words += page.Words.Count;
                if (page.Words.Count == 0) emptyPages++;
                File.Delete(Path.Combine(work, "page.tif"));
                File.Delete(Path.Combine(work, "page.tsv"));
                progress?.Report(new(pageNumber, count, $"Страница {pageNumber} из {count}: {page.Words.Count} слов", page.PlainText));
            }
            progress?.Report(new(count, count, "Добавление OCR-слоя и проверка результата…"));
            script.AppendLine("save");
            await File.WriteAllTextAsync(Path.Combine(work, "apply.dsed"), script.ToString(), Encoding.ASCII, cancellationToken).ConfigureAwait(false);
            await runner.RunAsync(request.Tools.Djvused, ["document.djvu", "-f", "apply.dsed"], work, cancellationToken).ConfigureAwait(false);
            var verifiedCount = (await Djvused("document.djvu", "n")).StandardOutput.Trim().TrimStart('\uFEFF');
            if (verifiedCount != count.ToString(CultureInfo.InvariantCulture))
                throw new InvalidDataException("После сохранения изменилось число страниц DJVU.");
            // Read text back page by page to avoid retaining an entire book in memory.
            for (var i = 1; i <= count; i++)
            {
                var text = (await runner.RunAsync(request.Tools.Djvused,
                    ["document.djvu", "-u", "-e", $"select {i}; print-pure-txt"], work, cancellationToken).ConfigureAwait(false)).StandardOutput;
                if (!TextHash(text).SequenceEqual(expectedTextHashes[i - 1]))
                    throw new InvalidDataException($"Текстовый слой страницы {i} не прошёл проверку после сохранения.");
            }
            await CopyAsync(Path.Combine(work, "document.djvu"), pending, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(output) && request.OverwriteOutput) File.Replace(pending, output, null);
            else File.Move(pending, output); // Does not overwrite a file created by another process.
            return new OcrResult(output, count, words, emptyPages, clock.Elapsed);
        }
        finally
        {
            TryDeleteFile(pending);
            try { Directory.Delete(work, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        Task<ProcessResult> Djvused(string file, string command) => runner.RunAsync(request.Tools.Djvused,
            [NativePath(file), "-e", command], work, cancellationToken);
    }

    private static void ValidateRequest(OcrRequest request, string input, string output)
    {
        if (!File.Exists(input)) throw new FileNotFoundException("Исходный DJVU-файл не найден.", input);
        if (!new[] { ".djvu", ".djv" }.Contains(Path.GetExtension(input), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Выберите файл .djvu или .djv.");
        if (!new[] { ".djvu", ".djv" }.Contains(Path.GetExtension(output), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Выходной файл должен иметь расширение .djvu или .djv.");
        if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Исходный и выходной файлы должны отличаться.");
        if (!Directory.Exists(Path.GetDirectoryName(output)))
            throw new DirectoryNotFoundException("Папка для выходного файла не существует.");
        if (File.Exists(output) && !request.OverwriteOutput)
            throw new IOException("Выходной файл уже существует. Выберите другое имя.");
        if (!LanguageCombination().IsMatch(request.Languages)) throw new ArgumentException("Укажите языки в формате rus+eng.");
        if (request.PageSegmentationMode is not (3 or 6 or 11)) throw new ArgumentException("Неподдерживаемый режим разметки.");
    }

    private static async Task CopyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void TryDeleteFile(string file)
    {
        try { File.Delete(file); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static byte[] TextHash(string text)
    {
        // DjVu inserts structural separators and a Windows BOM on export. Compare actual word content.
        var content = string.Concat(text.Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c) && c != '\uFEFF'));
        return System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(content));
    }

    internal static string NativePath(string path)
    {
        if (!OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(path)) return path;
        var buffer = new StringBuilder(32768);
        return GetShortPathName(path, buffer, buffer.Capacity) is > 0 and < 32768 ? buffer.ToString() : path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathName(string longPath, StringBuilder shortPath, int bufferLength);

    [GeneratedRegex(@"width=(\d+)\s+height=(\d+)(?:\s+rotation=(\d+))?")]
    private static partial Regex GeometryPattern();
    [GeneratedRegex(@"^[a-zA-Z0-9_]+(?:/[a-zA-Z0-9_]+)?$")]
    private static partial Regex LanguageCode();
    [GeneratedRegex(@"^[a-zA-Z0-9_]+(?:/[a-zA-Z0-9_]+)?(?:\+[a-zA-Z0-9_]+(?:/[a-zA-Z0-9_]+)?)*$")]
    private static partial Regex LanguageCombination();
}
