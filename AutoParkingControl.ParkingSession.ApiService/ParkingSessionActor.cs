
public class ParkingSessionActor : Actor, IParkingSessionActor, IRemindable
{
    private ParkingSessionState _state;
    private readonly DaprClient _daprClient;
    private readonly HttpClient _daprHttpClient;
    private bool _isExpired;

    public ParkingSessionActor(ActorHost host, DaprClient daprClient) : base(host)
    {
        _daprClient = daprClient;
        _daprHttpClient = DaprClient.CreateInvokeHttpClient();
        _state = new ParkingSessionState();
    }

    public async Task RegisterVehicleDetectionAsync(RegisterVehicleDetection registerLocation)
    {
        _state.LastSeen = registerLocation.Timestamp;
        var hasPermit = await _daprHttpClient.GetFromJsonAsync<bool>($"https://residents-apiservice/licenseplatehaspermit/{Id.GetId()}");
        if(hasPermit)
        {
            await RemoveSessionAsync();
            return;
        }
        
        if (_state.PaidSessionStartedOn.HasValue || _state.ParkingFeeSent)
        {
            return;
        }



        var reminder = await GetReminderAsync(nameof(CheckSessionStatusAfterGracePeriodReminderAsync));
        if (reminder is null)
        {
            await RegisterReminderAsync(nameof(CheckSessionStatusAfterGracePeriodReminderAsync), null, TimeSpan.FromMinutes(1), Timeout.InfiniteTimeSpan);
        }
    }

    public async Task StartSessionAsync(StartSession registerPayment)
    {
        _state.PaidSessionStartedOn = registerPayment.Timestamp;
        await RegisterExpiryReminderAsync();
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
        await _daprClient.PublishEventAsync("pubsub", nameof(RequiredSessionNotFound), new RequiredSessionNotFound(Id.GetId(), _state.LastSeen.GetValueOrDefault()));
        await RegisterExpiryReminderAsync();
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
        _isExpired = true;
        _state = new ParkingSessionState();
        await StateManager.TryRemoveStateAsync(nameof(ParkingSessionState));
        await UnregisterReminderAsync(nameof(CheckSessionStatusAfterGracePeriodReminderAsync));
        await UnregisterReminderAsync(nameof(ExpiryReminderAsync));
    }

    protected override async Task OnPostActorMethodAsync(ActorMethodContext actorMethodContext)
    {
        if(_isExpired) {
            //Make sure the actor is valid if a call is made to this actor before it is removed.
            _isExpired = false;
            return;
        }

        await StateManager.SetStateAsync(nameof(ParkingSessionState), _state);
    }

    protected override async Task OnActivateAsync()
    {
        var tryGetResult = await StateManager.TryGetStateAsync<ParkingSessionState>(nameof(ParkingSessionState));
        _state = tryGetResult.HasValue ? tryGetResult.Value : new ParkingSessionState();
    }
}