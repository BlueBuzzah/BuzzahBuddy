using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Bluetooth;
using Xunit;

namespace BuzzahBuddy.Tests.Services;

public class ResponseMatcherTests
{
    [Fact]
    public void StaleSessionStatusReply_WhilePauseIsPending_IsIgnored()
    {
        // A late poll reply carries SESSION_STATUS, the key the legacy map expects for
        // SESSION_PAUSE too, so only the CMD echo can tell them apart.
        var stale = CommandResponse.Parse("CMD:SESSION_STATUS\nSESSION_STATUS:RUNNING\nELAPSED:42\n\x04");

        Assert.False(ResponseMatcher.Completes("SESSION_PAUSE", stale));
    }

    [Fact]
    public void MatchingCmdEcho_CompletesThePendingCommand()
    {
        var reply = CommandResponse.Parse("CMD:SESSION_PAUSE\nSESSION_STATUS:PAUSED\n\x04");

        Assert.True(ResponseMatcher.Completes("SESSION_PAUSE", reply));
    }

    [Fact]
    public void CmdEcho_CompletesACommandWithNoLegacyKey()
    {
        var reply = CommandResponse.Parse("CMD:PROFILE_CUSTOM\nSTATUS:CUSTOM_LOADED\n\x04");

        Assert.True(ResponseMatcher.Completes("PROFILE_CUSTOM", reply));
        Assert.False(ResponseMatcher.Completes("PARAM_SET", reply));
    }

    [Fact]
    public void ErrorFrame_WithOtherCommandsEcho_IsIgnored()
    {
        var error = CommandResponse.Parse("CMD:BATTERY\nERROR:Busy\n\x04");

        Assert.False(ResponseMatcher.Completes("SESSION_START", error));
        Assert.True(ResponseMatcher.Completes("BATTERY", error));
    }

    [Fact]
    public void UnparseableMessageError_WithoutCmd_CompletesAnyCommand()
    {
        var error = CommandResponse.Parse("ERROR:Invalid command format\n\x04");

        Assert.True(ResponseMatcher.Completes("SESSION_START", error));
    }

    [Fact]
    public void LegacyFrameWithoutCmd_StillMatchesOnExpectedKey()
    {
        var reply = CommandResponse.Parse("ROLE:PRIMARY\nNAME:BlueBuzzah\nSTATUS:IDLE\n\x04");

        Assert.True(ResponseMatcher.Completes("INFO", reply));
        // A legacy INFO frame lacks BATTERY's key, so it can't complete BATTERY.
        Assert.False(ResponseMatcher.Completes("BATTERY", reply));
    }

    [Fact]
    public void LegacyFrameWithoutCmd_AcceptedByCommandsWithNoExpectedKey()
    {
        var reply = CommandResponse.Parse("STATUS:CUSTOM_LOADED\n\x04");

        Assert.True(ResponseMatcher.Completes("PROFILE_CUSTOM", reply));
    }

    [Theory]
    [InlineData("PROFILE_LOAD:3", "PROFILE_LOAD")]
    [InlineData("session_status", "SESSION_STATUS")]
    [InlineData("INFO", "INFO")]
    public void CommandName_IsTheUppercasedTextBeforeTheFirstColon(string command, string expected)
    {
        Assert.Equal(expected, ResponseMatcher.CommandName(command));
    }
}
