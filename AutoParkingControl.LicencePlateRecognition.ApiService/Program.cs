using System.Text.RegularExpressions;
using Dapr.Client;
using AutoParkingControl.Contracts.Actors;
using Dapr.Actors.Client;
using System.Net.Http.Headers;

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
    ILogger<Program> logger) =>
{
    var timestamp = DateTime.UtcNow;

    var computerVisionClient = DaprClient.CreateInvokeHttpClient(appId: "computervision");
    using (var photoStream = new MemoryStream())
    {
        await photo.CopyToAsync(photoStream);
        using (var photoStreamContent = new StreamContent(photoStream))
        {
            var ocrUrl = "computervision/imageanalysis:analyze?features=denseCaptions,read&api-version=2023-04-01-preview";
            photoStreamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            var response = await computerVisionClient.PostAsync(ocrUrl, photoStreamContent);
            var responseString = await response.Content.ReadAsStringAsync();
        }
    }

    //TODO: photo -> OCR -> text
    var recognisedText = "***\n2 - DDJ - 413\nB";

    var europeseNummerplaatRegex = new Regex(@"(?<indexCijfer>\d)\s*-\s*(?<letters>[A-Z]+)\s*-\s*(?<cijfers>\d\d\d)"); //https://www.vlaanderen.be/de-europese-nummerplaat
    var matches = europeseNummerplaatRegex.Matches(recognisedText).ToList();
    if (!matches.Any()) return;
    foreach (Match match in matches)
    {
        if (!match.Success) continue;
        var indexCijfer = match.Groups["indexCijfer"].Value;
        var letters = match.Groups["letters"].Value;
        var cijfers = match.Groups["cijfers"].Value;
        var licencePlace = $"{indexCijfer}-{letters}-{cijfers}";

        var parkingSessionActor = ActorProxy.Create<IParkingSessionActor>(new Dapr.Actors.ActorId(licencePlace), "ParkingSessionActor");
        await parkingSessionActor.RegisterVehicleDetectionAsync(new RegisterVehicleDetection(timestamp));
    }
});

app.Run();
