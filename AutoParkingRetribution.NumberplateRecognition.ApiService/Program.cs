using System.Text.RegularExpressions;
using Dapr.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDaprClient();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapPost("/upload", async (
    IFormFile photo, 
    DaprClient daprClient,
    ILogger<Program> logger) =>
{
    var plateRecognisedEvents = new List<PlateRecognised>();
    var timestamp = DateTime.UtcNow;
    var zoneId = 1;

    //TODO: photo -> OCR -> text
    var recognisedText = "***\n2 - DDJ - 413\nB";

    var europeseNummerplaatRegex = new Regex(@"(?<indexCijfer>\d)\s*-\s*(?<letters>[A-Z]+)\s*-\s*(?<cijfers>\d\d\d)"); //https://www.vlaanderen.be/de-europese-nummerplaat
    var matches = europeseNummerplaatRegex.Matches(recognisedText).ToList();
    if (!matches.Any()) return;
    foreach(Match match in matches){
        if (!match.Success)continue;
        var indexCijfer = match.Groups["indexCijfer"].Value;
        var letters = match.Groups["letters"].Value;
        var cijfers = match.Groups["cijfers"].Value;
        plateRecognisedEvents.Add(new PlateRecognised(zoneId, $"{indexCijfer}-{letters}-{cijfers}", timestamp));
    }

    await daprClient.BulkPublishEventAsync("pubsub", "platerecognised", plateRecognisedEvents);
});

app.Run();

public record struct PlateRecognised(int zoneId, string plate, DateTime timestamp);