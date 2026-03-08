using System.ComponentModel.DataAnnotations;
using AdmXmlDb.Core;

namespace AdmXmlDb.Core.Entities;

public class TaskMapping
{
    [Key]
    public int Id { get; set; }

    public int TaskId { get; set; }
    public IntegrationTask Task { get; set; } = null!;

    public string XPath { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public SqlDataType SqlDataType { get; set; }

    /// <summary>
    /// Default/literal value when XPath is empty or for constant mapping.
    /// </summary>
    [MaxLength(500)]
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Find string for value replacement (Değer değiştir - Bulunacak).
    /// </summary>
    [MaxLength(500)]
    public string? FindValue { get; set; }

    /// <summary>
    /// Replace string for value replacement (Değer değiştir - Yerine konacak).
    /// </summary>
    [MaxLength(500)]
    public string? ReplaceValue { get; set; }

    /// <summary>
    /// Sort order for mapping sequence (Yukarı/Aşağı).
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// When true, use DefaultValue instead of XPath extraction.
    /// </summary>
    public bool IsLiteral { get; set; }

    /// <summary>
    /// Template for combining multiple XPath values and static text into a single column value.
    /// Use {//XPath} placeholders for XML values and plain text for literals.
    /// Example: "{//DepartmentCode}-{//CompanyCode}" or "{//Department} FIXED_TEXT"
    /// When set, this takes priority over XPath for value extraction.
    /// </summary>
    [MaxLength(1000)]
    public string? ValueTemplate { get; set; }
}
