using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.ServiceProcess;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Xml;
using AdmXmlDb.Core;
using AdmXmlDb.Core.Entities;
using AdmXmlDb.Management.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using DataGrid = System.Windows.Controls.DataGrid;

namespace AdmXmlDb.Management;

public partial class MainWindow : Window
{
    private readonly string _dbPath;
    private int _currentLogPage = 0;
    private IntegrationTask? _selectedTask;
    private System.Windows.Threading.DispatcherTimer? _statusTimer;
    private System.Windows.Threading.DispatcherTimer? _statusClearTimer;
    private string? _loadedXmlContent;
    private string? _taskXmlContent;
    private string? _currentConnectionString;

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
        ApplyLanguage(_currentLang);

        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        if (ver != null)
        {
            var verStr = $"v{ver.Major}.{ver.Minor}.{ver.Build}";
            if (StatusBarVersion != null) StatusBarVersion.Text = verStr;
            if (AboutVersionText != null) AboutVersionText.Text = $"AdmXmlDb {verStr}";
        }
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
            ShowUserFriendlyError(ex);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _statusTimer?.Stop();
        _statusTimer = null;
        _statusClearTimer?.Stop();
        _statusClearTimer = null;
        base.OnClosed(e);
    }

    // ======================== STATUS BAR ========================

    private void SetStatus(string message, bool isError = false)
    {
        if (StatusBarText == null) return;
        StatusBarText.Text = message;
        StatusBarText.Foreground = isError
            ? System.Windows.Media.Brushes.DarkRed
            : new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x10, 0x7C, 0x10));

        _statusClearTimer?.Stop();
        _statusClearTimer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromSeconds(5) };
        _statusClearTimer.Tick += (_, _) =>
        {
            if (StatusBarText != null)
            {
                StatusBarText.Text = "";
                StatusBarText.Foreground = System.Windows.Media.Brushes.DimGray;
            }
            _statusClearTimer?.Stop();
        };
        _statusClearTimer.Start();
    }

    // ======================== SERVICE STATUS ========================

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

    // ======================== LOGS ========================

    private async void LoadLogs()
    {
        try
        {
            var dbPath = _dbPath;
            var page = _currentLogPage;
            var filterName = (_selectedDashboardTask != null && _selectedDashboardTask.Id > 0)
                ? _selectedDashboardTask.Name : null;
            var pageSize = Constants.UI.LogPageSize;

            var (logs, total) = await Task.Run(() =>
            {
                using var db = new AdmXmlDbContext(dbPath);
                IQueryable<AdmXmlDb.Core.Entities.ExecutionLog> q = db.ExecutionLogs.AsNoTracking();
                if (filterName != null) q = q.Where(x => x.TaskName == filterName);
                var items = q.OrderByDescending(x => x.Timestamp)
                    .Skip(page * pageSize).Take(pageSize).ToList();
                var count = q.Count();
                return (items, count);
            });

            LogsGrid.ItemsSource = logs;
            var totalPages = total == 0 ? 1 : (int)Math.Ceiling(total / (double)pageSize);
            LogPageInfo.Text = $"Page {page + 1} / {totalPages} ({total} records)";
            LogPrevBtn.IsEnabled = page > 0;
            LogNextBtn.IsEnabled = (page + 1) * pageSize < total;
        }
        catch (Exception ex)
        {
            ShowUserFriendlyError(ex);
        }
    }

    private void RefreshLogsBtn_Click(object sender, RoutedEventArgs e) => LoadLogs();
    private void LogPrevBtn_Click(object sender, RoutedEventArgs e) { _currentLogPage--; LoadLogs(); }
    private void LogNextBtn_Click(object sender, RoutedEventArgs e) { _currentLogPage++; LoadLogs(); }

    private void LogsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (LogsGrid.SelectedItem is AdmXmlDb.Core.Entities.ExecutionLog log && !string.IsNullOrEmpty(log.Message))
        {
            var title = _currentLang == "tr" ? "Log Detayı" : "Log Detail";
            var header = _currentLang == "tr"
                ? $"Görev: {log.TaskName}\nDosya: {log.Filename}\nDurum: {log.Status}\nZaman: {log.Timestamp:yyyy-MM-dd HH:mm:ss}\n\nDetay:"
                : $"Task: {log.TaskName}\nFile: {log.Filename}\nStatus: {log.Status}\nTime: {log.Timestamp:yyyy-MM-dd HH:mm:ss}\n\nDetails:";
            System.Windows.MessageBox.Show($"{header}\n\n{log.Message}", title, MessageBoxButton.OK,
                log.Status == "Failed" ? MessageBoxImage.Error : MessageBoxImage.Information);
        }
    }

    private void CleanLogsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show("90 günden eski tüm loglar silinecek. Devam edilsin mi?",
            "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;
        try
        {
            using var db = new AdmXmlDbContext(_dbPath);
            var cutoff = DateTime.UtcNow.AddDays(-90);
            var count = db.ExecutionLogs.Where(l => l.Timestamp < cutoff).ExecuteDelete();
            System.Windows.MessageBox.Show($"{count} old log entries deleted.", "Done");
            _currentLogPage = 0;
            LoadLogs();
        }
        catch (Exception ex)
        {
            ShowUserFriendlyError(ex);
        }
    }

    // ======================== SMTP SETTINGS ========================

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
                SmtpPassword.Password = "";
            }
        }
        catch (Exception ex) { ShowUserFriendlyError(ex); }
    }

    private async void SaveSmtpBtn_Click(object sender, RoutedEventArgs e)
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
            if (SmtpPassword.SecurePassword.Length > 0)
                smtp.EncryptedPassword = DataProtectionHelper.Protect(SmtpPassword.SecurePassword);
            await db.SaveChangesAsync();
            SetStatus(_currentLang == "tr" ? "✓ SMTP ayarları kaydedildi." : "✓ SMTP settings saved.");
        }
        catch (Exception ex)
        {
            ShowUserFriendlyError(ex);
        }
    }

    private async void TestSmtpBtn_Click(object sender, RoutedEventArgs e)
    {
        TestSmtpBtn.IsEnabled = false;
        try
        {
            var result = await Task.Run(async () =>
            {
                try
                {
                    using var db = new AdmXmlDbContext(_dbPath);
                    var smtp = db.SmtpSettings.FirstOrDefault();
                    if (smtp == null || string.IsNullOrEmpty(smtp.Host))
                        return "SMTP not configured.";
                    var error = await SmtpService.TestAsync(smtp);
                    return error == null ? "Connection successful!" : $"Failed: {error}";
                }
                catch (Exception ex)
                {
                    var msg = ex.InnerException != null ? $"{ex.Message} -> {ex.InnerException.Message}" : ex.Message;
                    return $"Failed: {msg}";
                }
            });
            System.Windows.MessageBox.Show(result, "Test Connection");
        }
        finally
        {
            TestSmtpBtn.IsEnabled = true;
        }
    }

    // ======================== TASK LIST ========================

    private async void LoadTasks()
    {
        try
        {
            var dbPath = _dbPath;
            var tasks = await Task.Run(() =>
            {
                using var db = new AdmXmlDbContext(dbPath);
                return db.Tasks.AsNoTracking().OrderBy(t => t.Name).ToList();
            });

            // Capture before ItemsSource replacement clears selection
            var savedId = _selectedTask?.Id;

            // Suppress SelectionChanged while swapping ItemsSource to avoid ClearTaskFields
            _suppressTaskSelectionChanged = true;
            try
            {
                TasksCombo.ItemsSource = tasks;
                if (SettingsTasksCombo != null)
                    SettingsTasksCombo.ItemsSource = tasks;
                if (DashboardTasksCombo != null)
                {
                    var dashboardTasks = new List<IntegrationTask> { new IntegrationTask { Id = -1, Name = _currentLang == "tr" ? "Tümü" : "All Tasks" } };
                    dashboardTasks.AddRange(tasks);
                    DashboardTasksCombo.ItemsSource = dashboardTasks;
                }
            }
            finally { _suppressTaskSelectionChanged = false; }

            // Re-select the previously selected task by Id (new object from fresh DB load)
            if (savedId.HasValue)
            {
                var match = tasks.FirstOrDefault(t => t.Id == savedId.Value);
                if (match != null)
                {
                    TasksCombo.SelectedItem = match;
                    // SelectionChanged will fire and call PopulateTaskFields
                }
                else
                {
                    // Task was deleted, clear form
                    _selectedTask = null;
                    ClearTaskFields();
                }
            }
        }
        catch (Exception ex) { ShowUserFriendlyError(ex); }
    }

    private bool _syncingTaskCombos;
    private bool _suppressTaskSelectionChanged;

    private void TasksCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTaskSelectionChanged) return;

        _selectedTask = TasksCombo.SelectedItem as IntegrationTask;
        if (_selectedTask != null)
            PopulateTaskFields(_selectedTask);
        else
            ClearTaskFields();

        SyncTaskCombo(TasksCombo, SettingsTasksCombo);
    }

    private void SettingsTasksCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingTaskCombos) return;
        var task = SettingsTasksCombo.SelectedItem as IntegrationTask;
        if (task != null)
        {
            _selectedTask = task;
            PopulateTaskFields(task);
            SyncTaskCombo(SettingsTasksCombo, TasksCombo);
        }
    }

    private void SyncTaskCombo(System.Windows.Controls.ComboBox source, System.Windows.Controls.ComboBox target)
    {
        if (_syncingTaskCombos || target == null) return;
        _syncingTaskCombos = true;
        try
        {
            var selected = source.SelectedItem as IntegrationTask;
            if (selected != null && target.ItemsSource is IEnumerable<IntegrationTask> items)
            {
                var match = items.FirstOrDefault(t => t.Id == selected.Id);
                if (match != null) target.SelectedItem = match;
            }
        }
        finally { _syncingTaskCombos = false; }
    }

    private void PopulateTaskFields(IntegrationTask task)
    {
        if (TaskSettingsContainer != null) TaskSettingsContainer.IsEnabled = true;

        TaskName.Text = task.Name;
        TaskInputPath.Text = task.InputPath;
        TaskOutputPath.Text = task.OutputPath;
        TaskErrorPath.Text = task.ErrorPath;
        TaskPrependToFileName.Text = task.PrependToFileName ?? "";
        TaskFileNameSuffix.Text = task.FileNameSuffix ?? "";
        TaskAddDateTime.IsChecked = task.AddDateTimeToFileName;
        TaskCronExpression.Text = task.CronExpression;
        TaskErrorEmails.Text = task.ErrorEmails ?? "";
        TaskIsEnabled.IsChecked = task.IsEnabled;
        TaskNetworkUsername.Text = task.NetworkUsername ?? "";
        TaskNetworkPassword.Password = "";
        if (task.EncryptedNetworkPassword != null && task.EncryptedNetworkPassword.Length > 0)
        {
            try { TaskNetworkPassword.Password = DataProtectionHelper.Unprotect(task.EncryptedNetworkPassword); } catch { }
        }
        var delay = task.ProcessingDelaySeconds > 0 ? task.ProcessingDelaySeconds : 5;
        TaskProcessingDelay.Text = delay.ToString();
        TaskProcessingDelaySlider.Value = delay;
        TaskMultiRecord.IsChecked = !string.IsNullOrWhiteSpace(task.MultiRecordRootXPath);
        TaskDbPassword.Password = "";

        var connStr = DataProtectionHelper.Unprotect(task.EncryptedConnectionString);
        if (!string.IsNullOrEmpty(connStr))
        {
            _currentConnectionString = connStr;
            var (host, dbName, user) = DbConnectionHelper.ParseConnectionString(connStr);
            TaskDbHost.Text = host ?? "";
            TaskDbUser.Text = user ?? "";
            try {
                var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connStr);
                TaskDbPassword.Password = builder.Password ?? "";
            } catch { }

            if (string.IsNullOrEmpty(user))
            {
                AuthWindows.IsChecked = true;
            }
            else
            {
                AuthSql.IsChecked = true;
            }

            // Try populating databases and selecting the right one
            var databases = DbConnectionHelper.GetDatabases(connStr);
            DatabasesCombo.ItemsSource = databases;
            if (!string.IsNullOrEmpty(dbName))
            {
                var idx = databases.FindIndex(d => string.Equals(d, dbName, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    DatabasesCombo.SelectedIndex = idx;
                    // DatabasesCombo_SelectionChanged fires synchronously above → tables loaded
                    // Re-select the saved table from TaskTableName
                    if (!string.IsNullOrEmpty(task.TableName)
                        && TaskTablesList.ItemsSource is IEnumerable<string> tableList)
                    {
                        var tblMatch = tableList.FirstOrDefault(
                            t => string.Equals(t, task.TableName, StringComparison.OrdinalIgnoreCase));
                        if (tblMatch != null)
                            TaskTablesList.SelectedItem = tblMatch;
                    }
                }
            }
        }
        else
        {
            TaskDbHost.Text = "";
            TaskDbUser.Text = "";
            _currentConnectionString = null;
            DatabasesCombo.ItemsSource = null;
            TaskTablesList.ItemsSource = null;
        }

        using var db = new AdmXmlDbContext(_dbPath);
        var taskWithNav = db.Tasks
            .AsNoTracking()
            .Include(t => t.Mappings)
            .Include(t => t.TargetDirectoryComponents)
            .FirstOrDefault(t => t.Id == task.Id);
        if (taskWithNav != null)
        {
            // Static target dir always mirrors output path
            TaskStaticTargetDir.Text = taskWithNav.StaticTargetDirectory ?? task.OutputPath ?? "";
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
                ValueTemplate = m.ValueTemplate ?? "",
                TargetTableName = m.TargetTableName ?? task.TableName ?? "",
                IsRequired = m.IsRequired
            }).ToList();
            MappingsGrid.ItemsSource = new ObservableCollection<MappingEditItem>(items);
        }
        else
        {
            MappingsGrid.ItemsSource = null;
            TargetDirComponentsList.ItemsSource = null;
        }

        UpdateFileNamePreview();
        UpdateFolderPreviews();
    }
    
    // ======================== DASHBOARD TAB TASKS ========================
    
    private IntegrationTask? _selectedDashboardTask;

    private void DashboardTasksCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedDashboardTask = DashboardTasksCombo.SelectedItem as IntegrationTask;
        if (_selectedDashboardTask != null && _selectedDashboardTask.Id > 0)
        {
            DashboardTaskStatusBorder.Visibility = Visibility.Visible;
            UpdateDashboardTaskStatusUI(_selectedDashboardTask.IsEnabled);
        }
        else
        {
            DashboardTaskStatusBorder.Visibility = Visibility.Collapsed;
        }
        
        _currentLogPage = 0;
        LoadLogs();
    }

    private void UpdateDashboardTaskStatusUI(bool isEnabled)
    {
        if (isEnabled)
        {
            DashboardTaskStatusLight.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LimeGreen);
            DashboardTaskStatusText.Text = _currentLang == "tr" ? "Çalışıyor" : "Running";
            DashboardTaskToggleStateBtn.Content = _currentLang == "tr" ? "Durdur" : "Stop";
        }
        else
        {
            DashboardTaskStatusLight.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.OrangeRed);
            DashboardTaskStatusText.Text = _currentLang == "tr" ? "Duraklatıldı" : "Stopped";
            DashboardTaskToggleStateBtn.Content = _currentLang == "tr" ? "Başlat" : "Start";
        }
    }

    private void DashboardTaskToggleStateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDashboardTask == null || _selectedDashboardTask.Id == 0) return;

        try
        {
            using var db = new AdmXmlDbContext(_dbPath);
            var dbTask = db.Tasks.Find(_selectedDashboardTask.Id);
            if (dbTask != null)
            {
                dbTask.IsEnabled = !dbTask.IsEnabled;
                db.SaveChanges();
                
                // Update local instances
                _selectedDashboardTask.IsEnabled = dbTask.IsEnabled;
                if (_selectedTask != null && _selectedTask.Id == dbTask.Id) {
                    _selectedTask.IsEnabled = dbTask.IsEnabled;
                    if (TaskIsEnabled != null) TaskIsEnabled.IsChecked = dbTask.IsEnabled;
                }
                
                UpdateDashboardTaskStatusUI(dbTask.IsEnabled);
                
                var statusText = _currentLang == "tr" 
                    ? (dbTask.IsEnabled ? "Görev başlatıldı. Worker servisi değişiklikleri yakında algılayacak." : "Görev durduruldu. Worker servisi görev işlemeyi kesecek.") 
                    : (dbTask.IsEnabled ? "Task started. Worker service will reload shortly." : "Task stopped. Worker service will stop processing.");
                
                System.Windows.MessageBox.Show(statusText, _currentLang == "tr" ? "Bilgi" : "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            ShowUserFriendlyError(ex);
        }
    }

    private void ClearTaskFields()
    {
        if (TaskSettingsContainer != null) TaskSettingsContainer.IsEnabled = false;

        TaskName.Text = "";
        TaskInputPath.Text = "";
        TaskOutputPath.Text = "";
        TaskErrorPath.Text = "";
        TaskPrependToFileName.Text = "";
        TaskFileNameSuffix.Text = "";
        TaskAddDateTime.IsChecked = false;
        TaskDbHost.Text = "";
        TaskDbUser.Text = "";
        TaskDbPassword.Password = "";
        TaskMultiRecord.IsChecked = false;
        TaskStaticTargetDir.Text = "";
        TargetDirComponentsList.ItemsSource = new ObservableCollection<TargetDirComponentItem>();
        TaskCronExpression.Text = Constants.UI.DefaultCronExpression;
        TaskErrorEmails.Text = "";
        TaskProcessingDelay.Text = "5";
        TaskProcessingDelaySlider.Value = 5;
        TaskNetworkUsername.Text = "";
        TaskNetworkPassword.Password = "";
        TaskIsEnabled.IsChecked = true;
        MappingsGrid.ItemsSource = new ObservableCollection<MappingEditItem>();
        DatabasesCombo.ItemsSource = null;
        TaskTablesList.ItemsSource = null;
        _currentConnectionString = null;
        XmlViewBox.Text = "";
        XmlTreeView.Items.Clear();
        _taskXmlContent = null;
        InputPreview.Text = "";
        OutputPreview.Text = "";
        ErrorPreview.Text = "";
    }

    private void AddTaskBtn_Click(object sender, RoutedEventArgs e)
    {
        _selectedTask = null;
        TasksCombo.SelectedItem = null;
        ClearTaskFields();
    }

    private void DeleteTaskBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTask == null) return;
        if (System.Windows.MessageBox.Show("Delete this task?", "Confirm",
            MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;
        try
        {
            using var db = new AdmXmlDbContext(_dbPath);
            var toRemove = db.Tasks.Find(_selectedTask!.Id);
            if (toRemove != null)
                db.Tasks.Remove(toRemove);
            db.SaveChanges();
            LoadTasks();
            LoadSimulationTasks();
            AddTaskBtn_Click(sender, e);
        }
        catch (Exception ex)
        {
            ShowUserFriendlyError(ex);
        }
    }

    // ======================== DATABASE CONNECTION ========================

    private void AuthType_Changed(object sender, RoutedEventArgs e)
    {
        // Username/Password fields are bound to AuthSql.IsChecked via XAML
    }

    private void TaskDbConnectBtn_Click(object sender, RoutedEventArgs e)
    {
        var connStr = BuildConnectionStringFromUI("master");
        if (string.IsNullOrEmpty(connStr))
        {
            System.Windows.MessageBox.Show("Enter server name.", "Error");
            return;
        }

        var (ok, msg) = DbConnectionHelper.TestConnection(connStr);
        if (!ok)
        {
            System.Windows.MessageBox.Show(msg, "Connection Error");
            return;
        }

        _currentConnectionString = connStr;

        var databases = DbConnectionHelper.GetDatabases(connStr);
        DatabasesCombo.ItemsSource = databases;
        if (databases.Count > 0)
            DatabasesCombo.SelectedIndex = 0;
    }

    private void DatabasesCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var dbName = DatabasesCombo.SelectedItem as string;
        if (string.IsNullOrEmpty(dbName)) return;

        var connStr = BuildConnectionStringFromUI(dbName);
        if (string.IsNullOrEmpty(connStr)) return;

        _currentConnectionString = connStr;
        var tables = DbConnectionHelper.GetTables(connStr);
        TaskTablesList.ItemsSource = tables;
    }

    private void TaskTablesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var tableName = TaskTablesList.SelectedItem as string;
        if (string.IsNullOrEmpty(tableName)) return;

        if (string.IsNullOrEmpty(_currentConnectionString)) return;
        var cols = DbConnectionHelper.GetTableColumns(_currentConnectionString, tableName);
        TaskColumnsListLeft.ItemsSource = cols.Select(c => c.ColumnName).ToList();
    }

    private string BuildConnectionStringFromUI(string database)
    {
        var useWindowsAuth = AuthWindows.IsChecked == true;
        return DbConnectionHelper.BuildConnectionString(
            TaskDbHost.Text ?? "",
            database,
            useWindowsAuth ? null : TaskDbUser.Text,
            useWindowsAuth ? null : TaskDbPassword.Password);
    }

    private string GetCurrentConnectionString()
    {
        var dbName = DatabasesCombo.SelectedItem as string;
        return BuildConnectionStringFromUI(dbName ?? (_selectedTask?.TableName ?? "").Split('.').FirstOrDefault() ?? "master");
    }

    private void TaskUseConnectionString_Changed(object sender, RoutedEventArgs e)
    {
    }

    private void TaskGetColumnsBtn_Click(object sender, RoutedEventArgs e)
    {
        var connStr = GetCurrentConnectionString();
        var table = (_selectedTask?.TableName ?? "").Trim();
        if (string.IsNullOrEmpty(connStr) || string.IsNullOrEmpty(table))
        {
            System.Windows.MessageBox.Show("Enter connection details and table name first.", "Error");
            return;
        }
        var cols = DbConnectionHelper.GetTableColumns(connStr, table);
    }

    // ======================== FOLDER BROWSING ========================

    private void BrowseFolder(System.Windows.Controls.TextBox target)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Select folder",
            ShowNewFolderButton = true
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            target.Text = dlg.SelectedPath;
            UpdateFolderPreviews();
            if (target == TaskOutputPath)
                SyncStaticTargetDirFromOutput();
        }
    }

    private void SyncStaticTargetDirFromOutput()
    {
        if (TaskStaticTargetDir != null)
            TaskStaticTargetDir.Text = TaskOutputPath.Text ?? "";
    }

    private void BrowseInputPath_Click(object sender, RoutedEventArgs e) => BrowseFolder(TaskInputPath);
    private void BrowseOutputPath_Click(object sender, RoutedEventArgs e) => BrowseFolder(TaskOutputPath);
    private void BrowseErrorPath_Click(object sender, RoutedEventArgs e) => BrowseFolder(TaskErrorPath);

    private void PathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateFolderPreviews();
        if (sender == TaskOutputPath)
            SyncStaticTargetDirFromOutput();
    }

    private void UpdateFolderPreviews()
    {
        InputPreview.Text = TaskInputPath.Text;
        ErrorPreview.Text = TaskErrorPath.Text;
        UpdateFileNamePreview();
    }

    // ======================== PROCESSING DELAY ========================

    private void TaskProcessingDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TaskProcessingDelay != null)
            TaskProcessingDelay.Text = ((int)e.NewValue).ToString();
    }

    private void TaskProcessingDelay_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (int.TryParse(TaskProcessingDelay.Text, out var v) && v >= 1 && v <= 120
            && TaskProcessingDelaySlider != null)
            TaskProcessingDelaySlider.Value = v;
    }

    // ======================== FILE NAME PREVIEW ========================

    private void FileNamePreview_Changed(object sender, RoutedEventArgs e)
    {
        UpdateFileNamePreview();
    }

    private void UpdateFileNamePreview()
    {
        var basePath = TaskOutputPath.Text ?? "";
        var suffix = TaskFileNameSuffix.Text ?? "";
        var addDt = TaskAddDateTime.IsChecked == true;
        var prepend = TaskPrependToFileName.Text ?? "";

        var sampleName = "sample";
        if (!string.IsNullOrWhiteSpace(prepend))
            sampleName = prepend + sampleName;
        if (!string.IsNullOrWhiteSpace(suffix))
            sampleName += suffix;
        if (addDt)
            sampleName += DateTime.Now.ToString("yyyyMMddHHmmss");
        sampleName += ".xml";

        var preview = string.IsNullOrWhiteSpace(basePath)
            ? sampleName
            : Path.Combine(basePath, sampleName);
        OutputPreview.Text = preview;
    }

    // ======================== XML LOADING + TREE VIEW ========================

    private string PrettyPrintXml(string xmlContent)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xmlContent);
            using var sw = new StringWriter();
            using var xw = new XmlTextWriter(sw);
            xw.Formatting = Formatting.Indented;
            xw.Indentation = 2;
            doc.WriteTo(xw);
            xw.Flush();
            return sw.ToString();
        }
        catch
        {
            return xmlContent;
        }
    }

    private void LoadTaskXmlBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                _taskXmlContent = File.ReadAllText(dlg.FileName);
                XmlViewBox.Text = PrettyPrintXml(_taskXmlContent);
                PopulateXmlTreeView(_taskXmlContent);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading XML: {ex.Message}", "Error");
            }
        }
    }

    private void PopulateXmlTreeView(string xmlContent)
    {
        XmlTreeView.Items.Clear();
        _selectedXPath = null;
        
        if (string.IsNullOrWhiteSpace(xmlContent))
        {
            var warnItem = new TreeViewItem { Header = _currentLang == "tr" ? "XML içeriği boş. Geçerli bir XML dosyası seçin." : "XML content is empty. Please select a valid XML." };
            warnItem.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.OrangeRed);
            XmlTreeView.Items.Add(warnItem);
            return;
        }

        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xmlContent);

            if (doc.DocumentElement != null)
            {
                var rootItem = BuildTreeItem(doc.DocumentElement, "");
                XmlTreeView.Items.Add(rootItem);
                ExpandTreeItems(rootItem, 3);
            }
        }
        catch (Exception ex)
        {
            var errorMsg = _currentLang == "tr" ? $"XML okunurken hata oluştu:\n{ex.Message}" : $"Parse error:\n{ex.Message}";
            System.Windows.MessageBox.Show(errorMsg, _currentLang == "tr" ? "Hata" : "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            
            var errorItem = new TreeViewItem { Header = $"Parse error: {ex.Message}" };
            errorItem.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
            XmlTreeView.Items.Add(errorItem);
        }
    }

    private void ExpandTreeItems(TreeViewItem item, int depth)
    {
        if (depth <= 0) return;
        item.IsExpanded = true;
        foreach (var child in item.Items)
        {
            if (child is TreeViewItem childItem)
                ExpandTreeItems(childItem, depth - 1);
        }
    }

    private string? _selectedXPath;

    private void XmlTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem tvi && tvi.Tag is string xpath)
            _selectedXPath = xpath;
        else
            _selectedXPath = null;
    }

    private void AddToMappingBtn_Click(object sender, RoutedEventArgs e)
    {
        var columnName = TaskColumnsListLeft.SelectedItem as string;
        
        // Find all checked XPaths
        var checkedXPaths = new List<string>();
        FindCheckedNodes(XmlTreeView.Items, checkedXPaths);

        // Fallback to selected XPath if no checkboxes are checked
        if (checkedXPaths.Count == 0 && !string.IsNullOrEmpty(_selectedXPath))
        {
            checkedXPaths.Add(_selectedXPath);
        }

        if (string.IsNullOrEmpty(columnName) && checkedXPaths.Count == 0)
        {
            var warn = _currentLang == "tr"
                ? "Sol panelden bir kolon ve/veya Ağaç Görünümünden düğüm(ler) seçin."
                : "Select a Column from the left panel and/or XML node(s) from Tree View.";
            System.Windows.MessageBox.Show(warn, _currentLang == "tr" ? "Bilgi" : "Info");
            return;
        }

        var src = MappingsGrid.ItemsSource as ObservableCollection<MappingEditItem>;
        if (src == null)
            MappingsGrid.ItemsSource = src = new ObservableCollection<MappingEditItem>();

        var newItem = new MappingEditItem
        {
            ColumnName = columnName ?? "",
            SqlDataType = SqlDataType.VARCHAR,
            SortOrder = src.Count,
            TargetTableName = _selectedTask?.TableName ?? ""
        };

        if (checkedXPaths.Count == 1)
        {
            newItem.XPath = checkedXPaths[0];
        }
        else if (checkedXPaths.Count > 1)
        {
            newItem.XPath = checkedXPaths[0]; // Set first as XPath just in case
            newItem.ValueTemplate = string.Join("-", checkedXPaths.Select(xp => $"{{{xp}}}"));
        }

        src.Add(newItem);
        
        // Optionally uncheck after adding
        UncheckAllNodes(XmlTreeView.Items);
        
        // Try to preview the current value for the new mapping using the loaded XML (if any)
        string? previewValue = null;
        try
        {
            var xml = _taskXmlContent ?? _loadedXmlContent;
            if (!string.IsNullOrEmpty(xml))
            {
                if (!string.IsNullOrWhiteSpace(newItem.ValueTemplate))
                {
                    var doc = new XmlDocument();
                    doc.LoadXml(xml);
                    var nsManager = new XmlNamespaceManager(doc.NameTable);
                    var ctx = doc.DocumentElement ?? (XmlNode)doc;

                    previewValue = System.Text.RegularExpressions.Regex.Replace(newItem.ValueTemplate, @"\{(.+?)\}", match =>
                    {
                        var xp = match.Groups[1].Value;
                        try
                        {
                            var n = ctx.SelectSingleNode(xp, nsManager);
                            return n?.InnerText?.Trim() ?? "";
                        }
                        catch { return ""; }
                    });
                }
                else if (!string.IsNullOrWhiteSpace(newItem.XPath))
                {
                    previewValue = XmlParser.TestXPath(xml, newItem.XPath);
                }
            }
        }
        catch
        {
            previewValue = null;
        }
        
        var baseMsg = _currentLang == "tr"
            ? $"Eşleme eklendi:\n  Kolon: {columnName ?? "-"}\n  " + (checkedXPaths.Count > 1 ? $"Şablon: {newItem.ValueTemplate}" : $"XPath: {newItem.XPath ?? "-"}")
            : $"Mapping added:\n  Column: {columnName ?? "-"}\n  " + (checkedXPaths.Count > 1 ? $"Template: {newItem.ValueTemplate}" : $"XPath: {newItem.XPath ?? "-"}");

        if (previewValue != null)
        {
            baseMsg += _currentLang == "tr"
                ? $"\n  Örnek değer: {previewValue}"
                : $"\n  Sample value: {previewValue}";
        }
        
        System.Windows.MessageBox.Show(baseMsg, _currentLang == "tr" ? "Bilgi" : "Info",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void FindCheckedNodes(ItemCollection items, List<string> checkedXPaths)
    {
        foreach (var item in items)
        {
            if (item is TreeViewItem tvi)
            {
                if (tvi.Header is StackPanel panel)
                {
                    var checkBox = panel.Children.OfType<System.Windows.Controls.CheckBox>().FirstOrDefault();
                    if (checkBox != null && checkBox.IsChecked == true && checkBox.Tag is string xpath)
                    {
                        checkedXPaths.Add(xpath);
                    }
                }
                FindCheckedNodes(tvi.Items, checkedXPaths);
            }
        }
    }

    private void UncheckAllNodes(ItemCollection items)
    {
        foreach (var item in items)
        {
            if (item is TreeViewItem tvi)
            {
                if (tvi.Header is StackPanel panel)
                {
                    var checkBox = panel.Children.OfType<System.Windows.Controls.CheckBox>().FirstOrDefault();
                    if (checkBox != null)
                        checkBox.IsChecked = false;
                }
                UncheckAllNodes(tvi.Items);
            }
        }
    }

    private TreeViewItem BuildTreeItem(XmlNode node, string parentXPath)
    {
        var item = new TreeViewItem();

        if (node is XmlElement element)
        {
            var currentXPath = BuildXPathForElement(element, parentXPath);
            item.Tag = currentXPath;

            var headerPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };

            var checkBox = new System.Windows.Controls.CheckBox 
            { 
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = currentXPath
            };
            headerPanel.Children.Add(checkBox);

            var tagBlock = new System.Windows.Controls.TextBlock
            {
                Text = element.Name,
                FontWeight = FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0, 0, 180)),
                VerticalAlignment = VerticalAlignment.Center
            };
            headerPanel.Children.Add(tagBlock);

            if (element.Attributes != null && element.Attributes.Count > 0)
            {
                foreach (XmlAttribute attr in element.Attributes)
                {
                    if (attr.Name.StartsWith("xmlns")) continue;
                    var attrBlock = new System.Windows.Controls.TextBlock
                    {
                        Text = $"  {attr.Name}=",
                        Foreground = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(120, 0, 0)),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    headerPanel.Children.Add(attrBlock);
                    var valBlock = new System.Windows.Controls.TextBlock
                    {
                        Text = $"\"{attr.Value}\"",
                        Foreground = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0, 120, 0)),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    headerPanel.Children.Add(valBlock);
                }
            }

            bool isLeaf = !element.HasChildNodes
                || (element.ChildNodes.Count == 1 && element.FirstChild is XmlText);
            if (isLeaf)
            {
                var text = element.InnerText.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    var valueBlock = new System.Windows.Controls.TextBlock
                    {
                        Text = $": {text}",
                        Foreground = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(60, 60, 60)),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    headerPanel.Children.Add(valueBlock);
                }
            }

            item.Header = headerPanel;

            foreach (XmlNode child in element.ChildNodes)
            {
                if (child is XmlText) continue;
                item.Items.Add(BuildTreeItem(child, currentXPath));
            }
        }
        else if (node is XmlComment comment)
        {
            item.Header = $"<!-- {comment.Value} -->";
        }
        else if (node is XmlDeclaration decl)
        {
            item.Header = $"<?xml {decl.Value} ?>";
        }
        else
        {
            item.Header = node.OuterXml?.Trim();
        }

        return item;
    }

    private string BuildXPathForElement(XmlElement element, string parentXPath)
    {
        var name = element.LocalName;

        string nodeXPath = $"*[local-name()='{name}']";

        // Check common identifying attributes in priority order
        string[] identifyingAttrs = ["Name", "Sequence", "Id", "Code", "Type", "Index"];
        foreach (var attrName in identifyingAttrs)
        {
            var attrValue = element.GetAttribute(attrName);
            if (!string.IsNullOrEmpty(attrValue))
            {
                return $"{parentXPath}/{nodeXPath}[@{attrName}='{attrValue}']";
            }
        }

        // Root element (parentXPath is empty) never needs a content-based filter
        // because it is unique by definition.
        bool isRoot = string.IsNullOrEmpty(parentXPath);
        if (isRoot)
        {
            return $"/{nodeXPath}";
        }

        // Smart filtering: only use Label/Key/Name child text as filter
        // (safe because these are classification values, not variable data).
        bool hasFilter = false;
        string? valueChildName = null;

        foreach (XmlNode child in element.ChildNodes)
        {
            if (child is XmlElement childEl)
            {
                if ((!childEl.HasChildNodes || (childEl.ChildNodes.Count == 1 && childEl.FirstChild is XmlText)) &&
                    !string.IsNullOrWhiteSpace(childEl.InnerText))
                {
                    var childName = childEl.LocalName;
                    var childText = childEl.InnerText.Trim().Replace("'", "&apos;");

                    if (!hasFilter && !childText.Contains("&apos;") && 
                        (childName.Contains("Name", StringComparison.OrdinalIgnoreCase) || childName.Contains("Label", StringComparison.OrdinalIgnoreCase) || childName.Contains("Key", StringComparison.OrdinalIgnoreCase)))
                    {
                        nodeXPath += $"[*[local-name()='{childName}']='{childText}']";
                        hasFilter = true;
                    }
                    else if (hasFilter && (childName.Contains("Value", StringComparison.OrdinalIgnoreCase) || childName.Contains("Val", StringComparison.OrdinalIgnoreCase)))
                    {
                        valueChildName = childName;
                    }
                }
            }
        }

        if (hasFilter && !string.IsNullOrEmpty(valueChildName))
        {
            return $"{parentXPath}/{nodeXPath}/*[local-name()='{valueChildName}']";
        }

        // No Label/Key/Name based filter found: return WITHOUT any content-based
        // filter to avoid hardcoding variable data (like file paths, numbers, etc.)
        // into the XPath which would break for other XML files.
        return $"{parentXPath}/{nodeXPath}";
    }

    // ======================== XPATH TEST ========================

    private void TaskTestXPathBtn_Click(object sender, RoutedEventArgs e)
    {
        var xml = _taskXmlContent ?? _loadedXmlContent;
        if (string.IsNullOrEmpty(xml))
        {
            System.Windows.MessageBox.Show(
                _currentLang == "tr" ? "Önce bir XML dosyası yükleyin." : "Load an XML file first.", "Info");
            return;
        }
        var xpath = _selectedXPath;
        if (string.IsNullOrWhiteSpace(xpath))
        {
            System.Windows.MessageBox.Show(
                _currentLang == "tr" ? "Ağaçtan bir düğüm seçin." : "Select a node in the tree view first.", "Info");
            return;
        }
        var value = XmlParser.TestXPath(xml, xpath);
        var count = XmlParser.GetXPathNodeCount(xml, xpath);
        System.Windows.MessageBox.Show(
            $"XPath: {xpath}\n\n" +
            $"{(_currentLang == "tr" ? "İlk eşleşme" : "First match")}: {value ?? "(null)"}\n" +
            $"{(_currentLang == "tr" ? "Toplam düğüm" : "Total nodes")}: {count}",
            "XPath Test");
    }

    // ======================== TARGET DIR COMPONENTS ========================

    private void TargetDirAddXPath_Click(object sender, RoutedEventArgs e)
    {
        var val = TargetDirStaticInput.Text?.Trim();
        if (string.IsNullOrEmpty(val)) return;
        var src = TargetDirComponentsList.ItemsSource as ObservableCollection<TargetDirComponentItem>
            ?? new ObservableCollection<TargetDirComponentItem>();
        if (TargetDirComponentsList.ItemsSource == null) TargetDirComponentsList.ItemsSource = src;
        src.Add(new TargetDirComponentItem
        {
            ComponentType = TargetDirectoryComponentType.XPath,
            Value = val,
            SortOrder = src.Count
        });
        TargetDirStaticInput.Text = "";
    }

    private void TargetDirAddStatic_Click(object sender, RoutedEventArgs e)
    {
        var val = TargetDirStaticInput.Text?.Trim();
        if (string.IsNullOrEmpty(val)) return;
        var src = TargetDirComponentsList.ItemsSource as ObservableCollection<TargetDirComponentItem>
            ?? new ObservableCollection<TargetDirComponentItem>();
        if (TargetDirComponentsList.ItemsSource == null) TargetDirComponentsList.ItemsSource = src;
        src.Add(new TargetDirComponentItem
        {
            ComponentType = TargetDirectoryComponentType.Static,
            Value = val,
            SortOrder = src.Count
        });
        TargetDirStaticInput.Text = "";
    }

    private void TargetDirRemove_Click(object sender, RoutedEventArgs e)
    {
        var sel = (sender as FrameworkElement)?.Tag as TargetDirComponentItem;
        var src = TargetDirComponentsList.ItemsSource as ObservableCollection<TargetDirComponentItem>;
        if (src != null && sel != null) src.Remove(sel);
    }

    private void TargetDirMoveUp_Click(object sender, RoutedEventArgs e)
    {
        var sel = (sender as FrameworkElement)?.Tag as TargetDirComponentItem;
        var src = TargetDirComponentsList.ItemsSource as ObservableCollection<TargetDirComponentItem>;
        if (src == null || sel == null) return;
        var idx = src.IndexOf(sel);
        if (idx <= 0) return;
        src.RemoveAt(idx);
        src.Insert(idx - 1, sel);
        for (var i = 0; i < src.Count; i++) src[i].SortOrder = i;
    }

    private void TargetDirMoveDown_Click(object sender, RoutedEventArgs e)
    {
        var sel = (sender as FrameworkElement)?.Tag as TargetDirComponentItem;
        var src = TargetDirComponentsList.ItemsSource as ObservableCollection<TargetDirComponentItem>;
        if (src == null || sel == null) return;
        var idx = src.IndexOf(sel);
        if (idx < 0 || idx >= src.Count - 1) return;
        src.RemoveAt(idx);
        src.Insert(idx + 1, sel);
        for (var i = 0; i < src.Count; i++) src[i].SortOrder = i;
    }

    // ======================== MAPPING OPERATIONS ========================

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

    private void AddMappingBtn_Click(object sender, RoutedEventArgs e)
    {
        var src = MappingsGrid.ItemsSource as ObservableCollection<MappingEditItem>;
        if (src == null)
            MappingsGrid.ItemsSource = src = new ObservableCollection<MappingEditItem>();
        src.Add(new MappingEditItem { SqlDataType = SqlDataType.VARCHAR });
    }

    private void TestMappingRowBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.DataContext is MappingEditItem item)
        {
            var xml = _taskXmlContent ?? _loadedXmlContent;
            if (string.IsNullOrEmpty(xml))
            {
                System.Windows.MessageBox.Show(_currentLang == "tr" ? "Önce bir XML dosyası yükleyin." : "Load an XML file first.", _currentLang == "tr" ? "Bilgi" : "Info");
                return;
            }

            if (item.IsLiteral)
            {
                System.Windows.MessageBox.Show(_currentLang == "tr" ? $"Sabit (Literal) Değer: {item.DefaultValue}" : $"Literal Value: {item.DefaultValue}", _currentLang == "tr" ? "Satır Testi" : "Row Test");
                return;
            }

            if (string.IsNullOrWhiteSpace(item.XPath))
            {
                System.Windows.MessageBox.Show(_currentLang == "tr" ? "Test etmek için bir XML Düğümü (XPath) belirtin." : "Please specify an XML Node (XPath) to test.", _currentLang == "tr" ? "Hata" : "Error");
                return;
            }

            var value = XmlParser.TestXPath(xml, item.XPath);

            if (!string.IsNullOrEmpty(item.FindValue) && value != null)
            {
                try
                {
                    value = System.Text.RegularExpressions.Regex.Replace(value, item.FindValue, item.ReplaceValue ?? "");
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Regex hatası: {ex.Message}", "Hata");
                    return;
                }
            }

            if (!string.IsNullOrEmpty(item.ValueTemplate))
            {
                try
                {
                    var doc = new XmlDocument();
                    doc.LoadXml(xml);
                    var nsManager = new XmlNamespaceManager(doc.NameTable);
                    var ctx = doc.DocumentElement ?? (XmlNode)doc;

                    value = System.Text.RegularExpressions.Regex.Replace(item.ValueTemplate, @"\{(.+?)\}", match =>
                    {
                        var xp = match.Groups[1].Value;
                        if (xp == "value") return value ?? ""; // legacy {value} support
                        try
                        {
                            var n = ctx.SelectSingleNode(xp, nsManager);
                            return n?.InnerText?.Trim() ?? "";
                        }
                        catch { return ""; }
                    });
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Değer şablonu hatası:\n{ex.Message}", "Hata");
                    return;
                }
            }

            if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(item.DefaultValue))
            {
                value = item.DefaultValue;
            }

            System.Windows.MessageBox.Show($"{(_currentLang == "tr" ? "Sonuç:" : "Result:")}\n\n{value ?? "(null / boş)"}", _currentLang == "tr" ? "Satır Testi" : "Row Test");
        }
    }

    private void DeleteMappingRowBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.DataContext is MappingEditItem item)
        {
            if (MappingsGrid.ItemsSource is ObservableCollection<MappingEditItem> src)
            {
                src.Remove(item);
            }
        }
    }

    // ======================== SAVE TASK ========================

    private void SaveTaskBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(TaskName.Text))
            {
                System.Windows.MessageBox.Show(_currentLang == "tr" ? "Görev Adı zorunludur." : "Task name is required.", "Error");
                return;
            }
            if (string.IsNullOrWhiteSpace(TaskInputPath.Text) || string.IsNullOrWhiteSpace(TaskOutputPath.Text) || string.IsNullOrWhiteSpace(TaskErrorPath.Text))
            {
                System.Windows.MessageBox.Show(_currentLang == "tr" ? "Giriş, Çıkış ve Hata klasör yolları boş bırakılamaz." : "Input, Output, and Error folder paths cannot be empty.", "Error");
                return;
            }
            if (ContainsPathTraversal(TaskInputPath.Text) || ContainsPathTraversal(TaskOutputPath.Text) || ContainsPathTraversal(TaskErrorPath.Text))
            {
                System.Windows.MessageBox.Show(_currentLang == "tr" ? "Klasör yollarında geçersiz karakter dizisi (..) kullanılamaz." : "Folder paths must not contain path traversal sequences (..).", "Error");
                return;
            }
            var cron = (TaskCronExpression.Text ?? "").Trim();
            if (!string.IsNullOrEmpty(cron) && !Quartz.CronExpression.IsValidExpression(cron))
            {
                System.Windows.MessageBox.Show(_currentLang == "tr" ? "Geçersiz cron ifadesi." : "Invalid cron expression.", "Error");
                return;
            }

            using var db = new AdmXmlDbContext(_dbPath);
            using var transaction = db.Database.BeginTransaction();
            IntegrationTask task;
            if (_selectedTask != null)
            {
                task = db.Tasks
                    .Include(t => t.Mappings)
                    .Include(t => t.TargetDirectoryComponents)
                    .First(t => t.Id == _selectedTask.Id);
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
            task.PrependToFileName = string.IsNullOrWhiteSpace(TaskPrependToFileName.Text)
                ? null : TaskPrependToFileName.Text.Trim();
            task.FileNameSuffix = string.IsNullOrWhiteSpace(TaskFileNameSuffix.Text)
                ? null : TaskFileNameSuffix.Text.Trim();
            task.AddDateTimeToFileName = TaskAddDateTime.IsChecked == true;
            // Use _selectedXPath (tree node selection) if set, else keep existing value
            task.MultiRecordRootXPath = TaskMultiRecord.IsChecked == true
                ? (_selectedXPath ?? _selectedTask?.MultiRecordRootXPath)?.Trim()
                : null;
            task.TextToRemoveInXPath = string.IsNullOrWhiteSpace(_selectedTask?.TextToRemoveInXPath)
                ? null : _selectedTask?.TextToRemoveInXPath?.Trim();
            task.StaticTargetDirectory = string.IsNullOrWhiteSpace(TaskStaticTargetDir.Text)
                ? null : TaskStaticTargetDir.Text.Trim();

            var connStr = GetCurrentConnectionString();
            if (!string.IsNullOrEmpty(connStr))
                task.EncryptedConnectionString = DataProtectionHelper.Protect(connStr);

            task.TableName = TaskTablesList.SelectedItem as string ?? _selectedTask?.TableName ?? "";
            task.CronExpression = string.IsNullOrWhiteSpace(TaskCronExpression.Text)
                ? Constants.UI.DefaultCronExpression : TaskCronExpression.Text.Trim();
            task.ErrorEmails = TaskErrorEmails.Text ?? "";
            task.IsEnabled = TaskIsEnabled.IsChecked == true;
            task.ProcessingDelaySeconds = int.TryParse(TaskProcessingDelay.Text, out var delaySec) && delaySec > 0 ? delaySec : 5;
            task.NetworkUsername = string.IsNullOrWhiteSpace(TaskNetworkUsername.Text) ? null : TaskNetworkUsername.Text.Trim();
            task.EncryptedNetworkPassword = TaskNetworkPassword.SecurePassword.Length == 0
                ? null : DataProtectionHelper.Protect(TaskNetworkPassword.SecurePassword);

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
            transaction.Commit();
            _selectedTask = task;
            LoadTasks();
            LoadSimulationTasks();
            SetStatus(_currentLang == "tr" ? "✓ Görev kaydedildi." : "✓ Task saved.");
        }
        catch (Exception ex)
        {
            ShowUserFriendlyError(ex);
        }
    }

    // ======================== SAVE MAPPINGS ========================

    private void SaveMappingsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTask == null)
        {
            System.Windows.MessageBox.Show("Select a task first.", "Error");
            return;
        }
        try
        {
            var items = (MappingsGrid.ItemsSource as ObservableCollection<MappingEditItem>)?.ToList()
                ?? new List<MappingEditItem>();
            using var db = new AdmXmlDbContext(_dbPath);
            var existing = db.TaskMappings.Where(m => m.TaskId == _selectedTask.Id).ToList();
            db.TaskMappings.RemoveRange(existing);
            var invalidXPath = items.FirstOrDefault(i =>
                !i.IsLiteral &&
                !string.IsNullOrWhiteSpace(i.XPath) &&
                !IsValidXPath(i.XPath));
            if (invalidXPath != null)
            {
                System.Windows.MessageBox.Show(
                    (_currentLang == "tr" ? $"Geçersiz XPath ifadesi: {invalidXPath.XPath}" : $"Invalid XPath expression: {invalidXPath.XPath}"),
                    "Error");
                return;
            }

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
                    ValueTemplate = string.IsNullOrWhiteSpace(item.ValueTemplate) ? null : item.ValueTemplate.Trim(),
                    TargetTableName = string.IsNullOrWhiteSpace(item.TargetTableName) ? null : item.TargetTableName.Trim(),
                    IsRequired = item.IsRequired
                });
            }
            db.SaveChanges();
            SetStatus(_currentLang == "tr" ? "✓ Mapping'ler kaydedildi." : "✓ Mappings saved.");
        }
        catch (Exception ex)
        {
            ShowUserFriendlyError(ex);
        }
    }

    // ======================== CONFIG IMPORT/EXPORT ========================

    private void ConfigSaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTask == null)
        {
            System.Windows.MessageBox.Show("Select a task first.", "Error");
            return;
        }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            ConfigXmlSerializer.Export(_dbPath, _selectedTask.Id, dlg.FileName);
            SetStatus(_currentLang == "tr" ? "✓ Config dışa aktarıldı." : "✓ Config exported.");
        }
        catch (Exception ex)
        {
            ShowUserFriendlyError(ex);
        }
    }

    private void ConfigLoadBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var taskId = ConfigXmlSerializer.Import(_dbPath, dlg.FileName);
            // Set a stub so LoadTasks() picks up the id and re-selects after async load
            _selectedTask = new IntegrationTask { Id = taskId };
            LoadTasks();
            LoadSimulationTasks();
            SetStatus(_currentLang == "tr" ? "✓ Config içe aktarıldı." : "✓ Config imported.");
        }
        catch (Exception ex)
        {
            ShowUserFriendlyError(ex);
        }
    }

    // ======================== SIMULATION ========================

    private async void LoadSimulationTasks()
    {
        try
        {
            var dbPath = _dbPath;
            var tasks = await Task.Run(() =>
            {
                using var db = new AdmXmlDbContext(dbPath);
                return db.Tasks.AsNoTracking().Include(t => t.Mappings).ToList();
            });
            SimTaskCombo.ItemsSource = tasks;
        }
        catch { }
    }

    private void SimTaskCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_loadedXmlContent)
            && SimTaskCombo.SelectedItem is IntegrationTask t
            && t.Mappings.Any())
            ParseAndShow();
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

    public class ParsedRecordItem
    {
        public int RowIndex { get; set; }
        public string ColumnName { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
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
            
            var displayRows = new List<ParsedRecordItem>();
            for (var i = 0; i < allRows.Count; i++)
            {
                var row = allRows[i];
                foreach (var kvp in row)
                {
                var val = kvp.Value;
                string displayValue;

                if (val == null || val is DBNull)
                {
                    displayValue = "(null)";
                }
                else if (val is DateTime dt)
                {
                    displayValue = dt.ToString("yyyy-MM-dd HH:mm:ss");
                }
                else
                {
                    displayValue = val.ToString() ?? "(null)";
                }

                displayRows.Add(new ParsedRecordItem
                {
                    RowIndex = i + 1,
                    ColumnName = kvp.Key,
                    Value = displayValue
                });
                }
            }
            
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

    // ======================== MENU ========================

    private void MenuExit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        var vStr = ver != null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "v2.0.0";
        System.Windows.MessageBox.Show(
            $"AdmXmlDb Management {vStr}\n\nXML to Database Integration Tool\n\n© 2026 AdmXmlDb",
            "About", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ======================== LANGUAGE ========================

    // ======================== VALIDATION HELPERS ========================

    private static bool IsValidXPath(string xpath) => InputValidator.IsValidXPath(xpath);

    private static bool ContainsPathTraversal(string path) => InputValidator.ContainsPathTraversal(path);

    // ======================== REAL-TIME VALIDATION ========================

    private void TaskName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox tb) return;
        var isEmpty = string.IsNullOrWhiteSpace(tb.Text);
        tb.BorderBrush = isEmpty
            ? System.Windows.Media.Brushes.OrangeRed
            : System.Windows.Media.Brushes.Gray;
        tb.ToolTip = isEmpty
            ? (_currentLang == "tr" ? "Görev adı zorunludur." : "Task name is required.")
            : null;
    }

    private void TaskCronExpression_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox tb) return;
        var cron = tb.Text.Trim();
        if (string.IsNullOrEmpty(cron)) { tb.BorderBrush = System.Windows.Media.Brushes.Gray; tb.ToolTip = null; return; }
        var valid = Quartz.CronExpression.IsValidExpression(cron);
        tb.BorderBrush = valid
            ? System.Windows.Media.Brushes.Gray
            : System.Windows.Media.Brushes.OrangeRed;
        tb.ToolTip = valid ? null : (_currentLang == "tr" ? "Geçersiz cron ifadesi." : "Invalid cron expression.");
    }

    // ======================== ERROR HANDLING ========================

    private void ShowUserFriendlyError(Exception ex)
    {
        string message;
        if (ex is SqlException sqlEx)
        {
            message = _currentLang == "tr"
                ? $"Veritabanı hatası oluştu. (Kod: {sqlEx.Number})\nLütfen bağlantı ayarlarını kontrol edin."
                : $"A database error occurred. (Code: {sqlEx.Number})\nPlease check your connection settings.";
        }
        else if (ex is IOException)
        {
            message = _currentLang == "tr"
                ? "Dosya/klasör erişim hatası oluştu.\nKlasörün var olduğunu ve erişim izniniz olduğunu kontrol edin."
                : "A file or folder access error occurred.\nPlease verify the path exists and you have permission.";
        }
        else if (ex is UnauthorizedAccessException)
        {
            message = _currentLang == "tr"
                ? "Bu işlem için yetkiniz yok.\nUygulama izinlerini kontrol edin."
                : "You do not have permission to perform this action.\nPlease check application permissions.";
        }
        else
        {
            message = _currentLang == "tr"
                ? "Beklenmeyen bir hata oluştu. Lütfen tekrar deneyin."
                : "An unexpected error occurred. Please try again.";
        }
        System.Windows.MessageBox.Show(message, _currentLang == "tr" ? "Hata" : "Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private string _currentLang = Constants.UI.DefaultLanguage;

    private void MenuLang_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem mi || mi.Tag is not string lang) return;

        _currentLang = lang;
        MenuLangTr.IsChecked = lang == "tr";
        MenuLangEn.IsChecked = lang == "en";
        ApplyLanguage(lang);
    }

    private void ApplyLanguage(string lang)
    {
        var dict = Localization.GetDictionary(lang);

        Title = dict["title"];

        if (LangTrRadio != null) LangTrRadio.IsChecked = lang == "tr";
        if (LangEnRadio != null) LangEnRadio.IsChecked = lang == "en";

        ApplyToLogicalTree(this, dict);

        UpdateDataGridHeaders(MappingsGrid, new[]
        {
            "col_priority", "col_target_table", "col_column_name", "col_data_type", "col_xml_node",
            "col_search_term", "col_replace_with", "col_xml_default", "col_value_template", "col_literal"
        }, dict);

        UpdateDataGridHeaders(LogsGrid, new[]
        {
            "col_task", "col_filename", "col_status", "col_timestamp", "col_message"
        }, dict);

        AddToMappingBtn.ToolTip = dict["tip_add_mapping"];
        MaterialDesignThemes.Wpf.HintAssist.SetHint(TaskName, dict["hint_task_name"]);
    }

    private static void ApplyToLogicalTree(DependencyObject parent, Dictionary<string, string> dict)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is FrameworkElement fe && fe.Tag is string tag && tag.StartsWith("loc:"))
            {
                var key = tag[4..];
                if (dict.TryGetValue(key, out var val))
                {
                    if (fe is HeaderedItemsControl hic) hic.Header = val;
                    else if (fe is HeaderedContentControl hcc) hcc.Header = val;
                    else if (fe is TextBlock tb) tb.Text = val;
                    else if (fe is ContentControl cc) cc.Content = val;
                }
            }
            if (child is DependencyObject dChild)
                ApplyToLogicalTree(dChild, dict);
        }
    }

    private static void UpdateDataGridHeaders(DataGrid grid, string[] keys, Dictionary<string, string> dict)
    {
        for (var i = 0; i < keys.Length && i < grid.Columns.Count; i++)
        {
            if (dict.TryGetValue(keys[i], out var val))
                grid.Columns[i].Header = val;
        }
    }
}
