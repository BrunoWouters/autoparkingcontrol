namespace AutoParkingControl.Contracts.Events;

public record struct RequiredSessionNotFound(string LicencePlate, DateTime VehicleDetectedOn);