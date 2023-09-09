using System.Text.RegularExpressions;
using Dapr.Client;
using Microsoft.AspNetCore.Hosting;
using Dapr.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using AutoParkingControl.Contracts.Actors;
using Dapr.Actors.Client;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDaprClient();

var client = new DaprClientBuilder().Build();
builder.Configuration.AddDaprConfigurationStore("apc-secret-store", new List<string>() { "computervision" }, client, TimeSpan.FromSeconds(20));
builder.Configuration.AddStreamingDaprConfigurationStore("apc-secret-store", new List<string>() { "computervision" }, client, TimeSpan.FromSeconds(20));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

//Register HttpClient without any Dapr service invocation but with Dapr sourced configuration.
builder.Services.AddHttpClient("DirectComputerVisionHttpClient", (serviceProvider, httpClient) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var apiKey = configuration.GetSection("computervision")["apikey"];
    httpClient.BaseAddress = new Uri("https://westeurope.api.cognitive.microsoft.com");
    httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);
});

//Register HttpClient with Dapr InvocationHandler to use Dapr service invocation.
builder.Services.AddHttpClient("DaprHttpClient", httpClient =>
{
    //Set the base address to the name of the HTTPEndpoint (or appid).
    httpClient.BaseAddress = new Uri("http://computervision");
}).AddHttpMessageHandler(() => new InvocationHandler());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//Upload without any Dapr component.
app.MapPost("/uploaddirect", async (IFormFile photo, IHttpClientFactory httpClientFactory) =>
{
    var timestamp = DateTime.UtcNow;
    ByteArrayContent photoContent;
    using (var photoStream = new MemoryStream())
    {
        await photo.CopyToAsync(photoStream);
        photoContent = new ByteArrayContent(photoStream.ToArray());
    }

    var httpClient = httpClientFactory.CreateClient("DirectComputerVisionHttpClient");
    var url = "computervision/imageanalysis:analyze?features=read&api-version=2023-04-01-preview";
    photoContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
    var response = await httpClient.PostAsync(url, photoContent);
    var imageAnalysisResult = await response.Content.ReadFromJsonAsync<ImageAnalysisResult>();

    await ProcesExtractedTextAsync(timestamp, imageAnalysisResult?.ReadResult?.Content);
});

//Upload using HTTPEndpoint computervision.
app.MapPost("/upload", async (IFormFile photo, IHttpClientFactory httpClientFactory) =>
{
    var timestamp = DateTime.UtcNow;
    ByteArrayContent photoContent;
    using (var photoStream = new MemoryStream())
    {
        await photo.CopyToAsync(photoStream);
        photoContent = new ByteArrayContent(photoStream.ToArray());
    }

    var httpClient = httpClientFactory.CreateClient("DaprHttpClient");
    var url = "computervision/imageanalysis:analyze?features=read&api-version=2023-04-01-preview";
    photoContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
    var response = await httpClient.PostAsync(url, photoContent);
    var imageAnalysisResult = await response.Content.ReadFromJsonAsync<ImageAnalysisResult>();

    await ProcesExtractedTextAsync(timestamp, imageAnalysisResult?.ReadResult?.Content);
});

app.MapPost("/uploadoffline", async (
    string extractedText,
    ILogger<Program> logger) =>
{
    var timestamp = DateTime.UtcNow;
    await ProcesExtractedTextAsync(timestamp, extractedText);
});

app.Use(next => context =>
{
    return next(context);
});


app.Run();




async Task ProcesExtractedTextAsync(DateTime timestamp, string? extractedText)
{
    if (extractedText == null) return;
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


