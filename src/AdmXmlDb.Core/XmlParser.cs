using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using AdmXmlDb.Core.Entities;

namespace AdmXmlDb.Core;

/// <summary>
/// Parses XML files and extracts values based on XPath mappings.
/// Supports multi-record documents, Find/Replace, literal values, and DefaultValue.
/// </summary>
public static class XmlParser
{
    /// <summary>
    /// Extracts one or more rows of values from XML. When MultiRecordRootXPath is set,
    /// returns one row per matching node; otherwise returns a single row.
    /// </summary>
    public static IReadOnlyList<Dictionary<string, object?>> ExtractAllRows(
        string xmlContent,
        IntegrationTask task)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xmlContent);
        var nsManager = new XmlNamespaceManager(doc.NameTable);
        var mappings = task.Mappings.OrderBy(m => m.SortOrder).ToList();

        if (!string.IsNullOrWhiteSpace(task.MultiRecordRootXPath))
        {
            var contextNodes = doc.SelectNodes(task.MultiRecordRootXPath, nsManager);
            if (contextNodes == null || contextNodes.Count == 0)
                return [];

            var rows = new List<Dictionary<string, object?>>();
            foreach (XmlNode? ctx in contextNodes)
            {
                if (ctx == null) continue;
                var row = ExtractValuesFromContext(ctx, doc, mappings, nsManager, task.TextToRemoveInXPath);
                if (row.Count > 0)
                    rows.Add(row);
            }
            return rows;
        }

        var singleRow = ExtractValuesFromContext(doc.DocumentElement ?? (XmlNode)doc, doc, mappings, nsManager, task.TextToRemoveInXPath);
        return singleRow.Count > 0 ? [singleRow] : [];
    }

    /// <summary>
    /// Legacy: Extracts a single row (first record when multi-record). For backward compatibility.
    /// </summary>
    public static Dictionary<string, object?> ExtractValues(string xmlContent, IReadOnlyList<TaskMapping> mappings)
    {
        var rows = ExtractAllRows(xmlContent, new IntegrationTask
        {
            Mappings = mappings.ToList(),
            MultiRecordRootXPath = null,
            TextToRemoveInXPath = null
        });
        return rows.Count > 0 ? rows[0] : new Dictionary<string, object?>();
    }

    /// <summary>
    /// Extracts values using ad-hoc XPath/ColumnName/Type tuples (for simulation).
    /// </summary>
    public static Dictionary<string, object?> ExtractValues(
        string xmlContent,
        IReadOnlyList<(string XPath, string ColumnName, SqlDataType DataType)> mappings)
    {
        var result = new Dictionary<string, object?>();
        var doc = new XmlDocument();
        doc.LoadXml(xmlContent);
        var nsManager = new XmlNamespaceManager(doc.NameTable);
        var ctx = doc.DocumentElement ?? (XmlNode)doc;

        foreach (var (xpath, columnName, dataType) in mappings)
        {
            try
            {
                var node = ctx.SelectSingleNode(xpath, nsManager);
                var value = GetSafeTextValue(node);
                result[columnName] = ConvertToTypedValue(value, dataType);
            }
            catch
            {
                result[columnName] = DBNull.Value;
            }
        }

        return result;
    }

    /// <summary>
    /// Tests an XPath against XML and returns the first match value.
    /// </summary>
    public static string? TestXPath(string xmlContent, string xpath)
    {
        if (string.IsNullOrWhiteSpace(xpath)) return null;
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xmlContent);
            var nsManager = new XmlNamespaceManager(doc.NameTable);
            var node = doc.SelectSingleNode(xpath, nsManager);
            return GetSafeTextValue(node);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the count of nodes matching the XPath.
    /// </summary>
    public static int GetXPathNodeCount(string xmlContent, string xpath)
    {
        if (string.IsNullOrWhiteSpace(xpath)) return 0;
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xmlContent);
            var nsManager = new XmlNamespaceManager(doc.NameTable);
            var nodes = doc.SelectNodes(xpath, nsManager);
            return nodes?.Count ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private static readonly Regex TemplateXPathPattern = new(@"\{(.+?)\}", RegexOptions.Compiled);

    /// <summary>
    /// Safely extracts text from an XmlNode. If the node is a container element
    /// (has child elements), returns only direct text content instead of the
    /// concatenated InnerText of all descendants which would produce garbage values.
    /// </summary>
    private static string? GetSafeTextValue(XmlNode? node)
    {
        if (node == null) return null;

        if (node is XmlText or XmlCDataSection or XmlAttribute or XmlComment)
            return node.Value?.Trim();

        if (node is XmlElement element)
        {
            bool hasChildElements = false;
            foreach (XmlNode child in element.ChildNodes)
            {
                if (child is XmlElement)
                {
                    hasChildElements = true;
                    break;
                }
            }

            if (!hasChildElements)
                return element.InnerText?.Trim();

            // Container element: collect only direct text nodes to avoid
            // returning a concatenation of all descendant values.
            var directText = new System.Text.StringBuilder();
            foreach (XmlNode child in element.ChildNodes)
            {
                if (child is XmlText or XmlCDataSection)
                    directText.Append(child.Value);
            }
            var result = directText.ToString().Trim();
            return string.IsNullOrEmpty(result) ? null : result;
        }

        return node.InnerText?.Trim();
    }

    private static Dictionary<string, object?> ExtractValuesFromContext(
        XmlNode contextNode,
        XmlDocument document,
        IReadOnlyList<TaskMapping> mappings,
        XmlNamespaceManager nsManager,
        string? textToRemove)
    {
        var result = new Dictionary<string, object?>();

        foreach (var mapping in mappings)
        {
            try
            {
                string? value;

                if (!string.IsNullOrWhiteSpace(mapping.ValueTemplate))
                {
                    value = TemplateXPathPattern.Replace(mapping.ValueTemplate, match =>
                    {
                        var xp = match.Groups[1].Value;
                        try
                        {
                            var n = contextNode.SelectSingleNode(xp, nsManager);
                            return GetSafeTextValue(n) ?? "";
                        }
                        catch { return ""; }
                    });
                    if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(mapping.DefaultValue))
                        value = mapping.DefaultValue;
                }
                else if (mapping.IsLiteral || (string.IsNullOrWhiteSpace(mapping.XPath) && !string.IsNullOrEmpty(mapping.DefaultValue)))
                {
                    value = mapping.DefaultValue;
                }
                else
                {
                    var node = contextNode.SelectSingleNode(mapping.XPath, nsManager);
                    value = GetSafeTextValue(node);
                    if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(mapping.DefaultValue))
                        value = mapping.DefaultValue;
                }

                if (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(textToRemove))
                    value = value.Replace(textToRemove, "").Trim();

                if (!string.IsNullOrEmpty(mapping.FindValue) && mapping.ReplaceValue != null)
                    value = value?.Replace(mapping.FindValue, mapping.ReplaceValue);

                var typedValue = ConvertToTypedValue(value, mapping.SqlDataType);

                if (mapping.IsRequired && (typedValue == null || typedValue is DBNull))
                    throw new InvalidOperationException(
                        $"Required field '{mapping.ColumnName}' has no value in the XML.");

                result[mapping.ColumnName] = typedValue;
            }
            catch (InvalidOperationException)
            {
                throw; // propagate required-field violations as-is
            }
            catch
            {
                if (mapping.IsRequired)
                    throw new InvalidOperationException(
                        $"Required field '{mapping.ColumnName}' could not be extracted from the XML.");

                result[mapping.ColumnName] = mapping.DefaultValue != null
                    ? ConvertToTypedValue(mapping.DefaultValue, mapping.SqlDataType)
                    : DBNull.Value;
            }
        }

        return result;
    }

    private static object? ConvertToTypedValue(string? value, SqlDataType dataType)
    {
        if (string.IsNullOrEmpty(value))
            return DBNull.Value;

        return dataType switch
        {
            SqlDataType.INT => int.TryParse(value, out var i) ? i : DBNull.Value,
            SqlDataType.BIGINT => long.TryParse(value, out var l) ? l : DBNull.Value,
            SqlDataType.VARCHAR => value,
            SqlDataType.NVARCHAR => value,
            SqlDataType.DATETIME => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : DBNull.Value,
            SqlDataType.BIT => value switch
            {
                _ when value.Equals("1", StringComparison.OrdinalIgnoreCase) => true,
                _ when value.Equals("true", StringComparison.OrdinalIgnoreCase) => true,
                _ when value.Equals("yes", StringComparison.OrdinalIgnoreCase) => true,
                _ when value.Equals("0", StringComparison.OrdinalIgnoreCase) => false,
                _ when value.Equals("false", StringComparison.OrdinalIgnoreCase) => false,
                _ when value.Equals("no", StringComparison.OrdinalIgnoreCase) => false,
                _ => DBNull.Value
            },
            _ => value
        };
    }
}
