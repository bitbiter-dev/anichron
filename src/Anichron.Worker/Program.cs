using Anichron.Worker.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.AddAppConfiguration();

builder.Services
    .AddWorkerCoreServices()
    .AddWorkerDataServices(builder.Configuration)
    .AddIngestionServices()
    .AddWorkQueueServices()
    .AddWorkerHostedServices();

await builder.Build().RunAsync();
