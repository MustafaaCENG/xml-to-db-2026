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

        // Establish UNC network share connection if credentials are configured
        NetworkShareConnector? inputShare = null, outputShare = null, errorShare = null;
        try
        {
            var netUser = task.NetworkUsername;
            var netPass = DataProtectionHelper.Unprotect(task.EncryptedNetworkPassword);
            if (!string.IsNullOrWhiteSpace(netUser))
            {
                inputShare = TryConnectShare(task.InputPath, netUser, netPass, task.Name);
                outputShare = TryConnectShare(task.OutputPath, netUser, netPass, task.Name);
                errorShare = TryConnectShare(task.ErrorPath, netUser, netPass, task.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Task {TaskName}: Could not connect to network shares. Will try anyway.", task.Name);
        }

        try
        {

        if (!Directory.Exists(task.InputPath))
        {
            _logger.LogWarning("Input path does not exist or is not accessible: {Path}. " +
                "If this is a UNC path, configure Network Credentials in Task Settings.", task.InputPath);
            return;
        }

        if (string.IsNullOrWhiteSpace(task.OutputPath) || string.IsNullOrWhiteSpace(task.ErrorPath))
        {
            _logger.LogError("Task {TaskName}: OutputPath or ErrorPath is not configured.", task.Name);
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
            }
        }

        } // end try for UNC connections
        finally
        {
            inputShare?.Dispose();
            outputShare?.Dispose();
            errorShare?.Dispose();
        }
    }

    private NetworkShareConnector? TryConnectShare(string? path, string username, string password, string taskName)
    {
        if (!NetworkShareConnector.IsUncPath(path)) return null;
        try
        {
            return NetworkShareConnector.Connect(path!, username, password);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Task {TaskName}: Could not connect to share {Share}", taskName, NetworkShareConnector.GetShareRoot(path!));
            return null;
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

        bool copySuccess = false;
        Exception? lastCopyEx = null;
        for (int i = 0; i < 3; i++)
        {
            try
            {
                File.Copy(xmlPath, tempPath, overwrite: true);
                copySuccess = true;
                break;
            }
            catch (Exception ex)
            {
                lastCopyEx = ex;
                await Task.Delay(1000, ct); // File might be locked by an upstream transfer tool
            }
        }

        if (!copySuccess)
        {
            _logger.LogError(lastCopyEx, "Task {TaskName}: Could not copy file {File} to temp after 3 attempts. It may still be locked.", task.Name, xmlPath);
            return;
        }

        try
        {
            var xmlContent = await File.ReadAllTextAsync(tempPath, ct);
            var allRows = XmlParser.ExtractAllRows(xmlContent, task);

            // Group mappings by target table so we can split each row
            var mappingsByTable = task.Mappings
                .GroupBy(m => string.IsNullOrWhiteSpace(m.TargetTableName) ? task.TableName : m.TargetTableName.Trim())
                .ToDictionary(g => g.Key, g => g.Select(m => m.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase));

            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            await using var tran = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            try
            {
                var totalRows = 0;
                foreach (var values in allRows)
                {
                    foreach (var (tableName, columns) in mappingsByTable)
                    {
                        var tableValues = values
                            .Where(kvp => columns.Contains(kvp.Key))
                            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                        if (tableValues.Count > 0)
                            totalRows += await SqlInsertBuilder.ExecuteInsertAsync(conn, tran, tableName, tableValues, ct);
                    }
                }
                await tran.CommitAsync(ct);

                await LogExecutionAsync(db, task.Id, task.Name, fileName, "Success", $"Rows affected: {totalRows}", ct);
                _logger.LogInformation("Task {TaskName}: {File} -> {Rows} row(s), moved to output", task.Name, fileName, totalRows);

                try
                {
                    var destPath = TargetPathBuilder.GetOutputFilePath(xmlContent, task, fileName);
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    SafeMoveFile(xmlPath, destPath, _logger);
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
                    catch (Exception procEx) { _logger.LogError(procEx, "Failed to append .processed suffix to {File}", xmlPath); }
                }
            }
            catch (Exception ex)
            {
                await tran.RollbackAsync(ct);
                await LogExecutionAsync(db, task.Id, task.Name, fileName, "Failed", ex.ToString(), ct);

                var errorPath = Path.Combine(task.ErrorPath, fileName);
                SafeMoveFile(xmlPath, errorPath, _logger);

                await SendErrorNotificationAsync(db, task, fileName, ex, _logger, ct);
            }
        }
        catch (XmlException ex)
        {
            await LogExecutionAsync(db, task.Id, task.Name, fileName, "Failed", ex.ToString(), ct);
            var errorPath = Path.Combine(task.ErrorPath, fileName);
            SafeMoveFile(xmlPath, errorPath, _logger);
            await SendErrorNotificationAsync(db, task, fileName, ex, _logger, ct);
        }
        catch (Exception ex)
        {
            await LogExecutionAsync(db, task.Id, task.Name, fileName, "Failed", ex.ToString(), ct);
            var errorPath = Path.Combine(task.ErrorPath, fileName);
            SafeMoveFile(xmlPath, errorPath, _logger);
            await SendErrorNotificationAsync(db, task, fileName, ex, _logger, ct);
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

    private static void SafeMoveFile(string source, string dest, ILogger logger)
    {
        var finalDest = GetUniqueFilePath(dest);
        try
        {
            File.Move(source, finalDest);
            logger.LogInformation("File successfully moved to {Dest}", finalDest);
        }
        catch (Exception moveEx)
        {
            logger.LogWarning(moveEx, "File.Move failed from {Source} to {Dest}. Attempting Copy/Delete fallback.", source, finalDest);
            try
            {
                File.Copy(source, finalDest, overwrite: true);
                File.Delete(source);
                logger.LogInformation("File successfully copy-deleted to {Dest}", finalDest);
            }
            catch (Exception fallbackEx)
            {
                logger.LogError(fallbackEx, "Fallback Copy/Delete also failed for {Source}.", source);
            }
        }
    }

    private static async Task SendErrorNotificationAsync(
        AdmXmlDbContext db,
        IntegrationTask task,
        string filename,
        Exception ex,
        ILogger logger,
        CancellationToken ct)
    {
        var emails = task.ErrorEmails?
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(e => !string.IsNullOrEmpty(e))
            .ToArray() ?? [];

        if (emails.Length == 0)
        {
            logger.LogInformation("No error emails configured for task {TaskName}. Skipping email notification.", task.Name);
            return;
        }

        var smtp = await db.SmtpSettings.FirstOrDefaultAsync(ct);
        if (smtp == null || string.IsNullOrEmpty(smtp.Host))
        {
            logger.LogWarning("SMTP settings are empty or missing. Cannot send error email.");
            return;
        }

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

            logger.LogInformation("Attempting to send error email to {Count} recipients via {Host}:{Port}", emails.Length, smtp.Host, smtp.Port);
            await client.SendMailAsync(mail);
            logger.LogInformation("Error email sent successfully.");
        }
        catch (Exception smtpEx)
        {
            logger.LogError(smtpEx, "SMTP email sending failed. Check SMTP configuration.");
        }
    }
}
