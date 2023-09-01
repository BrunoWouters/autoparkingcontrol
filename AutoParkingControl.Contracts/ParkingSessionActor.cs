using Dapr.Actors;

namespace AutoParkingControl.Contracts.Actors;

public interface IParkingSessionActor : IActor
{
    Task StartSessionAsync(StartSession registerPayment);
    Task StopSessionAsync(StopSession registerPayment);

    Task RegisterVehicleDetectionAsync(RegisterVehicleDetection registerLocation);
}


public record struct StartSession(DateTime Timestamp);
public record struct StopSession(DateTime Timestamp);
public record struct RegisterVehicleDetection(DateTime Timestamp);