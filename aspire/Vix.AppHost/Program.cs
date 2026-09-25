var builder = DistributedApplication.CreateBuilder(args);

var sqlServer = builder.AddSqlServer("sql")
    .AddDatabase("DefaultConnection");

// Dev-only fallback; production deployments set Jwt__Key explicitly.
// The web production guard rejects any Jwt__Key containing "REPLACE_WITH",
// so this placeholder can never seed a live environment.
var jwtKey = builder.Configuration["Jwt__Key"]
    ?? "REPLACE_WITH_AspireDevJwtSecret_0123456789_ABCDEFGHIJKLMNOP";

builder.AddProject("webfrontend", "../../src/NewVixSmart.Web/NewVixSmart.Web.csproj")
    .WithReference(sqlServer)
    .WithEnvironment("Jwt__Key", jwtKey)
    .WaitFor(sqlServer);

builder.Build().Run();
