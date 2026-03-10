using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Mail;
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

namespace AdmXmlDb.Management;

public partial class MainWindow : Window
{
    private readonly string _dbPath;
    private int _currentLogPage = 0;
    private const int LogPageSize = 50;
    private IntegrationTask? _selectedTask;
    private System.Windows.Threading.DispatcherTimer? _statusTimer;
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
        if (System.Windows.MessageBox.Show("90 günden eski tüm loglar silinecek. Devam edilsin mi?",
            "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;
        try
        {
            using var db = new AdmXmlDbContext(_dbPath);
            var cutoff = DateTime.UtcNow.AddDays(-90);
            var oldLogs = db.ExecutionLogs.Where(l => l.Timestamp < cutoff).ToList();
            db.ExecutionLogs.RemoveRange(oldLogs);
            var count = db.SaveChanges();
            System.Windows.MessageBox.Show($"{count} old log entries deleted.", "Done");
            _currentLogPage = 0;
            LoadLogs();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error");
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
        catch { }
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
                        Credentials = string.IsNullOrEmpty(smtp.Username)
                            ? null
                            : new NetworkCredential(smtp.Username, password)
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

    // ======================== TASK LIST ========================

    private void LoadTasks()
    {
        try
        {
            using var db = new AdmXmlDbContext(_dbPath);
            var tasks = db.Tasks.OrderBy(t => t.Name).ToList();
            TasksCombo.ItemsSource = tasks;
        }
        catch { }
    }

    private void TasksCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedTask = TasksCombo.SelectedItem as IntegrationTask;
        if (_selectedTask != null)
            PopulateTaskFields(_selectedTask);
        else
            ClearTaskFields();
    }

    private void PopulateTaskFields(IntegrationTask task)
    {
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
        TaskMultiRecord.IsChecked = !string.IsNullOrWhiteSpace(task.MultiRecordRootXPath);
        TaskMultiRecordXPath.Text = task.MultiRecordRootXPath ?? "";
        TaskTextToRemove.Text = task.TextToRemoveInXPath ?? "";
        TaskUseConnectionString.IsChecked = false;
        TaskConnectionString.Text = "";
        TaskConnectionString.Visibility = Visibility.Collapsed;
        TaskDbPassword.Password = "";

        var connStr = DataProtectionHelper.Unprotect(task.EncryptedConnectionString);
        if (!string.IsNullOrEmpty(connStr))
        {
            _currentConnectionString = connStr;
            var (host, dbName, user) = DbConnectionHelper.ParseConnectionString(connStr);
            TaskDbHost.Text = host ?? "";
            TaskDbUser.Text = user ?? "";
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
                    DatabasesCombo.SelectedIndex = idx;
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

        TaskTableName.Text = task.TableName;

        using var db = new AdmXmlDbContext(_dbPath);
        var taskWithNav = db.Tasks
            .Include(t => t.Mappings)
            .Include(t => t.TargetDirectoryComponents)
            .FirstOrDefault(t => t.Id == task.Id);
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
        else
        {
            MappingsGrid.ItemsSource = null;
            TargetDirComponentsList.ItemsSource = null;
        }

        UpdateFileNamePreview();
        UpdateFolderPreviews();
    }

    private void ClearTaskFields()
    {
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
        TaskConnectionString.Text = "";
        TaskUseConnectionString.IsChecked = false;
        TaskConnectionString.Visibility = Visibility.Collapsed;
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
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error");
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

        TaskTableName.Text = tableName;

        if (string.IsNullOrEmpty(_currentConnectionString)) return;
        var cols = DbConnectionHelper.GetTableColumns(_currentConnectionString, tableName);
        TaskColumnsListLeft.ItemsSource = cols.Select(c => c.ColumnName).ToList();
    }

    private string BuildConnectionStringFromUI(string database)
    {
        if (TaskUseConnectionString.IsChecked == true)
            return TaskConnectionString.Text ?? "";

        var useWindowsAuth = AuthWindows.IsChecked == true;
        return DbConnectionHelper.BuildConnectionString(
            TaskDbHost.Text ?? "",
            database,
            useWindowsAuth ? null : TaskDbUser.Text,
            useWindowsAuth ? null : TaskDbPassword.Password);
    }

    private string GetCurrentConnectionString()
    {
        if (TaskUseConnectionString.IsChecked == true)
            return TaskConnectionString.Text ?? "";

        var dbName = DatabasesCombo.SelectedItem as string;
        return BuildConnectionStringFromUI(dbName ?? TaskTableName.Text?.Split('.').FirstOrDefault() ?? "master");
    }

    private void TaskUseConnectionString_Changed(object sender, RoutedEventArgs e)
    {
        var useManual = TaskUseConnectionString.IsChecked == true;
        TaskConnectionString.Visibility = useManual ? Visibility.Visible : Visibility.Collapsed;
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
        }
    }

    private void BrowseInputPath_Click(object sender, RoutedEventArgs e) => BrowseFolder(TaskInputPath);
    private void BrowseOutputPath_Click(object sender, RoutedEventArgs e) => BrowseFolder(TaskOutputPath);
    private void BrowseErrorPath_Click(object sender, RoutedEventArgs e) => BrowseFolder(TaskErrorPath);

    private void UpdateFolderPreviews()
    {
        InputPreview.Text = TaskInputPath.Text;
        ErrorPreview.Text = TaskErrorPath.Text;
        UpdateFileNamePreview();
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
        var xpath = _selectedXPath;

        if (string.IsNullOrEmpty(columnName) && string.IsNullOrEmpty(xpath))
        {
            var warn = _currentLang == "tr"
                ? "Sol panelden bir kolon ve/veya Ağaç Görünümünden bir XML düğümü seçin."
                : "Select a Column from the left panel and/or an XML node from Tree View.";
            System.Windows.MessageBox.Show(warn, _currentLang == "tr" ? "Bilgi" : "Info");
            return;
        }

        var src = MappingsGrid.ItemsSource as ObservableCollection<MappingEditItem>;
        if (src == null)
            MappingsGrid.ItemsSource = src = new ObservableCollection<MappingEditItem>();

        src.Add(new MappingEditItem
        {
            ColumnName = columnName ?? "",
            XPath = xpath ?? "",
            SqlDataType = SqlDataType.VARCHAR,
            SortOrder = src.Count
        });

        var msg = _currentLang == "tr"
            ? $"Eşleme oluşturuldu:\n  Kolon: {columnName ?? "-"}\n  XPath: {xpath ?? "-"}"
            : $"Mapping created:\n  Column: {columnName ?? "-"}\n  XPath: {xpath ?? "-"}";
        System.Windows.MessageBox.Show(msg, _currentLang == "tr" ? "Bilgi" : "Info",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private TreeViewItem BuildTreeItem(XmlNode node, string parentXPath)
    {
        var item = new TreeViewItem();

        if (node is XmlElement element)
        {
            var currentXPath = BuildXPathForElement(element, parentXPath);
            item.Tag = currentXPath;

            var headerPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };

            var tagBlock = new System.Windows.Controls.TextBlock
            {
                Text = element.Name,
                FontWeight = FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0, 0, 180))
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
                            System.Windows.Media.Color.FromRgb(120, 0, 0))
                    };
                    headerPanel.Children.Add(attrBlock);
                    var valBlock = new System.Windows.Controls.TextBlock
                    {
                        Text = $"\"{attr.Value}\"",
                        Foreground = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0, 120, 0))
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
                            System.Windows.Media.Color.FromRgb(60, 60, 60))
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
        var nameAttr = element.GetAttribute("Name");

