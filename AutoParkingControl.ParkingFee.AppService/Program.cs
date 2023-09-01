using Dapr.AspNetCore;
using AutoParkingControl.Contracts.Events;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCloudEvents();
app.UseRouting();
app.UseEndpoints(endpoints => endpoints.MapSubscribeHandler());


app.MapPost("/sendfine", async ([FromBody] RequiredSessionNotFound requiredSessionNotFound) =>
{

}).WithTopic("pubsub", "RequiredSessionNotFound");

app.Run();