using FishyFlip;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Spectre.Console;


// We forcibly set the environment to Development because we are using the default builder which defaults to Production.
// This project .ignore apsettings.development.json so you can put your login information in there.
Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development"); 

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

var services = builder.Services;

services.AddOptions<BSkyInfo>()
    .Bind(builder.Configuration.GetSection("Bsky"))
    .Validate(x => !string.IsNullOrEmpty(x.Handle) && !string.IsNullOrEmpty(x.Password), "Handle and Password are required.");

services.AddScoped<FishyFlip.ATProtocol>(x => new ATProtocolBuilder().Build());

var app = builder.Build();

AnsiConsole.WriteLine("Hello");
await app.RunAsync();

public class BSkyInfo 
{
    public string Handle { get; set;} = string.Empty;

    public string Password { get; set;} = string.Empty;
}