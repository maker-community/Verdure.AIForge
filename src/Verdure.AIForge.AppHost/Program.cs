var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.Verdure_AIForge_ApiService>("apiservice");

builder.AddProject<Projects.Verdure_AIForge_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);

builder.AddNpmApp("BotSharpUI", "../../../BotSharp-UI")
    .WithReference(apiService)
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .PublishAsDockerFile();

builder.Build().Run();
