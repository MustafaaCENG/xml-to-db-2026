using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.ServiceProcess;
using System.Windows;
using System.Windows.Forms;
using AdmXmlDb.Core;
using AdmXmlDb.Core.Entities;
using AdmXmlDb.Management.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AdmXmlDb.Management;

public partial class MainWindow : Window
{
    private readonly string _dbPath;
    private int _currentLogPage = 0;
    private const int LogPageSize = 50;
    private IntegrationTask? _selectedTask;
    private System.Windows.Threading.DispatcherTimer? _statusTimer;

    public MainWindow()
    {
        InitializeComponent();
        _dbPath = Constants.GetDefaultDbPath();
        EnsureDatabase();
        LoadDashboard();
        LoadSmtpSettings();
        LoadTasks();
        LoadSimulationTasks();
        StartStatusTimer();
    }

    private void EnsureDatabase()
    {
        try
        {
            using var db = new AdmXmlDbContext(_dbPath);
            db.Database.EnsureCreated();
            SchemaMigrator.Migrate(db);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Could not initialize database: {ex.Message}", "Error");
        }
    }

    private void StartStatusTimer()
    {
        _statusTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _statusTimer.Tick += (_, _) => RefreshServiceStatus();
        _statusTimer.Start();
    }

    private void RefreshServiceStatus()
    {
        try
        {
            using var sc = new ServiceController(Constants.ServiceName);
            var status = sc.Status;
            ServiceStatusText.Text = status == ServiceControllerStatus.Running ? "Running" :
                status == ServiceControllerStatus.Stopped ? "Stopped" : status.ToString();
            UpdateStatusIndicator(status == ServiceControllerStatus.Running);
        }
        catch
        {
            ServiceStatusText.Text = "Not installed";
            UpdateStatusIndicator(false);
        }
        LoadLogs();
    }

