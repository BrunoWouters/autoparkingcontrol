using Dapr.AspNetCore;
using AutoParkingControl.Contracts.Events;
using Microsoft.AspNetCore.Mvc;
using Dapr.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDaprClient();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCloudEvents();
app.UseRouting();
app.MapSubscribeHandler();

app.MapPost("/sendfine", async ([FromBody] RequiredSessionNotFound requiredSessionNotFound, DaprClient daprClient) =>
{
    var body = 
    @$"<b>Fine for {requiredSessionNotFound.LicensePlate}</b><br>
    On {requiredSessionNotFound.VehicleDetectedOn.ToLocalTime():G} we found your vehicle parked without a valid session.";
    await daprClient.InvokeBindingAsync("smtp", "create", body, new Dictionary<string, string>{
        {"emailFrom", "finedep@autoparkingcontrol.com"},
        {"emailTo", "finereceipient@mailprovider.com"},
        {"subject", "Fine"}
    });
}).WithTopic("pubsub", "RequiredSessionNotFound");

app.Run();