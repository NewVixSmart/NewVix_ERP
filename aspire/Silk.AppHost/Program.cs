var builder = DistributedApplication.CreateBuilder(args);

var sqlServer = builder.AddSqlServer("sql")
    .AddDatabase("silktrading");

builder.AddProject<Projects.Silk_Trading_Web>("webfrontend")
    .WithReference(sqlServer)
    .WaitFor(sqlServer);

builder.Build().Run();
