using NATS.Client.Core;

var builder = WebApplication.CreateBuilder(args);
builder.AddNatsClient(connectionName: "nats");
builder.Services.AddHostedService<FirehoseService>();
var app = builder.Build();

app.MapGet("/", async (INatsConnection conn) =>
{
    await conn.PublishAsync(subject: "foo", data: "Hello, World!");
    return Results.Content("hello world");
}); 


app.Run();

public class FirehoseService(INatsConnection conn): BackgroundService, IDisposable
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach(var msg in conn.SubscribeAsync<string>(subject: "foo"))
        {
            Console.WriteLine(msg.Data);
        };
    }
}