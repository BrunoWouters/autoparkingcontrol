using Dapr.Client;

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

var daprClient = app.Services.GetRequiredService<DaprClient>();
await daprClient.SaveStateAsync("statestore", "1-BAR-481", DateTime.UtcNow.AddDays(300));
await daprClient.SaveStateAsync("statestore", "2-DDJ-413", DateTime.UtcNow.AddDays(300));

app.MapGet("/licenseplatehaspermit/{licensePlate}", async (string licensePlate, DaprClient daprClient) =>
{
    var licensePlatePermitExpiry = await daprClient.GetStateAsync<DateTime?>("statestore", licensePlate);
    if(licensePlatePermitExpiry == null) return false;
    return DateTime.UtcNow < licensePlatePermitExpiry;    
});

app.Run();
