
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace DoriDeck.Services;
public static class ScoreInfoReader
{
    private const string ScoreInfoEntry = "supplementary_data/scoreinfo/scoreinfo.xml";
    private const string PreviewEntry = "supplementary_data/preview/preview.png";

    private const long MaxPreviewBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string ReadJson(string zipPath)
    {
        using var zip = OpenScore(zipPath);

        var entry = FindEntry(zip, ScoreInfoEntry);

        if (entry == null)
            throw new FileNotFoundException("scoreinfo.xml not found in ZIP.");

        using var stream = entry.Open();
        var xml = XDocument.Load(stream);

        var data = ConvertElement(xml.Root!);

        return JsonSerializer.Serialize(data, JsonOptions);
    }


    public static byte[]? ReadPreviewPng(string zipPath)
    {
        using var zip = OpenScore(zipPath);

        var entry = FindEntry(zip, PreviewEntry);
        if (entry == null || entry.Length > MaxPreviewBytes)
            return null;

        var content = new byte[entry.Length];
        using var stream = entry.Open();
        stream.ReadExactly(content);
        return content;
    }

    private static ZipArchive OpenScore(string zipPath)
    {
        // Dorico may still hold the score open; don't demand exclusive access.
        var file = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        try
        {
            return new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive zip, string path) =>
        zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(path, StringComparison.OrdinalIgnoreCase));

    private static object ConvertElement(XElement element)
    {
        if (!element.HasElements)
            return element.Value;

        return element.Elements()
            .GroupBy(e => e.Name.LocalName)
            .ToDictionary(
                g => g.Key,
                g => g.Count() > 1 || 
                     (element.Name.LocalName == "Players" && g.Key == "Player") ||
                     (element.Name.LocalName == "Flows" && g.Key == "Flow")
                    ? (object)g.Select(ConvertElement).ToArray()
                    : ConvertElement(g.First())
            );
    }
}