    private void UpdateStatusIndicator(bool isRunning)
    {
        if (StatusIndicator != null)
        {
            StatusIndicator.Fill = isRunning
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LimeGreen)
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.OrangeRed);
        }
    }

    private void RefreshStatusBtn_Click(object sender, RoutedEventArgs e) => RefreshServiceStatus();

    private void LoadDashboard()
    {
        RefreshServiceStatus();
        LoadLogs();
    }

    private void LoadLogs()
    {
        try
        {
            using var db = new AdmXmlDbContext(_dbPath);
            var logs = db.ExecutionLogs
                .OrderByDescending(x => x.Timestamp)
                .Skip(_currentLogPage * LogPageSize)
                .Take(LogPageSize)
                .ToList();

            LogsGrid.ItemsSource = logs;
            LogPageInfo.Text = $"Page {_currentLogPage + 1}";
            LogPrevBtn.IsEnabled = _currentLogPage > 0;
            var total = db.ExecutionLogs.Count();
            LogNextBtn.IsEnabled = (_currentLogPage + 1) * LogPageSize < total;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Error loading logs: {ex.Message}", "Error");
        }
    }

    private void RefreshLogsBtn_Click(object sender, RoutedEventArgs e) => LoadLogs();
    private void LogPrevBtn_Click(object sender, RoutedEventArgs e) { _currentLogPage--; LoadLogs(); }
    private void LogNextBtn_Click(object sender, RoutedEventArgs e) { _currentLogPage++; LoadLogs(); }

    private void CleanLogsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show("90 günden eski tüm loglar silinecek. Devam edilsin mi?", "Onay", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;
        try
        {
            using var db = new AdmXmlDbContext(_dbPath);
            var cutoff = DateTime.UtcNow.AddDays(-90);
            var oldLogs = db.ExecutionLogs.Where(l => l.Timestamp < cutoff).ToList();
            db.ExecutionLogs.RemoveRange(oldLogs);
            var count = db.SaveChanges();
            System.Windows.MessageBox.Show($"{count} eski log kaydı silindi.", "Temizlendi");
            _currentLogPage = 0;
            LoadLogs();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Hata: {ex.Message}", "Hata");
        }
    }

    private void LoadSmtpSettings()
    {
        try
        {
            using var db = new AdmXmlDbContext(_dbPath);
            var smtp = db.SmtpSettings.FirstOrDefault();
            if (smtp != null)
            {
                SmtpHost.Text = smtp.Host;
                SmtpPort.Text = smtp.Port.ToString();
                SmtpUseSsl.IsChecked = smtp.UseSsl;
                SmtpUsername.Text = smtp.Username;
                SmtpSender.Text = smtp.SenderEmail;
                SmtpPassword.Password = ""; // Don't load decrypted password
            }
        }
        catch { /* ignore */ }
    }

    private void SaveSmtpBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var db = new AdmXmlDbContext(_dbPath);
            var smtp = db.SmtpSettings.FirstOrDefault();
            if (smtp == null)
            {
                smtp = new SmtpSettings();
                db.SmtpSettings.Add(smtp);
            }
            smtp.Host = SmtpHost.Text ?? "";
            smtp.Port = int.TryParse(SmtpPort.Text, out var p) ? p : 587;
            smtp.UseSsl = SmtpUseSsl.IsChecked == true;
            smtp.Username = SmtpUsername.Text ?? "";
            smtp.SenderEmail = SmtpSender.Text ?? "";
            if (!string.IsNullOrEmpty(SmtpPassword.Password))
                smtp.EncryptedPassword = DataProtectionHelper.Protect(SmtpPassword.Password);
            db.SaveChanges();
            System.Windows.MessageBox.Show("SMTP settings saved.", "Saved");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Error saving: {ex.Message}", "Error");
        }
    }

    private async void TestSmtpBtn_Click(object sender, RoutedEventArgs e)
    {
        TestSmtpBtn.IsEnabled = false;
        try
        {
            var result = await Task.Run(() =>
            {
                try
                {
                    using var db = new AdmXmlDbContext(_dbPath);
                    var smtp = db.SmtpSettings.FirstOrDefault();
                    if (smtp == null || string.IsNullOrEmpty(smtp.Host))
                        return "SMTP not configured.";
                    var password = DataProtectionHelper.Unprotect(smtp.EncryptedPassword);
                    using var client = new SmtpClient(smtp.Host, smtp.Port)
                    {
                        EnableSsl = smtp.UseSsl,
                        Credentials = string.IsNullOrEmpty(smtp.Username) ? null : new NetworkCredential(smtp.Username, password)
                    };
                    client.Send(smtp.SenderEmail, smtp.SenderEmail, "AdmXmlDb Test", "Test email from AdmXmlDb.");
                    return "Connection successful!";
                }
                catch (Exception ex)
                {
                    return $"Failed: {ex.Message}";
                }
            });
            System.Windows.MessageBox.Show(result, "Test Connection");
        }
        finally
        {
            TestSmtpBtn.IsEnabled = true;
        }
    }

    private void LoadTasks()
    {
        try
        {
            using var db = new AdmXmlDbContext(_dbPath);
            var tasks = db.Tasks.OrderBy(t => t.Name).ToList();
            TasksList.ItemsSource = tasks;
        }
        catch { /* ignore */ }
    }

    private void TasksList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _selectedTask = TasksList.SelectedItem as IntegrationTask;
        if (_selectedTask != null)
        {
            TaskName.Text = _selectedTask.Name;
            TaskInputPath.Text = _selectedTask.InputPath;
            TaskOutputPath.Text = _selectedTask.OutputPath;
            TaskErrorPath.Text = _selectedTask.ErrorPath;
            TaskPrependToFileName.Text = _selectedTask.PrependToFileName ?? "";
            TaskTableName.Text = _selectedTask.TableName;
            TaskCronExpression.Text = _selectedTask.CronExpression;
            TaskErrorEmails.Text = _selectedTask.ErrorEmails ?? "";
            TaskIsEnabled.IsChecked = _selectedTask.IsEnabled;
            TaskMultiRecord.IsChecked = !string.IsNullOrWhiteSpace(_selectedTask.MultiRecordRootXPath);
            TaskMultiRecordXPath.Text = _selectedTask.MultiRecordRootXPath ?? "";
            TaskTextToRemove.Text = _selectedTask.TextToRemoveInXPath ?? "";
            TaskUseConnectionString.IsChecked = false;
            TaskConnectionStringPanel.Visibility = Visibility.Collapsed;
            TaskConnectionString.Text = "";
            TaskDbPassword.Password = "";

            var connStr = DataProtectionHelper.Unprotect(_selectedTask.EncryptedConnectionString);
            if (!string.IsNullOrEmpty(connStr))
            {
                var (host, dbName, user) = DbConnectionHelper.ParseConnectionString(connStr);
                TaskDbHost.Text = host ?? "";
                TaskDbName.Text = dbName ?? "";
                TaskDbUser.Text = user ?? "";
            }
            else
            {
                TaskDbHost.Text = "";
                TaskDbName.Text = "";
                TaskDbUser.Text = "";
            }

            using var db = new AdmXmlDbContext(_dbPath);
            var taskWithNav = db.Tasks.Include(t => t.Mappings).Include(t => t.TargetDirectoryComponents)
                .FirstOrDefault(t => t.Id == _selectedTask.Id);
            if (taskWithNav != null)
            {
                TaskStaticTargetDir.Text = taskWithNav.StaticTargetDirectory ?? "";
                var dirItems = taskWithNav.TargetDirectoryComponents.OrderBy(c => c.SortOrder)
                    .Select(c => new TargetDirComponentItem(c.Id, c.ComponentType, c.Value, c.SortOrder)).ToList();
                TargetDirComponentsList.ItemsSource = new ObservableCollection<TargetDirComponentItem>(dirItems);

                var mappings = taskWithNav.Mappings.OrderBy(m => m.SortOrder).ToList();
                var items = mappings.Select((m, i) => new MappingEditItem
                {
                    Id = m.Id,
                    XPath = m.XPath,
                    ColumnName = m.ColumnName,
                    SqlDataType = m.SqlDataType,
                    DefaultValue = m.DefaultValue ?? "",
                    FindValue = m.FindValue ?? "",
                    ReplaceValue = m.ReplaceValue ?? "",
                    SortOrder = m.SortOrder,
                    IsLiteral = m.IsLiteral,
                    ValueTemplate = m.ValueTemplate ?? ""
                }).ToList();
                MappingsGrid.ItemsSource = new ObservableCollection<MappingEditItem>(items);
            }
        }
        else
        {
            MappingsGrid.ItemsSource = null;
            TargetDirComponentsList.ItemsSource = null;
        }
    }

    private void AddTaskBtn_Click(object sender, RoutedEventArgs e)
    {
        _selectedTask = null;
        TasksList.SelectedItem = null;
        TaskName.Text = "";
        TaskInputPath.Text = "";
        TaskOutputPath.Text = "";
        TaskErrorPath.Text = "";
        TaskPrependToFileName.Text = "";
        TaskDbHost.Text = "";
        TaskDbName.Text = "";
        TaskDbUser.Text = "";
        TaskDbPassword.Password = "";
        TaskConnectionString.Text = "";
        TaskUseConnectionString.IsChecked = false;
        TaskConnectionStringPanel.Visibility = Visibility.Collapsed;
        TaskTableName.Text = "";
        TaskColumnsList.ItemsSource = null;
        TaskMultiRecord.IsChecked = false;
        TaskMultiRecordXPath.Text = "";
        TaskTextToRemove.Text = "";
        TaskStaticTargetDir.Text = "";
        TargetDirComponentsList.ItemsSource = new ObservableCollection<TargetDirComponentItem>();
        TaskCronExpression.Text = "0 0 * * * ?";
        TaskErrorEmails.Text = "";
        TaskIsEnabled.IsChecked = true;
        MappingsGrid.ItemsSource = new ObservableCollection<MappingEditItem>();
    }

    private void DeleteTaskBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTask == null) return;
        if (System.Windows.MessageBox.Show("Delete this task?", "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;
        try
        {
            using var db = new AdmXmlDbContext(_dbPath);
            var toRemove = db.Tasks.Find(_selectedTask!.Id);
            if (toRemove != null)
                db.Tasks.Remove(toRemove);
            db.SaveChanges();
            LoadTasks();
            AddTaskBtn_Click(sender, e);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error");
        }
    }

    private void BrowseFolder(System.Windows.Controls.TextBox target)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Select folder",
            ShowNewFolderButton = true
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            target.Text = dlg.SelectedPath;
    }

    private void BrowseInputPath_Click(object sender, RoutedEventArgs e) => BrowseFolder(TaskInputPath);
    private void BrowseOutputPath_Click(object sender, RoutedEventArgs e) => BrowseFolder(TaskOutputPath);
    private void BrowseErrorPath_Click(object sender, RoutedEventArgs e) => BrowseFolder(TaskErrorPath);

    private void TaskUseConnectionString_Changed(object sender, RoutedEventArgs e)
    {
        var useManual = TaskUseConnectionString.IsChecked == true;
        TaskConnectionStringPanel.Visibility = useManual ? Visibility.Visible : Visibility.Collapsed;
        TaskDbHost.IsEnabled = TaskDbName.IsEnabled = TaskDbUser.IsEnabled = TaskDbPassword.IsEnabled = TaskDbConnectBtn.IsEnabled = !useManual;
    }

    private void TaskDbConnectBtn_Click(object sender, RoutedEventArgs e)
    {
        var connStr = GetCurrentConnectionString();
        if (string.IsNullOrEmpty(connStr))
        {
            System.Windows.MessageBox.Show("Enter host and database, or use manual connection string.", "Error");
            return;
        }
        var (ok, msg) = DbConnectionHelper.TestConnection(connStr);
        System.Windows.MessageBox.Show(msg, ok ? "Success" : "Error");
    }

    private void TaskGetColumnsBtn_Click(object sender, RoutedEventArgs e)
    {
        var connStr = GetCurrentConnectionString();
        var table = TaskTableName.Text?.Trim();
        if (string.IsNullOrEmpty(connStr) || string.IsNullOrEmpty(table))
        {
            System.Windows.MessageBox.Show("Enter connection details and table name first.", "Error");
            return;
        }
        var cols = DbConnectionHelper.GetTableColumns(connStr, table);
        TaskColumnsList.ItemsSource = cols.Select(c => $"{c.ColumnName} ({c.DataType})").ToList();
    }

    private string GetCurrentConnectionString()
    {
        if (TaskUseConnectionString.IsChecked == true)
            return TaskConnectionString.Text ?? "";
        return DbConnectionHelper.BuildConnectionString(
            TaskDbHost.Text ?? "",
            TaskDbName.Text ?? "",
            TaskDbUser.Text,
            TaskDbPassword.Password);
    }

    private void TaskTestXPathBtn_Click(object sender, RoutedEventArgs e)
    {
        var xml = _sampleXmlForTask ?? _loadedXmlContent;
        if (string.IsNullOrEmpty(xml))
        {
            System.Windows.MessageBox.Show("Örnek XML yükleyin (Veri Eşleştirmeleri bölümünde) veya Simulation sekmesinde XML yükleyin.", "Bilgi");
            return;
        }
        var xpath = TaskMultiRecordXPath.Text?.Trim();
        if (string.IsNullOrEmpty(xpath))
        {
            System.Windows.MessageBox.Show("Test etmek için bir XPath girin.", "Hata");
            return;
        }
        var value = XmlParser.TestXPath(xml, xpath);
        var count = XmlParser.GetXPathNodeCount(xml, xpath);
        System.Windows.MessageBox.Show($"İlk eşleşme: {(value ?? "(null)")}\nToplam düğüm: {count}", "XPath Test");
    }

    private void TargetDirAddXPath_Click(object sender, RoutedEventArgs e)
    {
        var val = TargetDirStaticInput.Text?.Trim();
        if (string.IsNullOrEmpty(val)) return;
        var src = TargetDirComponentsList.ItemsSource as ObservableCollection<TargetDirComponentItem> ?? new ObservableCollection<TargetDirComponentItem>();
        if (TargetDirComponentsList.ItemsSource == null) TargetDirComponentsList.ItemsSource = src;
        src.Add(new TargetDirComponentItem { ComponentType = TargetDirectoryComponentType.XPath, Value = val, SortOrder = src.Count });
        TargetDirStaticInput.Text = "";
    }

    private void TargetDirAddStatic_Click(object sender, RoutedEventArgs e)
    {
        var val = TargetDirStaticInput.Text?.Trim();
        if (string.IsNullOrEmpty(val)) return;
        var src = TargetDirComponentsList.ItemsSource as ObservableCollection<TargetDirComponentItem> ?? new ObservableCollection<TargetDirComponentItem>();
        if (TargetDirComponentsList.ItemsSource == null) TargetDirComponentsList.ItemsSource = src;
        src.Add(new TargetDirComponentItem { ComponentType = TargetDirectoryComponentType.Static, Value = val, SortOrder = src.Count });
        TargetDirStaticInput.Text = "";
    }

    private void TargetDirRemove_Click(object sender, RoutedEventArgs e)
    {
        var src = TargetDirComponentsList.ItemsSource as ObservableCollection<TargetDirComponentItem>;
        var sel = TargetDirComponentsList.SelectedItem as TargetDirComponentItem;
        if (src != null && sel != null) src.Remove(sel);
    }

    private void TargetDirMoveUp_Click(object sender, RoutedEventArgs e)
    {
        var src = TargetDirComponentsList.ItemsSource as ObservableCollection<TargetDirComponentItem>;
        var sel = TargetDirComponentsList.SelectedItem as TargetDirComponentItem;
        if (src == null || sel == null) return;
        var idx = src.IndexOf(sel);
        if (idx <= 0) return;
        src.RemoveAt(idx);
        src.Insert(idx - 1, sel);
        for (var i = 0; i < src.Count; i++) src[i].SortOrder = i;
    }

    private void TargetDirMoveDown_Click(object sender, RoutedEventArgs e)
    {
        var src = TargetDirComponentsList.ItemsSource as ObservableCollection<TargetDirComponentItem>;
        var sel = TargetDirComponentsList.SelectedItem as TargetDirComponentItem;
        if (src == null || sel == null) return;
        var idx = src.IndexOf(sel);
        if (idx < 0 || idx >= src.Count - 1) return;
        src.RemoveAt(idx);
        src.Insert(idx + 1, sel);
        for (var i = 0; i < src.Count; i++) src[i].SortOrder = i;
    }

    private void MappingMoveUpBtn_Click(object sender, RoutedEventArgs e)
    {
        var src = MappingsGrid.ItemsSource as ObservableCollection<MappingEditItem>;
        var sel = MappingsGrid.SelectedItem as MappingEditItem;
        if (src == null || sel == null) return;
        var idx = src.IndexOf(sel);
        if (idx <= 0) return;
        src.RemoveAt(idx);
        src.Insert(idx - 1, sel);
    }

    private void MappingMoveDownBtn_Click(object sender, RoutedEventArgs e)
    {
        var src = MappingsGrid.ItemsSource as ObservableCollection<MappingEditItem>;
        var sel = MappingsGrid.SelectedItem as MappingEditItem;
        if (src == null || sel == null) return;
        var idx = src.IndexOf(sel);
        if (idx < 0 || idx >= src.Count - 1) return;
        src.RemoveAt(idx);
        src.Insert(idx + 1, sel);
    }

    private void ConfigSaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTask == null) { System.Windows.MessageBox.Show("Select a task first.", "Error"); return; }
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            ConfigXmlSerializer.Export(_dbPath, _selectedTask.Id, dlg.FileName);
            System.Windows.MessageBox.Show("Config exported.", "Saved");
        }
        catch (Exception ex) { System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error"); }
    }

    private void ConfigLoadBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var taskId = ConfigXmlSerializer.Import(_dbPath, dlg.FileName);
            LoadTasks();
            LoadSimulationTasks();
            _selectedTask = null;
            var tasks = TasksList.ItemsSource as IEnumerable<IntegrationTask>;
            _selectedTask = tasks?.FirstOrDefault(t => t.Id == taskId);
            if (_selectedTask != null) TasksList.SelectedItem = _selectedTask;
            System.Windows.MessageBox.Show("Config imported.", "Loaded");
        }
        catch (Exception ex) { System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error"); }
    }

    private void SaveTaskBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(TaskName.Text))
            {
                System.Windows.MessageBox.Show("Task name is required.", "Error");
                return;
            }
            var cron = (TaskCronExpression.Text ?? "").Trim();
            if (!string.IsNullOrEmpty(cron) && !Quartz.CronExpression.IsValidExpression(cron))
            {
                System.Windows.MessageBox.Show("Geçersiz cron ifadesi. Örnek: 0 0 * * * ? (her saat)", "Hata");
                return;
            }

            using var db = new AdmXmlDbContext(_dbPath);
            IntegrationTask task;
            if (_selectedTask != null)
            {
                task = db.Tasks.Include(t => t.Mappings).Include(t => t.TargetDirectoryComponents).First(t => t.Id == _selectedTask.Id);
            }
            else
            {
                task = new IntegrationTask();
                db.Tasks.Add(task);
            }
            task.Name = TaskName.Text.Trim();
            task.InputPath = TaskInputPath.Text ?? "";
            task.OutputPath = TaskOutputPath.Text ?? "";
            task.ErrorPath = TaskErrorPath.Text ?? "";
            task.PrependToFileName = string.IsNullOrWhiteSpace(TaskPrependToFileName.Text) ? null : TaskPrependToFileName.Text.Trim();
            task.MultiRecordRootXPath = TaskMultiRecord.IsChecked == true && !string.IsNullOrWhiteSpace(TaskMultiRecordXPath.Text)
                ? TaskMultiRecordXPath.Text.Trim() : null;
            task.TextToRemoveInXPath = string.IsNullOrWhiteSpace(TaskTextToRemove.Text) ? null : TaskTextToRemove.Text.Trim();
            task.StaticTargetDirectory = string.IsNullOrWhiteSpace(TaskStaticTargetDir.Text) ? null : TaskStaticTargetDir.Text.Trim();

            var connStr = TaskUseConnectionString.IsChecked == true ? TaskConnectionString.Text
                : DbConnectionHelper.BuildConnectionString(TaskDbHost.Text ?? "", TaskDbName.Text ?? "", TaskDbUser.Text, TaskDbPassword.Password);
            if (!string.IsNullOrEmpty(connStr))
                task.EncryptedConnectionString = DataProtectionHelper.Protect(connStr);

            task.TableName = TaskTableName.Text ?? "";
            task.CronExpression = string.IsNullOrWhiteSpace(TaskCronExpression.Text) ? "0 0 * * * ?" : TaskCronExpression.Text.Trim();
            task.ErrorEmails = TaskErrorEmails.Text ?? "";
            task.IsEnabled = TaskIsEnabled.IsChecked == true;

            db.TargetDirectoryComponents.RemoveRange(task.TargetDirectoryComponents);
            task.TargetDirectoryComponents.Clear();
            var dirItems = TargetDirComponentsList.ItemsSource as IEnumerable<TargetDirComponentItem> ?? [];
            var sortOrder = 0;
            foreach (var c in dirItems)
            {
                task.TargetDirectoryComponents.Add(new TargetDirectoryComponent
                {
                    ComponentType = c.ComponentType,
                    Value = c.Value,
                    SortOrder = sortOrder++
                });
            }

            db.SaveChanges();
            _selectedTask = task;
            LoadTasks();
            LoadSimulationTasks();
            System.Windows.MessageBox.Show("Task saved.", "Saved");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error");
        }
    }

    private void AddMappingBtn_Click(object sender, RoutedEventArgs e)
    {
        var src = MappingsGrid.ItemsSource as ObservableCollection<MappingEditItem>;
        if (src == null)
            MappingsGrid.ItemsSource = src = new ObservableCollection<MappingEditItem>();
        src.Add(new MappingEditItem { SqlDataType = SqlDataType.VARCHAR });
    }

    private void SaveMappingsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTask == null)
        {
            System.Windows.MessageBox.Show("Select a task first.", "Error");
            return;
        }
        try
        {
            var items = (MappingsGrid.ItemsSource as ObservableCollection<MappingEditItem>)?.ToList() ?? new List<MappingEditItem>();
            using var db = new AdmXmlDbContext(_dbPath);
            var existing = db.TaskMappings.Where(m => m.TaskId == _selectedTask.Id).ToList();
            db.TaskMappings.RemoveRange(existing);
            foreach (var item in items.Where(i => !string.IsNullOrWhiteSpace(i.ColumnName) &&
                (!string.IsNullOrWhiteSpace(i.XPath) || i.IsLiteral || !string.IsNullOrWhiteSpace(i.ValueTemplate))))
            {
                db.TaskMappings.Add(new TaskMapping
                {
                    TaskId = _selectedTask.Id,
                    XPath = item.XPath ?? "",
                    ColumnName = item.ColumnName,
                    SqlDataType = item.SqlDataType,
                    DefaultValue = string.IsNullOrWhiteSpace(item.DefaultValue) ? null : item.DefaultValue.Trim(),
                    FindValue = string.IsNullOrWhiteSpace(item.FindValue) ? null : item.FindValue.Trim(),
                    ReplaceValue = string.IsNullOrWhiteSpace(item.ReplaceValue) ? null : item.ReplaceValue.Trim(),
                    SortOrder = items.IndexOf(item),
                    IsLiteral = item.IsLiteral,
                    ValueTemplate = string.IsNullOrWhiteSpace(item.ValueTemplate) ? null : item.ValueTemplate.Trim()
                });
            }
            db.SaveChanges();
            System.Windows.MessageBox.Show("Mappings saved.", "Saved");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error");
        }
    }

    private void LoadSimulationTasks()
    {
        try
        {
            using var db = new AdmXmlDbContext(_dbPath);
            var tasks = db.Tasks.Include(t => t.Mappings).ToList();
            SimTaskCombo.ItemsSource = tasks;
        }
        catch { /* ignore */ }
    }

    private void SimTaskCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Refresh parsed data if we have XML loaded
        if (!string.IsNullOrEmpty(_loadedXmlContent) && SimTaskCombo.SelectedItem is IntegrationTask t && t.Mappings.Any())
            ParseAndShow();
    }

    private string? _loadedXmlContent;
    private string? _sampleXmlForTask;

    private void LoadSampleXmlBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                _sampleXmlForTask = File.ReadAllText(dlg.FileName);
                SampleXmlPreview.Text = _sampleXmlForTask;
                SampleXmlPreview.ToolTip = dlg.FileName;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"XML yüklenemedi: {ex.Message}", "Hata");
            }
        }
    }

    private void ClearSampleXmlBtn_Click(object sender, RoutedEventArgs e)
    {
        _sampleXmlForTask = null;
        SampleXmlPreview.Text = "";
        SampleXmlPreview.ToolTip = null;
    }

    private void LoadXmlBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                _loadedXmlContent = File.ReadAllText(dlg.FileName);
                ParseAndShow();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading XML: {ex.Message}", "Error");
            }
        }
    }

    private void ParseAndShow()
    {
        if (string.IsNullOrEmpty(_loadedXmlContent) || SimTaskCombo.SelectedItem is not IntegrationTask task)
        {
            ParsedDataGrid.ItemsSource = null;
            SqlOutput.Text = "";
            return;
        }
        try
        {
            var allRows = XmlParser.ExtractAllRows(_loadedXmlContent, task);
            var displayRows = allRows.Select(row =>
                row.ToDictionary(k => k.Key, v => v.Value?.ToString() ?? "(null)")
            ).ToList();
            ParsedDataGrid.ItemsSource = displayRows;

            var sqlLines = allRows.Select(row => SqlInsertBuilder.BuildInsertStatement(task.TableName, row));
            SqlOutput.Text = string.Join("\n", sqlLines);
        }
        catch (Exception ex)
        {
            SqlOutput.Text = $"Error parsing: {ex.Message}";
        }
    }

    private async void DryRunBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SimTaskCombo.SelectedItem is not IntegrationTask task)
        {
            System.Windows.MessageBox.Show("Select a task.", "Error");
            return;
        }
        if (string.IsNullOrEmpty(_loadedXmlContent))
        {
            System.Windows.MessageBox.Show("Load an XML file first.", "Error");
            return;
        }

        DryRunBtn.IsEnabled = false;
        try
        {
            var result = await Task.Run(() =>
            {
                try
                {
                    var connStr = DataProtectionHelper.Unprotect(task.EncryptedConnectionString);
                    if (string.IsNullOrEmpty(connStr))
                        return "Could not decrypt connection string.";
                    var allRows = XmlParser.ExtractAllRows(_loadedXmlContent!, task);
                    using var conn = new SqlConnection(connStr);
                    conn.Open();
                    SqlTransaction? tran = null;
                    try
                    {
                        tran = conn.BeginTransaction();
                        var totalRows = 0;
                        foreach (var values in allRows)
                            totalRows += SqlInsertBuilder.ExecuteInsert(conn, tran, task.TableName, values);
                        return $"Rows that would be affected: {totalRows} (rolled back)";
                    }
                    finally
                    {
                        tran?.Rollback();
                    }
                }
                catch (Exception ex)
                {
                    return $"Error: {ex.Message}";
                }
            });
            SqlOutput.Text = SqlOutput.Text + "\n\n--- Dry Run Result ---\n" + result;
        }
        finally
        {
            DryRunBtn.IsEnabled = true;
        }
    }
}
