using Dapr.Client;
using Dapr.Actors;
using AutoParkingControl.Contracts.Actors;
using AutoParkingControl.Contracts.Events;
using Dapr.Actors.Runtime;

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
        var hasPermit = await _daprHttpClient.GetFromJsonAsync<bool>(
            $"https://residents-apiservice/licenseplatehaspermit/{Id.GetId()}");
        //http://localhost:<daprSidecarPort>/v1.0/invoke/residents-apiservice/method/licenseplatehaspermit/<licensePlate>
        if(hasPermit)
        {
            await RemoveSessionAsync();
            return;
        }
        
        if (_state.PaidSessionStartedOn.HasValue || _state.ParkingFeeSent)
        {
            return;
        }

        //GET http://localhost:<daprSidecarPort>/v1.0/actors/ParkingSessionActor/<licensePlate>/reminders/CheckSessionStatusAfterGracePeriodReminderAsync
        var reminder = await GetReminderAsync(nameof(CheckSessionStatusAfterGracePeriodReminderAsync));
        if (reminder is null)
        {
            //POST http://localhost:<daprSideCarPort>/v1.0/actors/ParkingSessionActor/<licensePlate>/reminders/CheckSessionStatusAfterGracePeriodReminderAsync
            await RegisterReminderAsync(
                nameof(CheckSessionStatusAfterGracePeriodReminderAsync), 
                null, 
                TimeSpan.FromSeconds(20), 
                Timeout.InfiniteTimeSpan);
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
        //POST http://localhost:<daprSidecarPort>/v1.0/publish/pubsub/RequiredSessionNotFound
        await _daprClient.PublishEventAsync(
            "pubsub", 
            nameof(RequiredSessionNotFound),
             new RequiredSessionNotFound(Id.GetId(), _state.LastSeen.GetValueOrDefault()));
        await RegisterExpiryReminderAsync();
        _state.ParkingFeeSent = true;
    }

    private async Task RegisterExpiryReminderAsync()
    {
        //POST http://localhost:<daprSidecarPort>/v1.0/actors/ParkingSessionActor/<licensePlate>/reminders/ExpiryReminderAsync
        await RegisterReminderAsync(
            nameof(ExpiryReminderAsync), 
            null, 
            TimeSpan.FromHours(24), 
            Timeout.InfiniteTimeSpan);
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
        //DELETE http://localhost:<daprSidecarPort>/v1.0/actors/ParkingSessionActor/<licensePlate>/reminders/CheckSessionStatusAfterGracePeriodReminderAsync
        await UnregisterReminderAsync(nameof(CheckSessionStatusAfterGracePeriodReminderAsync));
        await UnregisterReminderAsync(nameof(ExpiryReminderAsync));
    }

    protected override async Task OnActivateAsync()
    {
        //GET http://localhost:<daprSidecarPort>/v1.0/actors/ParkingSessionActor/<licensePlate>/state/ParkingSessionState
        var tryGetResult = await StateManager.TryGetStateAsync<ParkingSessionState>(nameof(ParkingSessionState));
        _state = tryGetResult.HasValue ? tryGetResult.Value : new ParkingSessionState();
    }

    protected override async Task OnPostActorMethodAsync(ActorMethodContext actorMethodContext)
    {
        if(_isExpired) {
            //The actor has expired and does not need to be saved.
            _isExpired = false;
            return;
        }

        //POST http://localhost:<daprSidecarPort>/v1.0/actors/ParkingSessionState/<licensePlate>/state
        await StateManager.SetStateAsync(nameof(ParkingSessionState), _state);
    }
}