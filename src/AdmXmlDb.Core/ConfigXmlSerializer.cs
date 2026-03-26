using System.Runtime.Versioning;
using System.Xml;
using AdmXmlDb.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace AdmXmlDb.Core;

/// <summary>
/// Exports and imports task configuration to/from XML files (compatible with legacy ADMXmlToDb format).
/// </summary>
[SupportedOSPlatform("windows")]
public static class ConfigXmlSerializer
{
    public static void Export(string dbPath, int taskId, string outputPath)
    {
        using var db = new AdmXmlDbContext(dbPath);
        var task = db.Tasks
            .Include(t => t.Mappings)
            .Include(t => t.TargetDirectoryComponents)
            .FirstOrDefault(t => t.Id == taskId)
            ?? throw new InvalidOperationException("Task not found");

        var connStr = DataProtectionHelper.Unprotect(task.EncryptedConnectionString);
        var (server, database, username) = DbConnectionHelper.ParseConnectionString(connStr ?? "");

        var doc = new XmlDocument();
        var root = doc.CreateElement("ROOT");
        doc.AppendChild(root);

        AppendElement(doc, root, "TaskName", task.Name);
        AppendElement(doc, root, "Server", server ?? "");
        AppendElement(doc, root, "Database", database ?? "");
        AppendElement(doc, root, "UserName", username ?? "");
        try {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connStr ?? "");
            AppendElement(doc, root, "Password", builder.Password ?? "");
        } catch {
            AppendElement(doc, root, "Password", "");
        }
        AppendElement(doc, root, "TableName", task.TableName);
        AppendElement(doc, root, "MultiRecordRootXPath", task.MultiRecordRootXPath ?? "");
        AppendElement(doc, root, "TextToRemoveInXPath", task.TextToRemoveInXPath ?? "");
        AppendElement(doc, root, "SourcePath", task.InputPath);
        AppendElement(doc, root, "TargetPath", task.OutputPath);
        AppendElement(doc, root, "ErrorPath", task.ErrorPath ?? "");
        AppendElement(doc, root, "PrependToFileName", task.PrependToFileName ?? "");
        AppendElement(doc, root, "StaticTargetDirectory", task.StaticTargetDirectory ?? "");

        var mappingsEl = doc.CreateElement("Mappings");
        foreach (var m in task.Mappings.OrderBy(x => x.SortOrder))
        {
            var mappingEl = doc.CreateElement("mapping");
            mappingEl.SetAttribute("ColumnName", m.ColumnName);
            mappingEl.SetAttribute("DataType", m.SqlDataType.ToString());
            mappingEl.SetAttribute("XPath", m.XPath);
            mappingEl.SetAttribute("DefaultValue", m.DefaultValue ?? "");
            mappingEl.SetAttribute("FindValue", m.FindValue ?? "");
            mappingEl.SetAttribute("ReplaceValue", m.ReplaceValue ?? "");
            mappingEl.SetAttribute("IsLiteral", m.IsLiteral.ToString());
            mappingEl.SetAttribute("ValueTemplate", m.ValueTemplate ?? "");
            mappingEl.SetAttribute("TargetTableName", m.TargetTableName ?? "");
            mappingEl.SetAttribute("IsRequired", m.IsRequired.ToString());
            mappingsEl.AppendChild(mappingEl);
        }
        root.AppendChild(mappingsEl);

        var dirsEl = doc.CreateElement("Directories");
        foreach (var c in task.TargetDirectoryComponents.OrderBy(x => x.SortOrder))
        {
            var dirEl = doc.CreateElement("directory");
            dirEl.SetAttribute("Type", c.ComponentType.ToString());
            dirEl.SetAttribute("Value", c.Value);
            dirsEl.AppendChild(dirEl);
        }
        root.AppendChild(dirsEl);

