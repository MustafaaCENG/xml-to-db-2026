using AdmXmlDb.Core.Entities;

namespace AdmXmlDb.Management.Models;

public class TargetDirComponentItem
{
    public int Id { get; set; }
    public TargetDirectoryComponentType ComponentType { get; set; }
    public int SortOrder { get; set; }
    public string Value { get; set; } = string.Empty;

    public TargetDirComponentItem() { }

    public TargetDirComponentItem(int id, TargetDirectoryComponentType componentType, string value, int sortOrder)
    {
        Id = id;
        ComponentType = componentType;
        Value = value;
        SortOrder = sortOrder;
    }

    public string DisplayValue => ComponentType == TargetDirectoryComponentType.XPath
        ? $"XPATH: {Value}"
        : $"STATIK: {Value}";
}
