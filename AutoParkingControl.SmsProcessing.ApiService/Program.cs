using AutoParkingControl.Contracts.Actors;
using Dapr.Actors.Client;

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

app.MapGet("/start", async (string licensePlate) =>
{
    var parkingSessionActor = ActorProxy.Create<IParkingSessionActor>(new Dapr.Actors.ActorId(licensePlate), "ParkingSessionActor");
    await parkingSessionActor.StartSessionAsync(new StartSession(DateTime.UtcNow));
});

app.MapGet("/stop", async (string licensePlate) =>
{
    var parkingSessionActor = ActorProxy.Create<IParkingSessionActor>(new Dapr.Actors.ActorId(licensePlate), "ParkingSessionActor");
    await parkingSessionActor.StopSessionAsync(new StopSession(DateTime.UtcNow));    
});

app.Run();