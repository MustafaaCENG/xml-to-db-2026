using System.Xml;
using AdmXmlDb.Core.Entities;

namespace AdmXmlDb.Core;

/// <summary>
/// Builds dynamic target file paths from XML content using XPath components.
/// </summary>
public static class TargetPathBuilder
{
    /// <summary>
    /// Builds the target directory path from XML using task's StaticTargetDirectory and TargetDirectoryComponents.
    /// Returns null if no dynamic path can be built (use base OutputPath instead).
    /// </summary>
    public static string? BuildTargetDirectory(
        string xmlContent,
        IntegrationTask task)
    {
        var components = task.TargetDirectoryComponents?
            .OrderBy(c => c.SortOrder)
            .ToList();

        if (components == null || components.Count == 0)
            return null;

        var doc = new XmlDocument();
        doc.LoadXml(xmlContent);
        var nsManager = new XmlNamespaceManager(doc.NameTable);
        var ctx = doc.DocumentElement ?? (XmlNode)doc;

        var segments = new List<string>();

        if (!string.IsNullOrWhiteSpace(task.StaticTargetDirectory))
        {
            segments.Add(task.StaticTargetDirectory!.Trim().TrimEnd(Path.DirectorySeparatorChar, '/'));
        }

        foreach (var comp in components)
        {
            if (comp.ComponentType == TargetDirectoryComponentType.Static)
            {
                if (!string.IsNullOrWhiteSpace(comp.Value))
                    segments.Add(comp.Value.Trim());
            }
            else
            {
                try
                {
                    var node = ctx.SelectSingleNode(comp.Value, nsManager);
                    var value = node?.InnerText?.Trim();
                    if (!string.IsNullOrEmpty(value))
                        segments.Add(SanitizePathSegment(value));
                }
                catch
                {
                    // Skip invalid XPath
                }
            }
        }

        if (segments.Count == 0)
            return null;

        return Path.Combine([.. segments]);
    }

    /// <summary>
    /// Gets the final output file path: either dynamic directory + prefixed filename, or base OutputPath + prefixed filename.
    /// </summary>
    public static string GetOutputFilePath(
        string xmlContent,
        IntegrationTask task,
        string fileName)
    {
        var dir = BuildTargetDirectory(xmlContent, task) ?? task.OutputPath;
        var prefix = task.PrependToFileName ?? "";
        var finalName = prefix + fileName;
        return Path.Combine(dir, finalName);
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
            value = value.Replace(c, '_');
        return value.Trim();
    }
}
