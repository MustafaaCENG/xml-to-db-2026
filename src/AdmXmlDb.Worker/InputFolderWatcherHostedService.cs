using AdmXmlDb.Core;
using AdmXmlDb.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace AdmXmlDb.Worker;

/// <summary>
/// Watches input folders and triggers XmlIntegrationJob immediately when new .xml files are added.
/// </summary>
public class InputFolderWatcherHostedService : IHostedService, IDisposable
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly TaskSchedulerHostedService _taskScheduler;
    private readonly ILogger<InputFolderWatcherHostedService> _logger;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly List<NetworkShareConnector> _shareConnections = new();
    private FileSystemWatcher? _dbWatcher;
    private System.Timers.Timer? _pollTimer;
    private readonly Dictionary<string, System.Timers.Timer> _debounceTimers = new();
    private readonly object _lock = new();
    private DateTime _lastKnownDbWrite = DateTime.MinValue;

    public InputFolderWatcherHostedService(
        ISchedulerFactory schedulerFactory, 
        TaskSchedulerHostedService taskScheduler,
        ILogger<InputFolderWatcherHostedService> logger)
    {
        _schedulerFactory = schedulerFactory;
        _taskScheduler = taskScheduler;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        SetupDbWatcher();
        StartPollingTimer();
        await SetupWatchersAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _pollTimer?.Stop();
        return Task.CompletedTask;
    }

    private void SetupDbWatcher()
    {
        try
        {
            var dbPath = Constants.GetDefaultDbPath();
            var dir = Path.GetDirectoryName(dbPath);
            var baseName = Path.GetFileNameWithoutExtension(dbPath);

            if (Directory.Exists(dir))
            {
                // Watch admxmldb.db, admxmldb.db-wal, admxmldb.db-shm (SQLite WAL mode
                // writes to the -wal file first; the main .db may not update immediately)
                _dbWatcher = new FileSystemWatcher(dir, baseName + ".*")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
                };
                _dbWatcher.Changed += (_, _) => OnDbChanged();
                _dbWatcher.Created += (_, _) => OnDbChanged();
                _dbWatcher.EnableRaisingEvents = true;
                _logger.LogInformation("Database watcher enabled for hot-reload (watching {Pattern}).", baseName + ".*");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not setup database watcher.");
        }
    }

    /// <summary>
    /// Periodic safety net: checks every 30 seconds if any DB file in the folder has been
    /// modified since the last reload. Covers edge cases where FileSystemWatcher misses events.
    /// </summary>
    private void StartPollingTimer()
    {
        _pollTimer = new System.Timers.Timer(30_000) { AutoReset = true };
        _pollTimer.Elapsed += (_, _) =>
        {
            try
            {
                var dbPath = Constants.GetDefaultDbPath();
                var latestWrite = GetLatestDbWriteTime(dbPath);
                if (latestWrite > _lastKnownDbWrite)
                {
                    _lastKnownDbWrite = latestWrite;
                    OnDbChanged();
                }
            }
            catch { /* polling is best-effort */ }
        };
        _pollTimer.Start();

        // Capture initial timestamp
        try
        {
            _lastKnownDbWrite = GetLatestDbWriteTime(Constants.GetDefaultDbPath());
        }
        catch { }
    }

    private static DateTime GetLatestDbWriteTime(string dbPath)
    {
        var latest = DateTime.MinValue;
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = dbPath + suffix;
            if (File.Exists(path))
            {
                var wt = File.GetLastWriteTimeUtc(path);
                if (wt > latest) latest = wt;
            }
        }
        return latest;
    }

    private void OnDbChanged()
    {
        lock (_lock)
        {
            if (_debounceTimers.TryGetValue("db_reload", out var t)) { t.Stop(); t.Dispose(); }
            var timer = new System.Timers.Timer(1500) { AutoReset = false };
            timer.Elapsed += async (_, _) =>
            {
                try
                {
                    _logger.LogInformation("Database changed. Reloading tasks and folder watchers...");
                    await _taskScheduler.ReloadTasksAsync(CancellationToken.None);
                    await SetupWatchersAsync(CancellationToken.None);
                }
                catch (Exception ex) { _logger.LogError(ex, "Failed to hot-reload tasks."); }
            };
            _debounceTimers["db_reload"] = timer;
            timer.Start();
        }
    }

    private async Task SetupWatchersAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            foreach (var w in _watchers) w.Dispose();
            _watchers.Clear();
            foreach (var s in _shareConnections) s.Dispose();
            _shareConnections.Clear();
        }

        var dbPath = Constants.GetDefaultDbPath();
        await using var db = new AdmXmlDbContext(dbPath);
        var enabledTasks = await db.Tasks.Where(t => t.IsEnabled && !string.IsNullOrWhiteSpace(t.InputPath)).ToListAsync(ct);

        // Connect to UNC shares if credentials are stored
        foreach (var t in enabledTasks)
        {
            if (!string.IsNullOrWhiteSpace(t.NetworkUsername) && NetworkShareConnector.IsUncPath(t.InputPath))
            {
                try
                {
                    var pass = DataProtectionHelper.Unprotect(t.EncryptedNetworkPassword);
                    var conn = NetworkShareConnector.Connect(t.InputPath, t.NetworkUsername, pass);
                    if (conn != null) _shareConnections.Add(conn);
                    _logger.LogInformation("Task {TaskName}: Connected to UNC share {Share}", t.Name, NetworkShareConnector.GetShareRoot(t.InputPath)!);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Task {TaskName}: Could not connect to UNC share {Path}. FileSystemWatcher may fail.", t.Name, t.InputPath);
                }
            }
        }

        var pathInfo = new Dictionary<string, (List<int> TaskIds, int DelaySec)>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in enabledTasks)
        {
            var path = t.InputPath.Trim();
            if (!path.StartsWith(@"\\")) path = Path.GetFullPath(path);
            if (!Directory.Exists(path))
            {
                _logger.LogWarning("Task {TaskName}: Input path {Path} does not exist or is not accessible." +
                    (NetworkShareConnector.IsUncPath(path) ? " Configure Network Credentials for UNC shares." : ""),
                    t.Name, path);
                continue;
            }
            if (!pathInfo.TryGetValue(path, out var info))
            {
                info = (new List<int>(), t.ProcessingDelaySeconds > 0 ? t.ProcessingDelaySeconds : 5);
                pathInfo[path] = info;
            }
            info.TaskIds.Add(t.Id);
            // Use the shortest delay among tasks that share the same folder
            if (t.ProcessingDelaySeconds > 0 && t.ProcessingDelaySeconds < info.DelaySec)
                pathInfo[path] = (info.TaskIds, t.ProcessingDelaySeconds);
        }

        foreach (var (path, info) in pathInfo)
        {
            try
            {
                var (taskIds, delaySec) = info;
                var watcher = new FileSystemWatcher(path) { Filter = "*.xml", NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite, IncludeSubdirectories = true };
                watcher.Created += (_, e) => OnFileCreated(path, taskIds, e.FullPath, delaySec);
                watcher.Changed += (_, e) => OnFileCreated(path, taskIds, e.FullPath, delaySec);
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
                _logger.LogInformation("InputFolderWatcher: Watching {Path} for tasks {TaskIds} (delay={Delay}s)",
                    path, string.Join(",", taskIds), delaySec);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not watch {Path}", path); }
        }
    }

    private void OnFileCreated(string folderPath, List<int> taskIds, string fullPath, int delaySec)
    {
        lock (_lock)
        {
            if (_debounceTimers.TryGetValue(fullPath, out var t)) { t.Stop(); t.Dispose(); }
            var timer = new System.Timers.Timer(delaySec * 1000) { AutoReset = false };
            timer.Elapsed += (_, _) => DebounceElapsed(folderPath, taskIds, fullPath, timer);
            _debounceTimers[fullPath] = timer;
            timer.Start();
        }
    }

    private void DebounceElapsed(string folderPath, List<int> taskIds, string fullPath, System.Timers.Timer timer)
    {
        lock (_lock) { _debounceTimers.Remove(fullPath); timer.Dispose(); }
        _ = Task.Run(async () =>
        {
            try
            {
                if (!File.Exists(fullPath)) return;
                var scheduler = await _schedulerFactory.GetScheduler();
                foreach (var taskId in taskIds)
                {
                    var jobKey = new JobKey("task-" + taskId, "xml-integration");
                    if (await scheduler.CheckExists(jobKey))
                        await scheduler.TriggerJob(jobKey);
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Error triggering job for {File}", fullPath); }
        });
    }

    public void Dispose()
    {
        _pollTimer?.Stop();
        _pollTimer?.Dispose();
        _dbWatcher?.Dispose();

        lock (_lock)
        {
            foreach (var w in _watchers)
                w.Dispose();
            _watchers.Clear();

            foreach (var s in _shareConnections)
                s.Dispose();
            _shareConnections.Clear();

            foreach (var t in _debounceTimers.Values)
                t.Dispose();
            _debounceTimers.Clear();
        }
    }
}
