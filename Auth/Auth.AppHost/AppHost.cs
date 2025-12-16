var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Auth_UI>("auth-ui");

builder.AddProject<Projects.UserManagement_API>("usermanagement-api");

builder.AddProject<Projects.RolePermission_API>("rolepermission-api");

builder.AddProject<Projects.AuditLog_API>("auditlog-api");

builder.Build().Run();
