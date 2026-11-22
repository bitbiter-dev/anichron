using Anichron.Infrastructure.Configuration;
using Anichron.Worker.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.AddAppConfiguration(WorkerIniDefaults.Entries);

builder.Services
    .AddWorkerCoreServices()
    .AddWorkerDataServices(builder.Configuration)
    .AddIngestionServices()
    .AddWorkQueueServices()
    .AddWorkerHostedServices();

await builder.Build().RunAsync();
