using System.Net;
using System.Net.Mail;
using System.Xml;
using AdmXmlDb.Core;
using AdmXmlDb.Core.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace AdmXmlDb.Worker.Jobs;

[DisallowConcurrentExecution]
public class XmlIntegrationJob : IJob
{
    public const string TaskIdKey = "TaskId";

    private readonly ILogger<XmlIntegrationJob> _logger;

    public XmlIntegrationJob(ILogger<XmlIntegrationJob> logger)
    {
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var taskId = context.MergedJobDataMap.GetInt(TaskIdKey);
        if (taskId == 0)
        {
            _logger.LogError("XmlIntegrationJob: TaskId not found in job data");
            return;
        }

        var dbPath = Constants.GetDefaultDbPath();
        await using var db = new AdmXmlDbContext(dbPath);
        await db.Database.EnsureCreatedAsync();

        var task = await db.Tasks
            .Include(t => t.Mappings)
            .Include(t => t.TargetDirectoryComponents)
            .FirstOrDefaultAsync(t => t.Id == taskId, context.CancellationToken);

        if (task == null || !task.IsEnabled)
            return;

        if (task.Mappings.Count == 0)
        {
            _logger.LogWarning("Task {TaskName} has no mappings", task.Name);
            return;
        }

        if (!Directory.Exists(task.InputPath))
        {
            _logger.LogWarning("Input path does not exist: {Path}", task.InputPath);
            return;
        }

        Directory.CreateDirectory(task.OutputPath);
        Directory.CreateDirectory(task.ErrorPath);

        var connectionString = DataProtectionHelper.Unprotect(task.EncryptedConnectionString);
        if (string.IsNullOrEmpty(connectionString))
        {
            _logger.LogError("Task {TaskName}: Could not decrypt connection string", task.Name);
            return;
        }

        var xmlFiles = Directory.GetFiles(task.InputPath, "*.xml");
        _logger.LogInformation("Task {TaskName}: Processing {Count} XML file(s) from {Path}", task.Name, xmlFiles.Length, task.InputPath);
        foreach (var xmlPath in xmlFiles)
        {
            try
            {
                await ProcessFileAsync(db, task, xmlPath, connectionString, context.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing {File}", xmlPath);
                // Do not rethrow - continue with next file
            }
        }
    }

    private async Task ProcessFileAsync(
        AdmXmlDbContext db,
        IntegrationTask task,
        string xmlPath,
        string connectionString,
        CancellationToken ct)
    {
        var fileName = Path.GetFileName(xmlPath);
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".xml");

        try
        {
            File.Copy(xmlPath, tempPath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not copy file {File} to temp", xmlPath);
            return;
        }

        try
        {
            var xmlContent = await File.ReadAllTextAsync(tempPath, ct);
            var allRows = XmlParser.ExtractAllRows(xmlContent, task);

            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            await using var tran = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            try
            {
                var totalRows = 0;
                foreach (var values in allRows)
                {
                    totalRows += await SqlInsertBuilder.ExecuteInsertAsync(conn, tran, task.TableName, values, ct);
                }
                await tran.CommitAsync(ct);

                await LogExecutionAsync(db, task.Id, task.Name, fileName, "Success", $"Rows affected: {totalRows}", ct);
                _logger.LogInformation("Task {TaskName}: {File} -> {Rows} row(s), moved to output", task.Name, fileName, totalRows);

                try
                {
                    var destPath = TargetPathBuilder.GetOutputFilePath(xmlContent, task, fileName);
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    SafeMoveFile(xmlPath, destPath);
                }
                catch (Exception moveEx)
                {
                    _logger.LogError(moveEx, "Task {TaskName}: Could not move {File} to output after successful commit. Renaming to .processed to prevent duplicate insert", task.Name, fileName);
                    try
                    {
                        var processedPath = xmlPath + ".processed";
                        processedPath = GetUniqueFilePath(processedPath);
                        File.Move(xmlPath, processedPath);
                    }
                    catch { /* best effort */ }
                }
            }
            catch (Exception ex)
            {
                await tran.RollbackAsync(ct);
                await LogExecutionAsync(db, task.Id, task.Name, fileName, "Failed", ex.ToString(), ct);

                var errorPath = Path.Combine(task.ErrorPath, fileName);
                SafeMoveFile(xmlPath, errorPath);

                await SendErrorNotificationAsync(db, task, fileName, ex, ct);
            }
        }
        catch (XmlException ex)
        {
            await LogExecutionAsync(db, task.Id, task.Name, fileName, "Failed", ex.ToString(), ct);
            var errorPath = Path.Combine(task.ErrorPath, fileName);
            SafeMoveFile(xmlPath, errorPath);
            await SendErrorNotificationAsync(db, task, fileName, ex, ct);
        }
        catch (Exception ex)
        {
            await LogExecutionAsync(db, task.Id, task.Name, fileName, "Failed", ex.ToString(), ct);
            var errorPath = Path.Combine(task.ErrorPath, fileName);
            SafeMoveFile(xmlPath, errorPath);
            await SendErrorNotificationAsync(db, task, fileName, ex, ct);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* ignore */ }
        }
    }

