using System.Globalization;

namespace RnxDjvuRaw2Ocr.Core;

public static class TesseractTsvParser
{
    public static OcrPage Parse(string tsv)
    {
        using var reader = new StringReader(tsv.TrimStart('\uFEFF'));
        var header = reader.ReadLine();
        if (header != "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext")
            throw new FormatException("Tesseract вернул TSV с неизвестным форматом.");

        var width = 0;
        var height = 0;
        var words = new List<OcrWord>();
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var fields = line.Split('\t', 12);
            if (fields.Length != 12) throw new FormatException("Повреждена строка TSV Tesseract.");
            int Number(int index) => int.Parse(fields[index], CultureInfo.InvariantCulture);
            var level = Number(0);
            if (level == 1)
            {
                if (width != 0) throw new FormatException("Ожидалась одна страница TSV.");
                width = Number(8);
                height = Number(9);
            }
            else if (level == 5 && !string.IsNullOrWhiteSpace(fields[11]))
            {
                var left = Math.Clamp(Number(6), 0, Math.Max(width - 1, 0));
                var top = Math.Clamp(Number(7), 0, Math.Max(height - 1, 0));
                var wordWidth = Math.Min(Number(8), width - left);
                var wordHeight = Math.Min(Number(9), height - top);
                if (wordWidth > 0 && wordHeight > 0)
                    words.Add(new OcrWord(Number(2), Number(3), Number(4), left, top, wordWidth, wordHeight, fields[11].Trim()));
            }
        }
        if (width <= 0 || height <= 0) throw new FormatException("В TSV отсутствует размер страницы.");
        return new OcrPage(width, height, words);
    }
}
