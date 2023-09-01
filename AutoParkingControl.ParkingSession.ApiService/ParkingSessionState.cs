public class ParkingSessionState
{
    public DateTime? PaidSessionStartedOn { get; set; }
    public DateTime? LastSeen { get; set; }
    public bool ParkingFeeSent { get; set; }
}