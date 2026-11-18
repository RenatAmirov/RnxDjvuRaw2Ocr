using System.Text;

namespace RnxDjvuRaw2Ocr.Core;

public static class DjvuTextWriter
{
    private readonly record struct Box(int X1, int Y1, int X2, int Y2);

    public static string Write(OcrPage page, PageGeometry geometry)
    {
        if (geometry.Width <= 0 || geometry.Height <= 0 || geometry.Rotation is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(geometry));
        var output = new StringBuilder();
        output.AppendLine($"(page 0 0 {geometry.Width} {geometry.Height}");
        foreach (var block in page.Words.GroupBy(w => w.Block))
        {
            Begin("region", block, 2);
            foreach (var paragraph in block.GroupBy(w => w.Paragraph))
            {
                Begin("para", paragraph, 4);
                foreach (var line in paragraph.GroupBy(w => w.Line))
                {
                    Begin("line", line, 6);
                    foreach (var word in line)
                    {
                        var box = Map(word);
                        output.AppendLine($"        (word {box.X1} {box.Y1} {box.X2} {box.Y2} {Quote(word.Text)})");
                    }
                    output.AppendLine("      )");
                }
                output.AppendLine("    )");
            }
            output.AppendLine("  )");
        }
        if (page.Words.Count == 0) output.AppendLine("  \"\"");
        output.AppendLine(")");
        return output.ToString();

        void Begin(string type, IEnumerable<OcrWord> words, int indent)
        {
            var boxes = words.Select(Map).ToArray();
            output.Append(' ', indent).AppendLine($"({type} {boxes.Min(b => b.X1)} {boxes.Min(b => b.Y1)} {boxes.Max(b => b.X2)} {boxes.Max(b => b.Y2)}");
        }

        Box Map(OcrWord word)
        {
            // ddjvu renders the default orientation. Text zones use the unrotated INFO dimensions.
            var renderedWidth = geometry.Rotation % 2 == 0 ? geometry.Width : geometry.Height;
            var renderedHeight = geometry.Rotation % 2 == 0 ? geometry.Height : geometry.Width;
            var x1 = (int)Math.Floor(word.Left * (double)renderedWidth / page.Width);
            var x2 = (int)Math.Ceiling((word.Left + word.Width) * (double)renderedWidth / page.Width);
            var y1 = renderedHeight - (int)Math.Ceiling((word.Top + word.Height) * (double)renderedHeight / page.Height);
            var y2 = renderedHeight - (int)Math.Floor(word.Top * (double)renderedHeight / page.Height);
            return geometry.Rotation switch
            {
                0 => new Box(x1, y1, x2, y2),
                1 => new Box(y1, geometry.Height - x2, y2, geometry.Height - x1),
                2 => new Box(geometry.Width - x2, geometry.Height - y2, geometry.Width - x1, geometry.Height - y1),
                3 => new Box(geometry.Width - y2, x1, geometry.Width - y1, x2),
                _ => throw new ArgumentOutOfRangeException(nameof(geometry))
            };
        }
    }

    public static string Quote(string value)
    {
        var output = new StringBuilder("\"");
        // ASCII scripts with UTF-8 octal escapes work independently of the Windows code page.
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            if (b == '"') output.Append("\\\"");
            else if (b == '\\') output.Append("\\\\");
            else if (b is >= 32 and <= 126) output.Append((char)b);
            else output.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
        }
        return output.Append('"').ToString();
    }
}
