using System.Xml;

namespace AdmXmlDb.Core;

/// <summary>
/// Input validation helpers shared between UI and tests.
/// </summary>
public static class InputValidator
{
    /// <summary>
    /// Returns true if the given XPath string is syntactically valid.
    /// </summary>
    public static bool IsValidXPath(string xpath)
    {
        if (string.IsNullOrWhiteSpace(xpath)) return false;
        try
        {
            var doc = new XmlDocument();
            doc.CreateNavigator()!.Compile(xpath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns true if the path contains directory traversal sequences (../).
    /// </summary>
    public static bool ContainsPathTraversal(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("../") || normalized.Contains("/..") || normalized == ".." || normalized.EndsWith("/..");
    }
}
