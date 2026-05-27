using BackupZCrypt.Composition;
using BackupZCrypt.Worker;
using BackupZCrypt.Worker.Extensions;
using BackupZCrypt.Worker.Services;
using BackupZCrypt.Worker.Services.Interfaces;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDomainServices();
builder.Services.AddApplicationServices();
builder.Services.AddWorkerConfiguration(builder.Configuration);
builder.Services.AddSingleton<IWorkerFileSystem, WorkerFileSystem>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.RunAsync();
