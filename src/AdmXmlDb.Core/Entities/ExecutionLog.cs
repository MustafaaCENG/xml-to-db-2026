using System.ComponentModel.DataAnnotations;

namespace AdmXmlDb.Core.Entities;

public class ExecutionLog
{
    [Key]
    public int Id { get; set; }

    public int TaskId { get; set; }
    public string TaskName { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // "Success" or "Failed"
    public string? Message { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
