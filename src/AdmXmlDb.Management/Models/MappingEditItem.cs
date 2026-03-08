using System.ComponentModel;
using AdmXmlDb.Core;

namespace AdmXmlDb.Management.Models;

public class MappingEditItem : INotifyPropertyChanged
{
    public int Id { get; set; }

    private string _xPath = string.Empty;
    public string XPath
    {
        get => _xPath;
        set { _xPath = value; OnPropertyChanged(nameof(XPath)); }
    }

    private string _columnName = string.Empty;
    public string ColumnName
    {
        get => _columnName;
        set { _columnName = value; OnPropertyChanged(nameof(ColumnName)); }
    }

    private SqlDataType _sqlDataType;
    public SqlDataType SqlDataType
    {
        get => _sqlDataType;
        set { _sqlDataType = value; OnPropertyChanged(nameof(SqlDataType)); }
    }

    private string? _defaultValue;
    public string? DefaultValue
    {
        get => _defaultValue;
        set { _defaultValue = value; OnPropertyChanged(nameof(DefaultValue)); }
    }

    private string? _findValue;
    public string? FindValue
    {
        get => _findValue;
        set { _findValue = value; OnPropertyChanged(nameof(FindValue)); }
    }

    private string? _replaceValue;
    public string? ReplaceValue
    {
        get => _replaceValue;
        set { _replaceValue = value; OnPropertyChanged(nameof(ReplaceValue)); }
    }

    private int _sortOrder;
    public int SortOrder
    {
        get => _sortOrder;
        set { _sortOrder = value; OnPropertyChanged(nameof(SortOrder)); }
    }

    private bool _isLiteral;
    public bool IsLiteral
    {
        get => _isLiteral;
        set { _isLiteral = value; OnPropertyChanged(nameof(IsLiteral)); }
    }

    private string? _valueTemplate;
    public string? ValueTemplate
    {
        get => _valueTemplate;
        set { _valueTemplate = value; OnPropertyChanged(nameof(ValueTemplate)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
