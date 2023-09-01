global using Dapr.Client;
global using Dapr.Actors;
global using AutoParkingControl.Contracts.Actors;
global using AutoParkingControl.Contracts.Events;
global using Dapr.Actors.Runtime;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDaprClient();
builder.Services.AddActors(options =>
{
    // Register actor types and configure actor settings
    options.Actors.RegisterActor<ParkingSessionActor>();
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapActorsHandlers();

app.Run();