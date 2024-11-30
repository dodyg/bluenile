var builder = DistributedApplication.CreateBuilder(args);

var nats = builder.AddNats("nats");

builder.AddProject<Projects.Firehose_Web>("Firehose-Web")
       .WithReference(nats);

builder.Build().Run();
