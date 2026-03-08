using System.ComponentModel.DataAnnotations;

namespace AdmXmlDb.Core.Entities;

public class SmtpSettings
{
    [Key]
    public int Id { get; set; }

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public bool UseSsl { get; set; }
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// DPAPI-encrypted password.
    /// </summary>
    public byte[]? EncryptedPassword { get; set; }

    public string SenderEmail { get; set; } = string.Empty;
}
