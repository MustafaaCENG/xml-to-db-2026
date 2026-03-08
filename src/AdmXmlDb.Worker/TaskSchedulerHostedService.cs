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

        var enabledTasks = await db.Tasks
            .Where(t => t.IsEnabled)
            .ToListAsync(cancellationToken);

        foreach (var task in enabledTasks)
        {
            var jobKey = new JobKey($"task-{task.Id}", "xml-integration");
            if (await scheduler.CheckExists(jobKey, cancellationToken))
                await scheduler.DeleteJob(jobKey, cancellationToken);

            if (!Quartz.CronExpression.IsValidExpression(task.CronExpression))
                continue;

            var job = JobBuilder.Create<XmlIntegrationJob>()
                .WithIdentity(jobKey)
                .UsingJobData(XmlIntegrationJob.TaskIdKey, task.Id)
                .Build();

            var trigger = TriggerBuilder.Create()
                .WithIdentity($"trigger-{task.Id}", "xml-integration")
                .WithCronSchedule(task.CronExpression)
                .Build();

            await scheduler.ScheduleJob(job, trigger, cancellationToken);

            // Başlangıçta mevcut dosyaları işlemek için hemen tetikle
            await scheduler.TriggerJob(jobKey, cancellationToken);
            _logger.LogInformation("Task {TaskName} (Id={Id}) scheduled and triggered for startup", task.Name, task.Id);
        }
    }
}
