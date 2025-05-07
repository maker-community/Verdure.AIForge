var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.Verdure_AIForge_ApiService>("apiservice");

builder.AddProject<Projects.Verdure_AIForge_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
