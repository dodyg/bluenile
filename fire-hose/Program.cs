using System.Text;
using System.Threading.Channels;
using FishyFlip;
using FishyFlip.Lexicon.App.Bsky.Feed;
using FishyFlip.Models;
using Microsoft.Extensions.Logging.Debug;

// We forcibly set the environment to Development because we are using the default builder which defaults to Production.
// This project .ignore apsettings.development.json so you can put your login information in there.
Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development"); 

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

var debugLog = new DebugLoggerProvider();

// You can set a custom url with WithInstanceUrl
var jetstreamBuilder = new ATJetStreamBuilder()
    .WithLogger(debugLog.CreateLogger("BlueNileDebug"));

var atWebProtocol = jetstreamBuilder.Build();

var channel = Channel.CreateBounded<FishyFlip.Lexicon.App.Bsky.Feed.Post>(new BoundedChannelOptions(110)
{
    SingleWriter = true,
    FullMode = BoundedChannelFullMode.DropOldest,
});

var reader = channel.Reader;
var writer = channel.Writer;

atWebProtocol.OnConnectionUpdated += (sender, args) =>
{
    Console.WriteLine($"Connection Updated: {args.State}");
};

// OnRecordReceived returns ATObjectWebSocket records,
// Which contain ATObject records.
// If you wish to receive all records being returned,
// subscribe to OnRawMessageReceived.
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
                            writer.TryWrite(post);
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

services.AddSingleton<List<Post>>(x => []);
services.AddSingleton<ChannelReader<Post>>(x => reader);
services.AddHostedService<FirehoseService>();

await atWebProtocol.ConnectAsync();

var app = builder.Build();

app.Services.GetService<IHostApplicationLifetime>()!
.ApplicationStopping.Register(async () =>
{
    writer.Complete();
    await atWebProtocol.CloseAsync();
    atWebProtocol.Dispose();
});

app.MapGet("/", (List<Post> posts) => 
{
    var list = new StringBuilder();

    foreach(var p in posts)
        list.AppendLine($"<li>{p.Text}</li>");

    var html = $$"""
    <!doctype html>
    <html lang="en">
    <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Firehose</title>
        <link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css" rel="stylesheet" integrity="sha384-QWTKZyjpPEjISv5WaRU9OFeRpok6YctnYmDr5pNlyT2bRjXh0JMhjY6hW+ALEwIH" crossorigin="anonymous">
    </head>
    <body>
        <div class="container">
            <h1>Firehose (updating every 5s)</h1>
            <ul hx-get="/stream" hx-trigger="every 5s" hx-swap="afterBegin swap:0.5s">{{ list }}</ul>
        </div>
        <script src="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js" integrity="sha384-YvpcrYf0tY3lHB60NNkmXc5s9fDVZLESaAA55NDzOxhy9GkcIdslK1eN7N6jIeHz" crossorigin="anonymous"></script>
        <script src="https://unpkg.com/htmx.org@2.0.0" integrity="sha384-wS5l5IKJBvK6sPTKa2WZ1js3d947pvWXbPJ1OmWfEuxLgeHcEbjUUA5i9V5ZkpCw" crossorigin="anonymous"></script>
    </body>
    </html>
    """;

    return Results.Content(html, "text/html");
});

app.MapGet("/stream", (List<Post> posts) =>
{
    var str = new StringBuilder();

    foreach(var p in posts)
        str.AppendLine($"<li>{p.Text}</li>");

    return Results.Content(str.ToString(), "text/html");    
});

app.Run();

public class FirehoseService(ChannelReader<Post> reader, List<Post> stream): BackgroundService, IDisposable
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var idx = 0;

        await foreach(var p in reader.ReadAllAsync())
        {
            idx++;
            stream.Add(p);
            Console.WriteLine(idx);
            if (idx > 100)
            {
                stream.Clear();
                idx = 0;
                continue;
            }
        }
    }
}
