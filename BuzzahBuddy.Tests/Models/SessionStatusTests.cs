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
}
