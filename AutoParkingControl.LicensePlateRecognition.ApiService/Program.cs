using System.Text.RegularExpressions;
using Dapr.Client;
using Microsoft.AspNetCore.Hosting;
using Dapr.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using AutoParkingControl.Contracts.Actors;
using Dapr.Actors.Client;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDaprClient();

//Load secrets into .NET configuration API.
var client = new DaprClientBuilder().Build();
builder.Configuration.AddDaprSecretStore(
    "apc-secret-store",
    new List<DaprSecretDescriptor> { new("computerVision") },
    client,
    TimeSpan.FromSeconds(20));
builder.Configuration.AddDaprConfigurationStore(
    "apc-configuration",
    new List<string> { "licencePlateRecognition:offlinelicenseplates" },
    client,
    TimeSpan.FromSeconds(20),
    new Dictionary<string, string> { { "label", "demo" } });

//Register HttpClient with Dapr InvocationHandler to use Dapr service invocation.
//HttpClient httpClient = DaprClient.CreateInvokeHttpClient("computervision");
builder.Services.AddHttpClient("DaprComputerVisionHttpClient", httpClient =>
{
    //Set the base address to the name of the HTTPEndpoint (or appid).
    httpClient.BaseAddress = new Uri("http://computervision");
}).AddHttpMessageHandler(() => new InvocationHandler());

//Register HttpClient without any Dapr service invocation but with Dapr sourced configuration.
builder.Services.AddHttpClient("DirectComputerVisionHttpClient", (serviceProvider, httpClient) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var apiKey = configuration["apiKey"];
    httpClient.Timeout = TimeSpan.FromSeconds(1000);
    httpClient.BaseAddress = new Uri("https://westeurope.api.cognitive.microsoft.com");
    httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//Upload using HTTPEndpoint computervision.
app.MapPost("/upload", async (IFormFile photo, IHttpClientFactory httpClientFactory, HttpContext httpContext) =>
{
    var timestamp = DateTime.UtcNow;
    ByteArrayContent photoContent;
    using (var photoStream = new MemoryStream())
    {
        await photo.CopyToAsync(photoStream);
        photoContent = new ByteArrayContent(photoStream.ToArray());
    }

    var httpClient = httpClientFactory.CreateClient("DaprComputerVisionHttpClient");
    var url = "computervision/imageanalysis:analyze?features=read&api-version=2023-04-01-preview";
    photoContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
    var response = await httpClient.PostAsync(url, photoContent);
    if(!response.IsSuccessStatusCode) return Results.BadRequest();
    var imageAnalysisResult = await response.Content.ReadFromJsonAsync<ImageAnalysisResult>();

    await ProcesExtractedTextAsync(timestamp, imageAnalysisResult?.ReadResult?.Content);
    return Results.Ok();
});

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
    if(!response.IsSuccessStatusCode) return Results.BadRequest();
    var imageAnalysisResult = await response.Content.ReadFromJsonAsync<ImageAnalysisResult>();

    await ProcesExtractedTextAsync(timestamp, imageAnalysisResult?.ReadResult?.Content);
    return Results.Ok();
});

app.MapPost("/uploadoffline", async (
    string extractedText,
    IConfiguration configuration) =>
{
    var timestamp = DateTime.UtcNow;
    await ProcesExtractedTextAsync(timestamp, extractedText);
});

app.MapPost("/uploadappconfig", async (
    IConfiguration configuration) =>
{
    var timestamp = DateTime.UtcNow;
    var offlinelicenseplates = configuration.GetSection("licencePlateRecognition")["offlinelicenseplates"].Split("\r\n");
    var extractedText = offlinelicenseplates.OrderBy(x => Guid.NewGuid()).First();
    await ProcesExtractedTextAsync(timestamp, extractedText);
});

app.Use(async (context, next) =>
{
    context.Response.Headers["traceroot"] = Activity.Current?.RootId;
    await next();
});

app.Run();




async Task ProcesExtractedTextAsync(DateTime timestamp, string? extractedText)
{
    if (extractedText == null) return;
    var europeseNummerplaatRegex = new Regex(@"(?<indexCijfer>\d)[\sº]*-?\s*(?<letters>[A-Z]+)\s*-?\s*(?<cijfers>\d\d\d)"); //https://www.vlaanderen.be/de-europese-nummerplaat
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


