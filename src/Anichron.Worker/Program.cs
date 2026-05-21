using Anichron.Worker.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.AddAppConfiguration();

builder.Services
    .AddWorkerCoreServices()
    .AddWorkerDataServices(builder.Configuration)
    .AddIngestionServices()
    .AddWorkerHostedServices();

await builder.Build().RunAsync();