        string nodeXPath = $"*[local-name()='{name}']";

        if (!string.IsNullOrEmpty(nameAttr))
        {
            return $"{parentXPath}/{nodeXPath}[@Name='{nameAttr}']";
        }

        // Akıllı filtreleme (Güçlü bir attribute yoksa çocuk elementin değerine göre filtrele)
        bool hasFilter = false;
        string? valueChildName = null;

        foreach (XmlNode child in element.ChildNodes)
        {
            if (child is XmlElement childEl)
            {
                // Sadece metin içeren basit bir çocuksa (örn: <Label>DOKUMAN_TIPI</Label>)
                if ((!childEl.HasChildNodes || (childEl.ChildNodes.Count == 1 && childEl.FirstChild is XmlText)) &&
                    !string.IsNullOrWhiteSpace(childEl.InnerText))
                {
                    var childName = childEl.LocalName;
                    var childText = childEl.InnerText.Trim().Replace("'", "&apos;");
                    
                    // Eğer bu element muhtemelen bir "Key/Label" ise onu filtre olarak kullan
                    if (!hasFilter && !childText.Contains("&apos;") && 
                        (childName.Contains("Name", StringComparison.OrdinalIgnoreCase) || childName.Contains("Label", StringComparison.OrdinalIgnoreCase) || childName.Contains("Key", StringComparison.OrdinalIgnoreCase)))
                    {
                        nodeXPath += $"[*[local-name()='{childName}']='{childText}']";
                        hasFilter = true;
                    }
                    // Eğer bu element muhtemelen "Value" taşıyan çocuksa, adını aklımızda tut
                    else if (hasFilter && (childName.Contains("Value", StringComparison.OrdinalIgnoreCase) || childName.Contains("Val", StringComparison.OrdinalIgnoreCase)))
                    {
                        valueChildName = childName;
                    }
                }
            }
        }

        // Eğer ebeveyn düğüme (IndexValue) tıklandıysa ve içinde hem filtre (Label) hem değer (Value) varsa, doğrudan değere yönlendir
        if (hasFilter && !string.IsNullOrEmpty(valueChildName))
        {
            return $"{parentXPath}/{nodeXPath}/*[local-name()='{valueChildName}']";
        }