        using var writer = XmlWriter.Create(outputPath, new XmlWriterSettings { Indent = true });
        doc.Save(writer);
    }

    public static int Import(string dbPath, string inputPath)
    {
        var doc = new XmlDocument();
        doc.Load(inputPath);

        var root = doc.DocumentElement ?? throw new InvalidOperationException("Invalid XML");
        var taskName = GetElement(root, "TaskName");
        var server = GetElement(root, "Server");
        var database = GetElement(root, "Database");
        var username = GetElement(root, "UserName");
        var password = GetElement(root, "Password");
        var connStr = DbConnectionHelper.BuildConnectionString(server, database, username, password);

        using var db = new AdmXmlDbContext(dbPath);
        var task = new IntegrationTask
        {
            Name = !string.IsNullOrWhiteSpace(taskName) ? taskName : System.IO.Path.GetFileNameWithoutExtension(inputPath) + "_imported",
            InputPath = GetElement(root, "SourcePath"),
            OutputPath = GetElement(root, "TargetPath"),
            ErrorPath = NullIfEmpty(GetElement(root, "ErrorPath")) ?? GetElement(root, "TargetPath") + "_error",
            EncryptedConnectionString = DataProtectionHelper.Protect(connStr),
            TableName = GetElement(root, "TableName"),
            MultiRecordRootXPath = NullIfEmpty(GetElement(root, "MultiRecordRootXPath")),
            TextToRemoveInXPath = NullIfEmpty(GetElement(root, "TextToRemoveInXPath")),
            PrependToFileName = NullIfEmpty(GetElement(root, "PrependToFileName")),
            StaticTargetDirectory = NullIfEmpty(GetElement(root, "StaticTargetDirectory")),
            CronExpression = "0 0 * * * ?",
            IsEnabled = true
        };
        db.Tasks.Add(task);
        db.SaveChanges();

        var mappingsNode = root.SelectSingleNode("Mappings");
        if (mappingsNode != null)
        {
            var sortOrder = 0;
            var mappingNodes = mappingsNode.SelectNodes("mapping");
            if (mappingNodes != null)
            foreach (XmlNode m in mappingNodes)
            {
                var col = m.Attributes?["ColumnName"]?.Value ?? "";
                var xpath = m.Attributes?["XPath"]?.Value ?? "";
                if (string.IsNullOrWhiteSpace(col) && string.IsNullOrWhiteSpace(xpath)) continue;
                var dataTypeStr = m.Attributes?["DataType"]?.Value ?? "VARCHAR";
                Enum.TryParse<SqlDataType>(dataTypeStr, true, out var dataType);
                db.TaskMappings.Add(new TaskMapping
                {
                    TaskId = task.Id,
                    ColumnName = col,
                    XPath = xpath,
                    SqlDataType = dataType,
                    DefaultValue = NullIfEmpty(m.Attributes?["DefaultValue"]?.Value ?? ""),
                    FindValue = NullIfEmpty(m.Attributes?["FindValue"]?.Value ?? ""),
                    ReplaceValue = NullIfEmpty(m.Attributes?["ReplaceValue"]?.Value ?? ""),
                    IsLiteral = string.Equals(m.Attributes?["IsLiteral"]?.Value, "true", StringComparison.OrdinalIgnoreCase),
                    ValueTemplate = NullIfEmpty(m.Attributes?["ValueTemplate"]?.Value ?? ""),
                    TargetTableName = NullIfEmpty(m.Attributes?["TargetTableName"]?.Value ?? ""),
                    IsRequired = string.Equals(m.Attributes?["IsRequired"]?.Value, "true", StringComparison.OrdinalIgnoreCase),
                    SortOrder = sortOrder++
                });
            }
        }

        var dirsNode = root.SelectSingleNode("Directories");
        if (dirsNode != null)
        {
            var sortOrder = 0;
            var dirNodes = dirsNode.SelectNodes("directory");
            if (dirNodes != null)
            foreach (XmlNode d in dirNodes)
            {
                var typeStr = d.Attributes?["Type"]?.Value ?? "Static";
                Enum.TryParse<TargetDirectoryComponentType>(typeStr, true, out var compType);
                var val = d.Attributes?["Value"]?.Value ?? "";
                if (string.IsNullOrWhiteSpace(val)) continue;
                db.TargetDirectoryComponents.Add(new TargetDirectoryComponent
                {
                    TaskId = task.Id,
                    ComponentType = compType,
                    Value = val,
                    SortOrder = sortOrder++
                });
            }
        }

        db.SaveChanges();
        return task.Id;
    }

    private static void AppendElement(XmlDocument doc, XmlElement parent, string name, string value)
    {
        var el = doc.CreateElement(name);
        el.InnerText = value;
        parent.AppendChild(el);
    }

    private static string GetElement(XmlElement root, string name)
    {
        return root.SelectSingleNode(name)?.InnerText?.Trim() ?? "";
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s!.Trim();
}
