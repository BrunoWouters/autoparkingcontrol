public class ParkingSessionActor : Actor, IParkingSessionActor, IRemindable
{
    private ParkingSessionState _state;
    private readonly DaprClient _daprClient;

    public ParkingSessionActor(ActorHost host, DaprClient daprClient) : base(host)
    {
        _daprClient = daprClient;
    }

    public async Task RegisterVehicleDetectionAsync(RegisterVehicleDetection registerLocation)
    {
        if (_state.PaidSessionStartedOn.HasValue || _state.ParkingFeeSent)
        {
            return;
        }

        var checkSessionStatusAfterGracePeriodReminder = await GetReminderAsync(nameof(CheckSessionStatusAfterGracePeriodReminderAsync));
        if (checkSessionStatusAfterGracePeriodReminder != null)
        {
            await RegisterReminderAsync(nameof(checkSessionStatusAfterGracePeriodReminder), null, TimeSpan.FromMinutes(10), Timeout.InfiniteTimeSpan);
        }
    }

    public Task StartSessionAsync(StartSession registerPayment)
    {
        _state.PaidSessionStartedOn = registerPayment.Timestamp;
        return Task.CompletedTask;
    }

    public async Task StopSessionAsync(StopSession registerPayment)
    {
        await RemoveSessionAsync();
    }

    public async Task ReceiveReminderAsync(string reminderName, byte[] state, TimeSpan dueTime, TimeSpan period)
    {
        switch (reminderName)
        {
            case nameof(CheckSessionStatusAfterGracePeriodReminderAsync):
                await CheckSessionStatusAfterGracePeriodReminderAsync();
                break;
            case nameof(ExpiryReminderAsync):
                await ExpiryReminderAsync();
                break;
            default:
                throw new ArgumentOutOfRangeException(reminderName);
        }
    }

    private async Task CheckSessionStatusAfterGracePeriodReminderAsync()
    {
        if (_state.PaidSessionStartedOn.HasValue)
        {
            return;
        }

        await SendParkingFeeAsync();
    }

    private async Task SendParkingFeeAsync()
    {
        if (_state.ParkingFeeSent) return;
        await _daprClient.PublishEventAsync("pubsub", nameof(RequiredSessionNotFound), new RequiredSessionNotFound(Id.GetId(), _state.LastSeen.Value));
        _state.ParkingFeeSent = true;
    }

    private async Task RegisterExpiryReminderAsync()
    {
        await RegisterReminderAsync(nameof(ExpiryReminderAsync), null, TimeSpan.FromHours(24), Timeout.InfiniteTimeSpan);
    }

    private async Task ExpiryReminderAsync()
    {
        await RemoveSessionAsync();
    }

    private async Task RemoveSessionAsync()
    {
        await StateManager.RemoveStateAsync(nameof(ParkingSessionState));
        await UnregisterReminderAsync(nameof(CheckSessionStatusAfterGracePeriodReminderAsync));
        await UnregisterReminderAsync(nameof(ExpiryReminderAsync));
    }

    protected override async Task OnPostActorMethodAsync(ActorMethodContext actorMethodContext)
    {
        //unless expired?
        await StateManager.SetStateAsync(nameof(ParkingSessionState), _state);
    }

    protected override async Task OnActivateAsync()
    {
        _state = await StateManager.GetStateAsync<ParkingSessionState>(nameof(ParkingSessionState))
            ?? new ParkingSessionState();
    }
}