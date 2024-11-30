using System.Threading.Channels;
using FishyFlip;
using FishyFlip.Lexicon.App.Bsky.Feed;
using FishyFlip.Models;
using Microsoft.Extensions.Logging.Debug;
using NATS.Client.Core;

Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development"); 

CancellationTokenSource source = new();
CancellationToken token = source.Token;

var builder = WebApplication.CreateBuilder(args);
builder.AddNatsClient(connectionName: "nats");

var services = builder.Services;
services.AddHostedService<FirehoseService>();
services.AddHostedService<FirehoseSubscriber>();

var debugLog = new DebugLoggerProvider();

// You can set a custom url with WithInstanceUrl
var jetstreamBuilder = new ATJetStreamBuilder()
    .WithLogger(debugLog.CreateLogger("BlueNileDebug"));

var atWebProtocol = jetstreamBuilder.Build();

var channel = Channel.CreateBounded<ATWebSocketRecord>(new BoundedChannelOptions(110)
{
    SingleWriter = true,
    FullMode = BoundedChannelFullMode.DropOldest,
});

var reader = channel.Reader;
var writer = channel.Writer;

services.AddSingleton<ChannelReader<ATWebSocketRecord>>(x => reader);

atWebProtocol.OnConnectionUpdated += (sender, args) =>
{
    Console.WriteLine($"Connection Updated: {args.State}");
};

var idx = 0;
atWebProtocol.OnRecordReceived += (sender, args) =>
{
    switch (args.Record.Kind)
    {
        case ATWebSocketEvent.Commit:
            switch (args.Record.Commit?.Operation)
            {
                // Create is when a new record is created.
                case ATWebSocketCommitType.Create:

                    // Record is an ATWebSocketRecord, which contains the actual record inside Commit.
                    switch (args.Record.Commit?.Record)
                    {
                        case FishyFlip.Lexicon.App.Bsky.Feed.Post post:
                            writer.TryWrite(args.Record);
                            idx++;
                            if (idx > 400){
                                source.Cancel();
                            }
                            break;
                        default:
                            break;
                    }
                    break;
            }
            break;
        default:
            break;
    }
};

token.Register(async () =>
{
    writer.Complete();
    await atWebProtocol.CloseAsync();
});

var app = builder.Build();

app.Services.GetService<IHostApplicationLifetime>()!
.ApplicationStopping.Register(async () =>
{
    writer.TryComplete();
    await atWebProtocol.CloseAsync();
    atWebProtocol.Dispose();
});

await atWebProtocol.ConnectAsync();
app.Run();

public class FirehoseSubscriber(INatsConnection conn): BackgroundService, IDisposable
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach(var msg in conn.SubscribeAsync<string>(subject: "mainline"))
        {
            Console.WriteLine("Subscribed Data: " + msg.Data);
        };
    }
}

public class FirehoseService(ChannelReader<ATWebSocketRecord> reader, INatsConnection conn): BackgroundService, IDisposable
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach(var m in reader.ReadAllAsync())
        {
            if (m.Commit?.Record is Post p)
            {
                var tags = p.Tags ?? [];
                if (tags.Count == 0)
                {
                    await conn.PublishAsync<string>(subject: "mainline", data: p.Text ?? string.Empty, cancellationToken: stoppingToken);
                }
                foreach(var t in tags)
                {
                    await conn.PublishAsync<string>(subject: t, data: p.Text ?? string.Empty, cancellationToken: stoppingToken);
                }
            }
        }
    }
}