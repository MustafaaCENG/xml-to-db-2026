using AdmXmlDb.Core;
using AdmXmlDb.Worker;
using AdmXmlDb.Worker.Jobs;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quartz;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = Constants.ServiceName;
});

builder.Services.AddQuartz(q =>
{
    q.SchedulerId = "AdmXmlDb-Scheduler";
    q.SchedulerName = "AdmXmlDb XML Integration Scheduler";
});

builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

builder.Services.TryAddSingleton<TaskSchedulerHostedService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<TaskSchedulerHostedService>());
builder.Services.AddHostedService<InputFolderWatcherHostedService>();

var host = builder.Build();
host.Run();
