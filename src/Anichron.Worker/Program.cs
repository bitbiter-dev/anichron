using Anichron.Worker.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.AddAppConfiguration();
builder.AddWorkerCoreServices();
builder.AddWorkerDataServices();
builder.AddIngestionServices();
builder.AddWorkerHostedServices();

await builder.Build().RunAsync();
