using System.ComponentModel.DataAnnotations;

namespace AdmXmlDb.Core.Entities;

public enum TargetDirectoryComponentType
{
    Static,
    XPath
}

public class TargetDirectoryComponent
{
    [Key]
    public int Id { get; set; }

    public int TaskId { get; set; }
    public IntegrationTask Task { get; set; } = null!;

    public TargetDirectoryComponentType ComponentType { get; set; }
    public int SortOrder { get; set; }

    /// <summary>
    /// Static text or XPath expression depending on ComponentType.
    /// </summary>
    [MaxLength(500)]
    public string Value { get; set; } = string.Empty;
}
