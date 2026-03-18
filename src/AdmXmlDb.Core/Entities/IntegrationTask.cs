using System.ComponentModel.DataAnnotations;

namespace AdmXmlDb.Core.Entities;

public class IntegrationTask
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public string InputPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string ErrorPath { get; set; } = string.Empty;

    /// <summary>
    /// DPAPI-encrypted connection string for target SQL Server.
    /// </summary>
    public byte[]? EncryptedConnectionString { get; set; }

    [MaxLength(200)]
    public string TableName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string CronExpression { get; set; } = "0 0 * * * ?"; // Every hour by default

    /// <summary>
    /// Semicolon-separated email addresses for error notifications.
    /// </summary>
    public string ErrorEmails { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// XPath for repeating records when document has multiple records (e.g. //item).
    /// </summary>
    [MaxLength(500)]
    public string? MultiRecordRootXPath { get; set; }

    /// <summary>
    /// Expression to remove from XPath result after each repeated record.
    /// </summary>
    [MaxLength(500)]
    public string? TextToRemoveInXPath { get; set; }

    /// <summary>
    /// Prefix to prepend to output filenames.
    /// </summary>
    [MaxLength(100)]
    public string? PrependToFileName { get; set; }

    /// <summary>
    /// Static base directory for dynamic target path construction.
    /// </summary>
    [MaxLength(1000)]
    public string? StaticTargetDirectory { get; set; }

    /// <summary>
    /// Suffix appended to output file name.
    /// </summary>
    [MaxLength(200)]
    public string? FileNameSuffix { get; set; }

    /// <summary>
    /// When true, current date-time is appended to the output file name.
    /// </summary>
    public bool AddDateTimeToFileName { get; set; }

    /// <summary>
    /// Seconds to wait after a file appears in the input folder before processing it.
    /// Gives slow/large file copies time to finish writing.
    /// </summary>
    public int ProcessingDelaySeconds { get; set; } = 5;

    /// <summary>
    /// Optional Windows username for accessing UNC network shares (e.g. DOMAIN\user).
    /// </summary>
    [MaxLength(200)]
    public string? NetworkUsername { get; set; }

    /// <summary>
    /// DPAPI-encrypted password for UNC network share access.
    /// </summary>
    public byte[]? EncryptedNetworkPassword { get; set; }

    public ICollection<TaskMapping> Mappings { get; set; } = new List<TaskMapping>();
    public ICollection<TargetDirectoryComponent> TargetDirectoryComponents { get; set; } = new List<TargetDirectoryComponent>();
}
