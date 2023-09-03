namespace AutoParkingControl.Contracts.Events;

public record struct RequiredSessionNotFound(string LicensePlate, DateTime VehicleDetectedOn);