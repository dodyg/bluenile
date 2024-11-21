using FishyFlip;
using Microsoft.Extensions.Options;

// We forcibly set the environment to Development because we are using the default builder which defaults to Production.
// This project .ignore apsettings.development.json so you can put your login information in there.
Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development"); 

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

services.AddOptions<BSkyInfo>()
    .Bind(builder.Configuration.GetSection("Bsky"))
    .Validate(x => !string.IsNullOrEmpty(x.Handle) && !string.IsNullOrEmpty(x.Password), "Handle and Password are required.");

services.AddScoped<FishyFlip.ATProtocol>(x => new ATProtocolBuilder().Build());

var app = builder.Build();

app.MapGet("/", async (ATProtocol at, IOptions<BSkyInfo> bsky) => 
{
    var session = await at.AuthenticateWithPasswordAsync(bsky.Value.Handle, bsky.Value.Password);
    if (session == null)
        return Results.Unauthorized();

    var html = $$"""
    <!doctype html>
    <html lang="en">
    <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Hello World</title>
        <link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css" rel="stylesheet" integrity="sha384-QWTKZyjpPEjISv5WaRU9OFeRpok6YctnYmDr5pNlyT2bRjXh0JMhjY6hW+ALEwIH" crossorigin="anonymous">
    </head>
    <body>
        <div class="container">
            <h1>Hello, world!</h1>
            <table class="table">
                <thead>
                    <tr>
                        <th>Key</th>
                        <th>Value</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td>DID</td>
                        <td>{{ session.Did }}</td>
                    </tr>
                    <tr>
                        <td>Email</td>
                        <td>{{ session.Email }}</td>
                    </tr>
                </tbody>
            </table>
        </div>
        <script src="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js" integrity="sha384-YvpcrYf0tY3lHB60NNkmXc5s9fDVZLESaAA55NDzOxhy9GkcIdslK1eN7N6jIeHz" crossorigin="anonymous"></script>
    </body>
    </html>
    """;

    return Results.Content(html, "text/html");
});

app.Run();


public class BSkyInfo 
{
    public string Handle { get; set;} = string.Empty;

    public string Password { get; set;} = string.Empty;
}