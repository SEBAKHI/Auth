var builder = DistributedApplication.CreateBuilder(args);

// Auth API - Core authentication service
var authApi = builder.AddProject<Projects.Auth_API>("auth-api")
    .WithExternalHttpEndpoints();

// API Gateway - YARP reverse proxy
var apiGateway = builder.AddProject<Projects.API_Gateway>("api-gateway")
    .WithExternalHttpEndpoints()
    .WithReference(authApi);

// Auth UI - Frontend
builder.AddProject<Projects.Auth_UI>("auth-ui")
    .WithReference(apiGateway);

builder.Build().Run();
