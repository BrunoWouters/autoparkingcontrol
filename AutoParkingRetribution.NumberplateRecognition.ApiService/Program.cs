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

app.MapPost("/upload", (
    IFormFileCollection photos, 
    DaprClient daprClient,
    ILogger<Program> logger) =>
{
//OCR
//Bericht op bus -> nrplt + tijdstip
    var plate = new RecognizedPlate(1, "2-DDJ-413", DateTime.UtcNow);

    var filenames = new List<string>();
    foreach (var photo in photos)
    {
        filenames.Add(photo.FileName);
    }
    return Results.Ok(filenames);
});

app.Run();

public record struct RecognizedPlate(int zone, string plate, DateTime timestamp);