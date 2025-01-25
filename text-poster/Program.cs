using FishyFlip;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;
using Htmx;
using Microsoft.AspNetCore.Mvc;
using FishyFlip.Models;
using FishyFlip.Lexicon.App.Bsky.Feed;
using FishyFlip.Lexicon.Com.Atproto.Repo;

// We forcibly set the environment to Development because we are using the default builder which defaults to Production.
// This project .ignore apsettings.development.json so you can put your login information in there.
Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development"); 

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

builder.Services.AddAntiforgery();

services.AddOptions<BSkyInfo>()
    .Bind(builder.Configuration.GetSection("Bsky"))
    .Validate(x => !string.IsNullOrEmpty(x.Handle) && !string.IsNullOrEmpty(x.Password), "Handle and Password are required.");

services.AddSingleton<FishyFlip.ATProtocol>(x => new ATProtocolBuilder().EnableAutoRenewSession(true).Build());
services.AddSingleton<SessionKeeper>();

var app = builder.Build();

app.UseAntiforgery();

app.MapGet("/", async (ATProtocol at, IOptions<BSkyInfo> bsky, SessionKeeper sessionKeeper, HttpContext context, IAntiforgery antiforgery) => 
{
    var (session, _) = await at.AuthenticateWithPasswordResultAsync(bsky.Value.Handle, bsky.Value.Password);
    if (session == null)
        return Results.Unauthorized();

    sessionKeeper.Session = session;

    var token = antiforgery.GetAndStoreTokens(context);

    var html = $$"""
    <!doctype html>
    <html lang="en">
    <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Text Poster</title>
        <link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css" rel="stylesheet" integrity="sha384-QWTKZyjpPEjISv5WaRU9OFeRpok6YctnYmDr5pNlyT2bRjXh0JMhjY6hW+ALEwIH" crossorigin="anonymous">
    </head>
    <body>
        <div class="container">
            <h1>Text Poster</h1>
            <table class="table mb-3">
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

            <h2>Skeet</h2>
            <form hx-post="/new-post" hx-swap="outerHTML" class="col-md-6">
                <input type="hidden" name="{{ token.FormFieldName }}" value="{{ token.RequestToken }}" />
                <div class="mb-3" id="postInput">
                    <label for="post" class="form-label">Post</label>
                    <textarea name="Post" id="post" rows="5" class="form-control" maxlength="300"></textarea>
                </div>
                <div class="mb-3">
                    <button class="btn btn-primary">Skeet</button>
                </div>
            </form>
        </div>
        <script src="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js" integrity="sha384-YvpcrYf0tY3lHB60NNkmXc5s9fDVZLESaAA55NDzOxhy9GkcIdslK1eN7N6jIeHz" crossorigin="anonymous"></script>
        <script src="https://unpkg.com/htmx.org@2.0.0" integrity="sha384-wS5l5IKJBvK6sPTKa2WZ1js3d947pvWXbPJ1OmWfEuxLgeHcEbjUUA5i9V5ZkpCw" crossorigin="anonymous"></script>
    </body>
    </html>
    """;

    return Results.Content(html, "text/html");
});

app.MapPost("/new-post", async (HttpRequest request, HttpResponse response, SessionKeeper session, ATProtocol at, [FromForm] Input i) =>
{
    if (request.IsHtmx() is false)
        return Results.Content("");

    if (session.Session == null)
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(i.Post))
    {
        response.Htmx(x => x.Retarget("#postInput"));

        return Results.Content("""
            <div class="mb-3" id="postInput">
                <label for="post" class="form-label">Post</label>
                <textarea name="Post" id="post" rows="5" class="form-control is-invalid" maxlength="300"></textarea>
                <div class="invalid-feedback">Post is required.</div>
            </div>
        """, "text/html");
    }


    var res = await at.CreatePostAsync(i.Post);

    IResult Success(CreateRecordOutput res)
    {
        var html = $$"""
        <div class="alert alert-success mb-3">
            Your post has been submitted. You can view the url of the post <a href="{{ res.Uri }}">here</a>.
        </div>
        """;

        return Results.Content(html, "text/html");
    }

    IResult Fail(ATError err)
    {
        var html = $$"""
        <div class="alert alert-success mb-3">
            Your post has been submitted but it comes with the following error {{ err.StatusCode }} {{ err.Detail }}
        </div>
        """;

        return Results.Content(html, "text/html");
    }

    return res.Value switch
    {
        CreateRecordOutput r => Success(r),
        ATError e => Fail(e),
        _ => throw new ArgumentOutOfRangeException()
    };
});

app.Run();


class Input 
{
    public string Post { get; set; } = string.Empty;
} 

public class BSkyInfo 
{
    public string Handle { get; set;} = string.Empty;

    public string Password { get; set;} = string.Empty;
}

public class SessionKeeper
{
    public FishyFlip.Models.Session? Session { get; set; }
}