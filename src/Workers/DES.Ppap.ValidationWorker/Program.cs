using DES.Ppap.ValidationWorker;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddOptions<ValidationWorkerOptions>()
    .BindConfiguration("ValidationWorker")
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IComplexValidationExecutor, JsonPathComparisonExecutor>();
builder.Services.AddSingleton<ComplexValidationExecutorRegistry>();
builder.Services.AddHostedService<ComplexValidationWorker>();
await builder.Build().RunAsync().ConfigureAwait(false);
