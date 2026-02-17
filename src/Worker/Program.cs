using Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.AddPlatformObservability();
builder.Services.AddPlatformInfrastructure(builder.Configuration);
builder.Services.AddHostedService<ElasticBootstrapService>();
builder.Services.AddHostedService<LogConsumerService>();
builder.Services.AddHostedService<AutoResolveService>();

await builder.Build().RunAsync();
