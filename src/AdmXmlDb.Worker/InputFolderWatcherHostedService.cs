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
    private readonly ILogger<InputFolderWatcherHostedService> _logger;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Dictionary<string, System.Timers.Timer> _debounceTimers = new();
    private readonly object _lock = new();

    public InputFolderWatcherHostedService(ISchedulerFactory schedulerFactory, ILogger<InputFolderWatcherHostedService> logger)
    {
        _schedulerFactory = schedulerFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await SetupWatchersAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SetupWatchersAsync(CancellationToken ct)
    {
        var dbPath = Constants.GetDefaultDbPath();
        await using var db = new AdmXmlDbContext(dbPath);
        var enabledTasks = await db.Tasks.Where(t => t.IsEnabled && !string.IsNullOrWhiteSpace(t.InputPath)).ToListAsync(ct);
        var pathToTaskIds = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in enabledTasks)
        {
            var path = Path.GetFullPath(t.InputPath.Trim());
            if (!Directory.Exists(path)) continue;
            if (!pathToTaskIds.TryGetValue(path, out var list)) { list = new List<int>(); pathToTaskIds[path] = list; }
            list.Add(t.Id);
        }
        foreach (var (path, taskIds) in pathToTaskIds)
        {
            try
            {
                var watcher = new FileSystemWatcher(path) { Filter = "*.xml", NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
                watcher.Created += (_, e) => OnFileCreated(path, taskIds, e.FullPath);
                watcher.Changed += (_, e) => OnFileCreated(path, taskIds, e.FullPath);
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
                _logger.LogInformation("InputFolderWatcher: Watching {Path} for tasks {TaskIds}", path, string.Join(",", taskIds));
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not watch {Path}", path); }
        }
    }

    private void OnFileCreated(string folderPath, List<int> taskIds, string fullPath)
    {
        lock (_lock)
        {
            if (_debounceTimers.TryGetValue(fullPath, out var t)) { t.Stop(); t.Dispose(); }
            var timer = new System.Timers.Timer(2000) { AutoReset = false };
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
        foreach (var w in _watchers)
            w.Dispose();

        lock (_lock)
        {
            foreach (var t in _debounceTimers.Values)
                t.Dispose();
            _debounceTimers.Clear();
        }
    }
}
