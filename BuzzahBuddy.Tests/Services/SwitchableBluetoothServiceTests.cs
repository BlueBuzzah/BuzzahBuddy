using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Bluetooth;
using BuzzahBuddy.Tests.TestHelpers;
using Xunit;

namespace BuzzahBuddy.Tests.Services;

/// <summary>Demo Mode swaps the real gloves for pretend ones behind one IBluetoothService.</summary>
public class SwitchableBluetoothServiceTests
{
    private static (FakeBluetoothService Real, FakeBluetoothService Demo) Pair() =>
        (new FakeBluetoothService { CurrentConnectionState = ConnectionState.Disconnected },
         new FakeBluetoothService { CurrentConnectionState = ConnectionState.Disconnected });

    [Fact]
    public async Task Commands_go_to_whichever_service_is_active()
    {
        var (real, demo) = Pair();
        var service = new SwitchableBluetoothService(real, demo, demoMode: false);

        await service.SendCommandAsync("PING");
        await service.SetDemoModeAsync(true);
        await service.SendCommandAsync("INFO");

        Assert.Equal(new[] { "PING" }, real.SentCommands);
        Assert.Equal(new[] { "INFO" }, demo.SentCommands);
        Assert.True(service.IsDemoMode);
    }

    [Fact]
    public async Task Turning_on_demo_mode_disconnects_the_real_gloves_and_says_so()
    {
        var (real, demo) = Pair();
        real.CurrentConnectionState = ConnectionState.Connected;
        var service = new SwitchableBluetoothService(real, demo, demoMode: false);
        var states = new List<ConnectionState>();
        service.ConnectionStateChanged += (_, s) => states.Add(s);

        await service.SetDemoModeAsync(true);

        Assert.Equal(ConnectionState.Disconnected, real.CurrentConnectionState);
        Assert.Equal(new[] { ConnectionState.Disconnected }, states);
        Assert.Equal(ConnectionState.Disconnected, service.CurrentConnectionState);
    }

    [Fact]
    public void Events_from_the_inactive_service_are_not_passed_on()
    {
        var (real, demo) = Pair();
        var service = new SwitchableBluetoothService(real, demo, demoMode: true);
        var states = new List<ConnectionState>();
        service.ConnectionStateChanged += (_, s) => states.Add(s);

        real.RaiseConnectionStateChanged(ConnectionState.Connected);
        demo.RaiseConnectionStateChanged(ConnectionState.Connecting);

        Assert.Equal(new[] { ConnectionState.Connecting }, states);
    }
}
