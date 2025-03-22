using System.Threading.Channels;
using FishyFlip;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Debug;
using Microsoft.Extensions.Options;
using Spectre.Console;
using FishyFlip.Lexicon.App.Bsky.Feed;
using FishyFlip.Models;
using FishyFlip.Lexicon.Com.Atproto.Repo;

// We forcibly set the environment to Development because we are using the default builder which defaults to Production.
// This project .ignore apsettings.development.json so you can put your login information in there.
HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    EnvironmentName = "Development"
});

CancellationTokenSource source = new();
CancellationToken token = source.Token;

var services = builder.Services;

services.AddOptions<BSkyInfo>()
    .Bind(builder.Configuration.GetSection("Bsky"))
    .Validate(x => !string.IsNullOrEmpty(x.Handle) && !string.IsNullOrEmpty(x.Password), "Handle and Password are required.");

services.AddSingleton<FishyFlip.ATProtocol>(x => new ATProtocolBuilder().Build());
services.AddSingleton<PingRecord>(x => new PingRecord());

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
                            if (counter > 1_000)
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

services.AddSingleton<ChannelReader<ATWebSocketRecord>>(x => reader);
services.AddHostedService<FirehoseService>();
await atWebProtocol.ConnectAsync(CancellationToken.None);

token.Register(async () =>
{
    writer.Complete();
    await atWebProtocol.CloseAsync();
    atWebProtocol.Dispose();
});

var app = builder.Build();

AnsiConsole.WriteLine("Hello " + app.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);

var at = app.Services.GetRequiredService<FishyFlip.ATProtocol>();
var bsky = app.Services.GetRequiredService<IOptions<BSkyInfo>>().Value;
var (session, _) = await at.AuthenticateWithPasswordResultAsync(bsky.Handle, bsky.Password);
if (session == null)
{
    AnsiConsole.WriteLine("Failed to authenticate. Exiting.");
    source.Cancel();
    return;
}

var post = $$"""
    Ping {{DateTime.UtcNow}}
""";

var res = await at.CreatePostAsync(post);

switch (res.Value)
{
    case CreateRecordOutput o:
        AnsiConsole.WriteLine("Created post: " + o.Cid);
        var ping = app.Services.GetRequiredService<PingRecord>();
        ping.Cid = o.Cid;
        ping.CreatedTime = DateTime.UtcNow;
        break;
    default:
        AnsiConsole.WriteLine("Failed to create post.");
        break;
}   

await app.RunAsync();

public class BSkyInfo 
{
    public string Handle { get; set;} = string.Empty;

    public string Password { get; set;} = string.Empty;
}


public class FirehoseService(ChannelReader<ATWebSocketRecord> reader, PingRecord ping, ATProtocol at): BackgroundService, IDisposable
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var idx = 0;

        await foreach(var p in reader.ReadAllAsync())
        {
            idx++;

            var cid = p.Commit?.Cid?.ToString();

            if (cid != null && cid == ping.Cid)
            {
                var found = DateTime.UtcNow;
                var delta = (found - ping.CreatedTime).TotalMilliseconds;
                AnsiConsole.WriteLine($"Ping for { cid } found at " + found);
                AnsiConsole.WriteLine("TTL ms: " + delta);

                var recordKey = p.Commit!.RKey;

                var rootUri = ATUri.Create($"{p.Did}/app.bsky.feed.post/{recordKey}");
                
                Console.WriteLine("This is the root Uri" + rootUri);

                var replyRef = new ReplyRefDef(new StrongRef(rootUri, cid: cid), new StrongRef(rootUri, cid: cid));

                var res = await at.CreatePostAsync($"Above post is found on the firehose after {delta} ms.", reply: replyRef);

                switch (res.Value)
                {
                    case CreateRecordOutput o:
                        AnsiConsole.WriteLine("Created reply " + o.Cid);
                        break;
                    default:
                        AnsiConsole.WriteLine("Failed to create reply.");
                        break;
                }
                break;
            }

            Console.WriteLine(idx + " " + p.Commit?.Record?.Type + " " +  cid);
        }
    }
}

public class PingRecord
{
    public string? Cid { get; set; } = string.Empty; 

    public DateTime CreatedTime { get; set; } = DateTime.UtcNow;
}