        // Sadece tek bir etiket bulduk ama Value bulamadıysak veya hiçbiri yoksa
        if (!hasFilter)
        {
           foreach (XmlNode child in element.ChildNodes)
           {
               if (child is XmlElement childEl && (!childEl.HasChildNodes || (childEl.ChildNodes.Count == 1 && childEl.FirstChild is XmlText)) && !string.IsNullOrWhiteSpace(childEl.InnerText))
               {
                   var childText = childEl.InnerText.Trim().Replace("'", "&apos;");
                   if (!childText.Contains("&apos;"))
                   {
                        nodeXPath += $"[*[local-name()='{childEl.LocalName}']='{childText}']";
                        break;
                   }
               }
           }
        }

        return $"{parentXPath}/{nodeXPath}";
    }

    // ======================== XPATH TEST ========================

    private void TaskTestXPathBtn_Click(object sender, RoutedEventArgs e)
    {
        var xml = _taskXmlContent ?? _loadedXmlContent;
        if (string.IsNullOrEmpty(xml))
        {
            System.Windows.MessageBox.Show("Load an XML file first.", "Info");
            return;
        }
        var xpath = TaskMultiRecordXPath.Text?.Trim();
        if (string.IsNullOrEmpty(xpath))
        {
            System.Windows.MessageBox.Show("Enter an XPath to test.", "Error");
            return;
        }
        var value = XmlParser.TestXPath(xml, xpath);
        var count = XmlParser.GetXPathNodeCount(xml, xpath);
        System.Windows.MessageBox.Show($"First match: {(value ?? "(null)")}\nTotal nodes: {count}", "XPath Test");
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

            if (!string.IsNullOrEmpty(item.ValueTemplate) && value != null)
            {
                value = item.ValueTemplate.Replace("{value}", value);
            }

            if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(item.DefaultValue))
            {
                value = item.DefaultValue;
            }

            System.Windows.MessageBox.Show($"{(_currentLang == "tr" ? "Sonuç:" : "Result:")}\n\n{value ?? "(null / boş)"}", _currentLang == "tr" ? "Satır Testi" : "Row Test");
        }
    }

    // ======================== SAVE TASK ========================

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
                System.Windows.MessageBox.Show("Invalid cron expression.", "Error");
                return;
            }

            using var db = new AdmXmlDbContext(_dbPath);
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
            task.MultiRecordRootXPath = TaskMultiRecord.IsChecked == true
                && !string.IsNullOrWhiteSpace(TaskMultiRecordXPath.Text)
                ? TaskMultiRecordXPath.Text.Trim() : null;
            task.TextToRemoveInXPath = string.IsNullOrWhiteSpace(TaskTextToRemove.Text)
                ? null : TaskTextToRemove.Text.Trim();
            task.StaticTargetDirectory = string.IsNullOrWhiteSpace(TaskStaticTargetDir.Text)
                ? null : TaskStaticTargetDir.Text.Trim();

            var connStr = GetCurrentConnectionString();
            if (!string.IsNullOrEmpty(connStr))
                task.EncryptedConnectionString = DataProtectionHelper.Protect(connStr);

            task.TableName = TaskTableName.Text ?? "";
            task.CronExpression = string.IsNullOrWhiteSpace(TaskCronExpression.Text)
                ? "0 0 * * * ?" : TaskCronExpression.Text.Trim();
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
            System.Windows.MessageBox.Show("Config exported.", "Saved");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error");
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
            LoadTasks();
            LoadSimulationTasks();
            _selectedTask = null;
            var tasks = TasksCombo.ItemsSource as IEnumerable<IntegrationTask>;
            _selectedTask = tasks?.FirstOrDefault(t => t.Id == taskId);
            if (_selectedTask != null) TasksCombo.SelectedItem = _selectedTask;
            System.Windows.MessageBox.Show("Config imported.", "Loaded");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error");
        }
    }

    // ======================== SIMULATION ========================

    private void LoadSimulationTasks()
    {
        try
        {
            using var db = new AdmXmlDbContext(_dbPath);
            var tasks = db.Tasks.Include(t => t.Mappings).ToList();
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

    // ======================== MENU ========================

    private void MenuExit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.MessageBox.Show(
            "XML Processor\nAdmXmlDb Management v1.1.0\n\nXML to Database Integration Tool",
            "About", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ======================== LANGUAGE ========================

    private string _currentLang = "tr";

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

        ApplyToLogicalTree(this, dict);

        UpdateDataGridHeaders(MappingsGrid, new[]
        {
            "col_priority", "col_column_name", "col_data_type", "col_xml_node",
            "col_search_term", "col_replace_with", "col_xml_default", "col_value_template", "col_literal"
        }, dict);

        UpdateDataGridHeaders(LogsGrid, new[]
        {
            "col_task", "col_filename", "col_status", "col_timestamp"
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
