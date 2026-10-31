using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Bluetooth;
using BuzzahBuddy.Tests.TestHelpers;
using Xunit;

namespace BuzzahBuddy.Tests.Services;

/// <summary>Launch auto-connect: one attempt, a Try Again banner on failure, gone once connected.</summary>
public class AutoConnectServiceTests
{
    private static FakeBluetoothService Disconnected() =>
        new() { CurrentConnectionState = ConnectionState.Disconnected, ConnectedDevice = null };

    [Fact]
    public void A_failed_attempt_offers_try_again_and_a_later_connection_clears_it()
    {
        var bluetooth = Disconnected();
        bluetooth.ConnectToKnown = (_, _) => Task.FromResult(false);
        var service = new AutoConnectService(bluetooth, _ => { });

        service.Start("GLOVE-1"); // the fake completes synchronously

        Assert.Equal(AutoConnectState.Failed, service.State);
        Assert.Equal("Try Again", service.Banner?.Action);
        Assert.Contains("near your phone", service.Banner?.Message);

        bluetooth.RaiseConnectionStateChanged(ConnectionState.Connected);
        Assert.Equal(AutoConnectState.Idle, service.State);
        Assert.Null(service.Banner);
    }

    [Fact]
    public void Bluetooth_off_says_to_turn_it_on()
    {
        var bluetooth = Disconnected();
        bluetooth.BluetoothEnabled = false;
        bluetooth.ConnectToKnown = (_, _) => Task.FromResult(false);
        var service = new AutoConnectService(bluetooth, _ => { });

        service.Start("GLOVE-1");

        Assert.Equal("Turn on Bluetooth, then tap Try Again.", service.Banner?.Message);
    }

    [Fact]
    public async Task Cancel_stops_the_attempt_without_a_failure_banner()
    {
        var bluetooth = Disconnected();
        var released = new TaskCompletionSource();
        bluetooth.ConnectToKnown = async (_, ct) =>
        {
            ct.Register(() => released.TrySetResult());
            await released.Task;
            return false;
        };
        var service = new AutoConnectService(bluetooth, _ => { });

        service.Start("GLOVE-1");
        Assert.Equal(AutoConnectState.Connecting, service.State);
        Assert.Equal("Cancel", service.Banner?.Action);

        await service.CancelAsync();

        Assert.Equal(AutoConnectState.Idle, service.State);
    }

    [Fact]
    public void Success_announces_the_gloves()
    {
        var bluetooth = Disconnected();
        bluetooth.ConnectToKnown = (_, _) =>
        {
            bluetooth.ConnectedDevice = new GloveDevice { Id = "GLOVE-1", Name = "BlueBuzzah", HardwareVersion = "v3" };
            bluetooth.RaiseConnectionStateChanged(ConnectionState.Connected);
            return Task.FromResult(true);
        };
        var announced = new List<string>();
        var service = new AutoConnectService(bluetooth, announced.Add);

        service.Start("GLOVE-1");

        Assert.Equal(AutoConnectState.Idle, service.State);
        Assert.Equal(new[] { "Connected to BlueBuzzah v3" }, announced);
    }
}
