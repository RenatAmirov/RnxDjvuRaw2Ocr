namespace RnxDjvuRaw2Ocr.Core;

public static class ToolDiscovery
{
    public static ToolPaths Discover()
    {
        var roots = new List<string>();
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            roots.Add(Path.Combine(dir.FullName, "tools"));
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RnxDjvuRaw2Ocr", "tools"));
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DjVuLibre"));
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "DjVuLibre"));
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tesseract-OCR"));
        roots.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        var djvu = roots.SelectMany(r => new[] { r, Path.Combine(r, "djvulibre") })
            .FirstOrDefault(r => File.Exists(Path.Combine(r, "djvused.exe"))) ?? Path.Combine(AppContext.BaseDirectory, "tools", "djvulibre");
        var tess = roots.SelectMany(r => new[] { r, Path.Combine(r, "tesseract") })
            .Select(r => Path.Combine(r, "tesseract.exe")).FirstOrDefault(File.Exists)
            ?? Path.Combine(AppContext.BaseDirectory, "tools", "tesseract", "tesseract.exe");
        return new ToolPaths(djvu, tess, Path.Combine(Path.GetDirectoryName(tess)!, "tessdata"));
    }
}
