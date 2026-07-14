using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

public class SessionStatusTests
{
    [Fact]
    public void FromCommandResponse_RunningState_ParsesFields()
    {
        var response = CommandResponse.Parse("SESSION_STATUS:RUNNING\nELAPSED:60\nTOTAL:7200\nPROGRESS:1\n\x04");
        var status = SessionStatus.FromCommandResponse(response);
        Assert.Equal(SessionState.RUNNING, status.Status);
        Assert.Equal(60, status.ElapsedTimeSeconds);
        Assert.Equal(7200, status.TotalTimeSeconds);
        Assert.True(status.IsActive);
    }

    [Theory]
    [InlineData("LOW_BATTERY", SessionState.LOW_BATTERY, true, true)]
    [InlineData("STOPPING", SessionState.STOPPING, false, false)]
    [InlineData("CONNECTION_LOST", SessionState.CONNECTION_LOST, false, false)]
    [InlineData("CRITICAL_BATTERY", SessionState.CRITICAL_BATTERY, false, false)]
    [InlineData("READY", SessionState.READY, false, false)]
    [InlineData("CONNECTING", SessionState.CONNECTING, false, false)]
    [InlineData("ERROR", SessionState.ERROR, false, false)]
    [InlineData("PHONE_DISCONNECTED", SessionState.PHONE_DISCONNECTED, false, false)]
    [InlineData("PAUSED", SessionState.PAUSED, true, false)]
    [InlineData("IDLE", SessionState.IDLE, false, false)]
    public void FromCommandResponse_FirmwareStates_ParseWithoutDecay(
        string wire, SessionState expected, bool isActive, bool isRunning)
    {
        var response = CommandResponse.Parse($"SESSION_STATUS:{wire}\nELAPSED:10\nTOTAL:100\nPROGRESS:10\n\x04");
        var status = SessionStatus.FromCommandResponse(response);
        Assert.Equal(expected, status.Status);
        Assert.Equal(isActive, status.IsActive);
        Assert.Equal(isRunning, status.IsRunning);
    }

    [Fact]
    public void FromCommandResponse_UnknownWireString_ParsesToUnknown_NotIdle()
    {
        var response = CommandResponse.Parse("SESSION_STATUS:SOME_FUTURE_STATE\nELAPSED:10\nTOTAL:100\nPROGRESS:10\n\x04");
        var status = SessionStatus.FromCommandResponse(response);
        Assert.Equal(SessionState.UNKNOWN, status.Status);
        Assert.False(status.IsIdle);
    }

    [Fact]
    public void FromCommandResponse_LowercaseWireString_ParsesCaseInsensitively()
    {
        var response = CommandResponse.Parse("SESSION_STATUS:running\nELAPSED:10\nTOTAL:100\nPROGRESS:10\n\x04");
        var status = SessionStatus.FromCommandResponse(response);
        Assert.Equal(SessionState.RUNNING, status.Status);
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(1, 0.01)]
    [InlineData(50, 0.5)]
    [InlineData(100, 1.0)]
    public void ProgressFraction_PercentValue_ReturnsZeroToOneFraction(int progress, double expected)
    {
        var status = new SessionStatus { Progress = progress };
        Assert.Equal(expected, status.ProgressFraction, precision: 5);
    }
}
