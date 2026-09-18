var builder = DistributedApplication.CreateBuilder(args);

var sqlServer = builder.AddSqlServer("sql")
    .AddDatabase("DefaultConnection");

// Dev-only fallback; production deployments set Jwt__Key explicitly.
var jwtKey = builder.Configuration["Jwt__Key"]
    ?? "DEVELOPMENT_ONLY_JwtSecret_ChangeMe_0123456789_ABCDEFGHIJKLMNOP";

builder.AddProject<Projects.NewVixSmart_Web>("webfrontend")
    .WithReference(sqlServer)
    .WithEnvironment("Jwt__Key", jwtKey)
    .WaitFor(sqlServer);

builder.Build().Run();
