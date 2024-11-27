using System.Threading.Channels;
using FishyFlip;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Debug;
using Microsoft.Extensions.Options;
using Spectre.Console;
using FishyFlip.Lexicon.App.Bsky.Feed;
using FishyFlip.Models;

// We forcibly set the environment to Development because we are using the default builder which defaults to Production.
// This project .ignore apsettings.development.json so you can put your login information in there.
Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development"); 

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

CancellationTokenSource source = new();
CancellationToken token = source.Token;

var services = builder.Services;

services.AddOptions<BSkyInfo>()
    .Bind(builder.Configuration.GetSection("Bsky"))
    .Validate(x => !string.IsNullOrEmpty(x.Handle) && !string.IsNullOrEmpty(x.Password), "Handle and Password are required.");

services.AddScoped<FishyFlip.ATProtocol>(x => new ATProtocolBuilder().Build());


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

atWebProtocol.OnConnectionUpdated += (sender, args) =>
{
    AnsiConsole.WriteLine($"Connection Updated: {args.State}");
};


int counter = 0;
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
                            writer.TryWrite(args.Record);
                            counter++;
                            if (counter > 100)
                            {
                                AnsiConsole.WriteLine("Cancelling");
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

services.AddSingleton<List<ATWebSocketRecord>>(x => []);
services.AddSingleton<ChannelReader<ATWebSocketRecord>>(x => reader);
services.AddHostedService<FirehoseService>();

await atWebProtocol.ConnectAsync();

token.Register(async () =>
{
    writer.Complete();
    await atWebProtocol.CloseAsync();
    atWebProtocol.Dispose();
});

var app = builder.Build();

AnsiConsole.WriteLine("Hello");

await app.RunAsync();

public class BSkyInfo 
{
    public string Handle { get; set;} = string.Empty;

    public string Password { get; set;} = string.Empty;
}


public class FirehoseService(ChannelReader<ATWebSocketRecord> reader, List<ATWebSocketRecord> stream): BackgroundService, IDisposable
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var idx = 0;

        await foreach(var p in reader.ReadAllAsync())
        {
            idx++;
            stream.Add(p);
            Console.WriteLine(idx + " " + p.Commit?.Record?.Type);
            if (idx > 100)
            {
                stream.Clear();
                idx = 0;
                continue;
            }
        }
    }
}