    private static async Task LogExecutionAsync(
        AdmXmlDbContext db,
        int taskId,
        string taskName,
        string filename,
        string status,
        string? message,
        CancellationToken ct)
    {
        db.ExecutionLogs.Add(new ExecutionLog
        {
            TaskId = taskId,
            TaskName = taskName,
            Filename = filename,
            Status = status,
            Message = message,
            Timestamp = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    private static string GetUniqueFilePath(string destPath)
    {
        if (!File.Exists(destPath))
            return destPath;

        var dir = Path.GetDirectoryName(destPath)!;
        var name = Path.GetFileNameWithoutExtension(destPath);
        var ext = Path.GetExtension(destPath);
        var counter = 1;
        string newPath;
        do
        {
            newPath = Path.Combine(dir, $"{name}_{counter}{ext}");
            counter++;
        } while (File.Exists(newPath));

        return newPath;
    }

    private static void SafeMoveFile(string source, string dest)
    {
        dest = GetUniqueFilePath(dest);
        try
        {
            File.Move(source, dest);
        }
        catch
        {
            try
            {
                File.Copy(source, dest, overwrite: true);
                File.Delete(source);
            }
            catch { /* best effort */ }
        }
    }

    private static async Task SendErrorNotificationAsync(
        AdmXmlDbContext db,
        IntegrationTask task,
        string filename,
        Exception ex,
        CancellationToken ct)
    {
        var emails = task.ErrorEmails?
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(e => !string.IsNullOrEmpty(e))
            .ToArray() ?? [];

        if (emails.Length == 0)
            return;

        var smtp = await db.SmtpSettings.FirstOrDefaultAsync(ct);
        if (smtp == null || string.IsNullOrEmpty(smtp.Host))
            return;

        var password = DataProtectionHelper.Unprotect(smtp.EncryptedPassword);
        try
        {
            using var client = new SmtpClient(smtp.Host, smtp.Port)
            {
                EnableSsl = smtp.UseSsl,
                Credentials = string.IsNullOrEmpty(smtp.Username) ? null : new NetworkCredential(smtp.Username, password)
            };

            using var mail = new MailMessage
            {
                From = new MailAddress(smtp.SenderEmail, "AdmXmlDb Worker"),
                Subject = $"AdmXmlDb Error: {task.Name} - {filename}",
                Body = $"Task: {task.Name}\r\nFile: {filename}\r\nError:\r\n{ex}",
                IsBodyHtml = false
            };

            foreach (var to in emails)
                mail.To.Add(to);

            await client.SendMailAsync(mail);
        }
        catch
        {
            // Log but do not rethrow - SMTP failure should not crash the worker
        }
    }
}
