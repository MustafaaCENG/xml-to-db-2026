using AdmXmlDb.Core;
using AdmXmlDb.Core.Entities;
using AdmXmlDb.Worker.Jobs;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace AdmXmlDb.Worker;

public class TaskSchedulerHostedService : IHostedService
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly ILogger<TaskSchedulerHostedService> _logger;

    public TaskSchedulerHostedService(ISchedulerFactory schedulerFactory, ILogger<TaskSchedulerHostedService> logger)
    {
        _schedulerFactory = schedulerFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await ReloadTasksAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task ReloadTasksAsync(CancellationToken cancellationToken)
    {
        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);

        var dbPath = Constants.GetDefaultDbPath();
        await using var db = new AdmXmlDbContext(dbPath);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        SchemaMigrator.Migrate(db);

        // Delete all existing jobs in the integration group first to prevent ghost/zombie tasks
        var existingJobKeys = await scheduler.GetJobKeys(Quartz.Impl.Matchers.GroupMatcher<JobKey>.GroupEquals("xml-integration"), cancellationToken);
        if (existingJobKeys.Count > 0)
        {
            await scheduler.DeleteJobs(existingJobKeys, cancellationToken);
            _logger.LogInformation("Cleaned up {Count} previous job(s) from the scheduler.", existingJobKeys.Count);
        }

        var enabledTasks = await db.Tasks
            .Where(t => t.IsEnabled)
            .ToListAsync(cancellationToken);

        foreach (var task in enabledTasks)
        {
            try
            {
                var jobKey = new JobKey($"task-{task.Id}", "xml-integration");

                var job = JobBuilder.Create<XmlIntegrationJob>()
                    .WithIdentity(jobKey)
                    .UsingJobData(XmlIntegrationJob.TaskIdKey, task.Id)
                    .StoreDurably(true)
                    .Build();

                await scheduler.AddJob(job, true, cancellationToken);

                var hasValidCron = !string.IsNullOrWhiteSpace(task.CronExpression) &&
                                   Quartz.CronExpression.IsValidExpression(task.CronExpression);

                if (hasValidCron)
                {
                    var trigger = TriggerBuilder.Create()
                        .WithIdentity($"trigger-{task.Id}", "xml-integration")
                        .ForJob(job) // jobKey yerine direkt job verelim
                        .WithCronSchedule(task.CronExpression!)
                        .Build();

                    await scheduler.ScheduleJob(trigger, cancellationToken);
                }
                else
                {
                    _logger.LogWarning("Task {TaskName} (Id={Id}) has no valid cron expression. It will only run on startup and on folder watcher events.", task.Name, task.Id);
                }

                // Başlangıçta mevcut dosyaları işlemek için hemen tetikle
                await scheduler.TriggerJob(jobKey, cancellationToken);
                _logger.LogInformation(
                    "Task {TaskName} (Id={Id}) scheduled (HasCron={HasCron}) and triggered for startup",
                    task.Name, task.Id, hasValidCron);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to schedule task {TaskName} (Id={Id}). This task will be skipped, other tasks will continue.",
                    task.Name, task.Id);
            }
        }
    }
}
