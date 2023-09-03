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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapPost("/test", async (IFormFile photo, DaprClient daprClient) =>
{
    var timestamp = DateTime.UtcNow;
    var httpClient = new HttpClient(); //TODO: Cache http client
    ByteArrayContent photoContent;
    using (var photoStream = new MemoryStream())
    {
        await photo.CopyToAsync(photoStream);
        photoContent = new ByteArrayContent(photoStream.ToArray());
    }

    var ocrUrl = "https://westeurope.api.cognitive.microsoft.com/computervision/imageanalysis:analyze?features=read&api-version=2023-04-01-preview";
    var apiKey = (await daprClient.GetSecretAsync("apc-secret-store", "computervision"))["apikey"];
    httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);
    photoContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
    var response = await httpClient.PostAsync(ocrUrl, photoContent);
    var imageAnalysisResult = await response.Content.ReadFromJsonAsync<ImageAnalysisResult>();

    await ProcesExtractedTextAsync(timestamp, imageAnalysisResult?.ReadResult?.Content);
});

app.MapPost("/invokehttpendpoint", async (IFormFile photo, DaprClient daprClient) =>
{
    var timestamp = DateTime.UtcNow;

    var httpClient = DaprClient.CreateInvokeHttpClient();


    ByteArrayContent photoContent;
    using (var photoStream = new MemoryStream())
    {
        await photo.CopyToAsync(photoStream);
        photoContent = new ByteArrayContent(photoStream.ToArray());
    }

    var ocrUrl = "http://computervision/computervision/imageanalysis:analyze?features=read&api-version=2023-04-01-preview";
    photoContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
    var response = await httpClient.PostAsync(ocrUrl, photoContent);
    var imageAnalysisResult = await response.Content.ReadFromJsonAsync<ImageAnalysisResult>();

    await ProcesExtractedTextAsync(timestamp, imageAnalysisResult?.ReadResult?.Content);
});

app.MapPost("/invokefqdn", async (IFormFile photo, DaprClient daprClient) =>
{
    var timestamp = DateTime.UtcNow;

    var httpClient = new HttpClient();

    ByteArrayContent photoContent;
    using (var photoStream = new MemoryStream())
    {
        await photo.CopyToAsync(photoStream);
        photoContent = new ByteArrayContent(photoStream.ToArray());
    }

    var ocrUrl = $"http://localhost:{Environment.GetEnvironmentVariable("DAPR_HTTP_PORT")}/v1.0/invoke/https://westeurope.api.cognitive.microsoft.com/method/computervision/imageanalysis:analyze?features=read&api-version=2023-04-01-preview";
    photoContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
    var apiKey = (await daprClient.GetSecretAsync("apc-secret-store", "computervision"))["apikey"];
    httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);
    var response = await httpClient.PostAsync(ocrUrl, photoContent);
    var imageAnalysisResult = await response.Content.ReadFromJsonAsync<ImageAnalysisResult>();

    await ProcesExtractedTextAsync(timestamp, imageAnalysisResult?.ReadResult?.Content);
});

app.MapPost("/upload", async (
    IFormFile photo,
    ILogger<Program> logger) =>
{
    var timestamp = DateTime.UtcNow;
    await ProcesExtractedTextAsync(timestamp, "***\n2 - DDJ - 413\nB");
});


app.Run();


async Task ProcesExtractedTextAsync(DateTime timestamp, string? extractedText)
{
    if(extractedText == null) return;
    var europeseNummerplaatRegex = new Regex(@"(?<indexCijfer>\d)\s*-\s*(?<letters>[A-Z]+)\s*-\s*(?<cijfers>\d\d\d)"); //https://www.vlaanderen.be/de-europese-nummerplaat
    var matches = europeseNummerplaatRegex.Matches(extractedText).ToList();
    foreach (Match match in matches)
    {
        if (!match.Success) continue;
        var indexCijfer = match.Groups["indexCijfer"].Value;
        var letters = match.Groups["letters"].Value;
        var cijfers = match.Groups["cijfers"].Value;
        var licensePlate = $"{indexCijfer}-{letters}-{cijfers}";
        var parkingSessionActor = ActorProxy.Create<IParkingSessionActor>(new Dapr.Actors.ActorId(licensePlate), "ParkingSessionActor");
        await parkingSessionActor.RegisterVehicleDetectionAsync(new RegisterVehicleDetection(timestamp));
    }
}

public class ReadResult
{
    public string? Content { get; set; }
}

public class ImageAnalysisResult
{
    public ReadResult? ReadResult { get; set; }
}
