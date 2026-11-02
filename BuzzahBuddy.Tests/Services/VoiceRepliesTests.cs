using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Glove;
using Xunit;

namespace BuzzahBuddy.Tests.Services;

public class VoiceRepliesTests
{
    private static VoiceSnapshot Snap(
        bool connected = true, SessionStatus? session = null, int profile = 1,
        double? pv = 4.02, bool? pLow = null, double? sv = 4.02, bool? sLow = null,
        bool? linked = true, bool switching = false) =>
        new(connected, session ?? SessionStatus.CreateIdle(), profile, pv, pLow, sv, sLow, linked, switching);

    private static SessionStatus Running(int elapsed = 600, int total = 3120) =>
        new() { Status = SessionState.RUNNING, ElapsedTimeSeconds = elapsed, TotalTimeSeconds = total };

    private static SessionStatus Paused(int elapsed = 600, int total = 3120) =>
        new() { Status = SessionState.PAUSED, ElapsedTimeSeconds = elapsed, TotalTimeSeconds = total };

    [Theory]
    [InlineData(VoiceCommand.Start)]
    [InlineData(VoiceCommand.Pause)]
    [InlineData(VoiceCommand.Resume)]
    [InlineData(VoiceCommand.Stop)]
    [InlineData(VoiceCommand.CheckBattery)]
    [InlineData(VoiceCommand.CheckProfile)]
    public void NotConnected_AnyCommand(VoiceCommand command)
    {
        var d = VoiceReplies.Decide(command, 0, Snap(connected: false));
        Assert.Equal(VoiceReplies.NotConnected, d.Reply);
        Assert.Equal(VoiceAction.None, d.Action);
    }

    [Fact]
    public void Start_Idle()
    {
        var d = VoiceReplies.Decide(VoiceCommand.Start, 0, Snap());
        Assert.Equal(VoiceReplies.Starting("Regular"), d.Reply);
        Assert.Equal("Starting your Regular session.", d.Reply);
        Assert.Equal(VoiceAction.Start, d.Action);
    }

    [Fact]
    public void Start_QuickTest_Runs()
    {
        var d = VoiceReplies.Decide(VoiceCommand.Start, 0, Snap(profile: 6));
        Assert.Equal("Starting your Quick Test session.", d.Reply);
        Assert.Equal(VoiceAction.Start, d.Action);
    }

    [Fact]
    public void Start_UnknownProfile_DropsName()
    {
        var d = VoiceReplies.Decide(VoiceCommand.Start, 0, Snap(profile: 0));
        Assert.Equal("Starting your session.", d.Reply);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Start_Running(int requested)
    {
        var d = VoiceReplies.Decide(VoiceCommand.Start, requested, Snap(session: Running()));
        Assert.Equal(VoiceReplies.AlreadyRunning("Regular"), d.Reply);
        Assert.Equal("Your Regular session is already running.", d.Reply);
        Assert.Equal(VoiceAction.None, d.Action);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Start_Paused(int requested)
    {
        var d = VoiceReplies.Decide(VoiceCommand.Start, requested, Snap(session: Paused()));
        Assert.Equal(VoiceReplies.AlreadyPausedStart("Regular"), d.Reply);
        Assert.Equal("Your Regular session is paused.", d.Reply);
        Assert.Equal(VoiceAction.None, d.Action);
    }

    [Fact]
    public void StartNoisy_AlreadyNoisy()
    {
        var d = VoiceReplies.Decide(VoiceCommand.Start, 2, Snap(profile: 2));
        Assert.Equal("Starting your Noisy session.", d.Reply);
        Assert.Equal(VoiceAction.Start, d.Action);
    }

    [Fact]
    public void StartNoisy_OtherProfile()
    {
        var d = VoiceReplies.Decide(VoiceCommand.Start, 2, Snap(profile: 1));
        Assert.Equal(VoiceReplies.Switching("Noisy"), d.Reply);
        Assert.Equal("Switching to Noisy. Your session starts when the gloves restart.", d.Reply);
        Assert.Equal(VoiceAction.SwitchThenStart, d.Action);
        Assert.Equal(2, d.ProfileId);
    }

    [Fact]
    public void Start_Low_Primary()
    {
        var d = VoiceReplies.Decide(VoiceCommand.Start, 0, Snap(pv: 3.70));
        Assert.Equal(VoiceReplies.NeedsCharging, d.Reply);
        Assert.Equal(VoiceAction.None, d.Action);
    }

    [Fact]
    public void Start_Low_Secondary()
    {
        var d = VoiceReplies.Decide(VoiceCommand.Start, 0, Snap(sv: 3.70));
        Assert.Equal(VoiceReplies.NeedsCharging, d.Reply);
        Assert.Equal(VoiceAction.None, d.Action);
    }

    [Fact]
    public void Start_Low_GloveLevelWins()
    {
        var d = VoiceReplies.Decide(VoiceCommand.Start, 0, Snap(pv: 3.85, pLow: true));
        Assert.Equal(VoiceReplies.NeedsCharging, d.Reply);
        Assert.Equal(VoiceAction.None, d.Action);
    }

    [Fact]
    public void StartNoisy_Low_DoesNotSwitch()
    {
        var d = VoiceReplies.Decide(VoiceCommand.Start, 2, Snap(profile: 1, pv: 3.70));
        Assert.Equal(VoiceReplies.NeedsCharging, d.Reply);
        Assert.Equal(VoiceAction.None, d.Action);
    }

    [Fact]
    public void Start_NoReading_DoesNotBlock()
    {
        var d = VoiceReplies.Decide(VoiceCommand.Start, 0, Snap(pv: null, sv: null));
        Assert.Equal(VoiceAction.Start, d.Action);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 1)]
    public void Start_SecondaryNotLinked(int requested, int device)
    {
        var d = VoiceReplies.Decide(VoiceCommand.Start, requested, Snap(profile: device, linked: false));
        Assert.Equal(VoiceReplies.NoSecondary, d.Reply);
        Assert.Equal("Your secondary glove isn't connected.", d.Reply);
        Assert.Equal(VoiceAction.None, d.Action);
    }

    [Fact]
    public void Start_SecondaryLinkUnknown_DoesNotBlock()
    {
        var d = VoiceReplies.Decide(VoiceCommand.Start, 0, Snap(linked: null));
        Assert.Equal(VoiceAction.Start, d.Action);
    }

    [Fact]
    public void Start_WhileSwitching()
    {
        var d = VoiceReplies.Decide(VoiceCommand.Start, 0, Snap(switching: true));
        Assert.Equal(VoiceReplies.Restarting, d.Reply);
        Assert.Equal(VoiceAction.None, d.Action);
    }

    [Fact]
    public void Pause()
    {
        var running = VoiceReplies.Decide(VoiceCommand.Pause, 0, Snap(session: Running()));
        Assert.Equal(VoiceReplies.Paused, running.Reply);
        Assert.Equal(VoiceAction.Pause, running.Action);

        var paused = VoiceReplies.Decide(VoiceCommand.Pause, 0, Snap(session: Paused()));
        Assert.Equal(VoiceReplies.AlreadyPaused, paused.Reply);
        Assert.Equal(VoiceAction.None, paused.Action);

        var idle = VoiceReplies.Decide(VoiceCommand.Pause, 0, Snap());
        Assert.Equal(VoiceReplies.NoSession, idle.Reply);
        Assert.Equal(VoiceAction.None, idle.Action);
    }

    [Fact]
    public void Resume()
    {
        var paused = VoiceReplies.Decide(VoiceCommand.Resume, 0, Snap(session: Paused()));
        Assert.Equal(VoiceReplies.Resumed, paused.Reply);
        Assert.Equal(VoiceAction.Resume, paused.Action);

        var running = VoiceReplies.Decide(VoiceCommand.Resume, 0, Snap(session: Running()));
        Assert.Equal(VoiceReplies.ResumeWhileRunning, running.Reply);
        Assert.Equal(VoiceAction.None, running.Action);

        var idle = VoiceReplies.Decide(VoiceCommand.Resume, 0, Snap());
        Assert.Equal(VoiceReplies.NoSession, idle.Reply);
        Assert.Equal(VoiceAction.None, idle.Action);
    }

    [Fact]
    public void Stop()
    {
        foreach (var session in new[] { Running(), Paused() })
        {
            var d = VoiceReplies.Decide(VoiceCommand.Stop, 0, Snap(session: session));
            Assert.Equal(VoiceReplies.ConfirmStop, d.Reply);
            Assert.Equal(VoiceAction.ConfirmStop, d.Action);
        }

        var idle = VoiceReplies.Decide(VoiceCommand.Stop, 0, Snap());
        Assert.Equal(VoiceReplies.NoSession, idle.Reply);
        Assert.Equal(VoiceAction.None, idle.Action);
    }

    [Theory]
    [InlineData(VoiceCommand.Pause)]
    [InlineData(VoiceCommand.Resume)]
    [InlineData(VoiceCommand.Stop)]
    public void Switching_PauseResumeStop(VoiceCommand command)
    {
        var d = VoiceReplies.Decide(command, 0, Snap(switching: true));
        Assert.Equal(VoiceReplies.NoSession, d.Reply);
        Assert.Equal(VoiceAction.None, d.Action);
    }

    [Fact]
    public void CheckBattery_Both()
    {
        // 4.02 V = 80 percent, 3.98 V = 75 percent on the firmware curve.
        var d = VoiceReplies.Decide(VoiceCommand.CheckBattery, 0, Snap(pv: 4.02, sv: 3.98));
        Assert.Equal("Primary glove 80 percent, secondary 75 percent.", d.Reply);
        Assert.Equal(VoiceAction.None, d.Action);
    }

    [Fact]
    public void CheckBattery_LowPrimary_MissingSecondary()
    {
        // 3.79 V = 35 percent.
        var d = VoiceReplies.Decide(VoiceCommand.CheckBattery, 0, Snap(pv: 3.79, sv: null));
        Assert.Equal("Primary glove 35 percent, low, secondary unavailable.", d.Reply);
    }

    [Fact]
    public void CheckBattery_LowSecondary_MissingPrimary()
    {
        var d = VoiceReplies.Decide(VoiceCommand.CheckBattery, 0, Snap(pv: null, sv: 3.79));
        Assert.Equal("Primary glove unavailable, secondary 35 percent, low.", d.Reply);
    }

    [Fact]
    public void CheckProfile_Idle()
    {
        var d = VoiceReplies.Decide(VoiceCommand.CheckProfile, 0, Snap(profile: 3));
        Assert.Equal("Your gloves are set to Hybrid.", d.Reply);
        Assert.Equal(VoiceAction.None, d.Action);
    }

    [Fact]
    public void CheckProfile_RunningAndPaused_WithTimeLeft()
    {
        var running = VoiceReplies.Decide(VoiceCommand.CheckProfile, 0, Snap(session: Running()));
        Assert.Equal("Your Regular session is running, 42 minutes left.", running.Reply);

        var paused = VoiceReplies.Decide(VoiceCommand.CheckProfile, 0, Snap(session: Paused()));
        Assert.Equal("Your Regular session is paused, 42 minutes left.", paused.Reply);
    }

    [Fact]
    public void CheckProfile_UnknownTotal()
    {
        var running = VoiceReplies.Decide(VoiceCommand.CheckProfile, 0, Snap(session: Running(total: 0)));
        Assert.Equal("Your Regular session is running.", running.Reply);

        var paused = VoiceReplies.Decide(VoiceCommand.CheckProfile, 0, Snap(session: Paused(total: 0)));
        Assert.Equal("Your Regular session is paused.", paused.Reply);
    }

    [Fact]
    public void CheckProfile_WhileSwitching_NamesRequestedProfile()
    {
        var d = VoiceReplies.Decide(VoiceCommand.CheckProfile, 0, Snap(profile: 2, switching: true));
        Assert.Equal("Your gloves are set to Noisy.", d.Reply);
    }

    [Fact]
    public void Result_Start()
    {
        Assert.Equal("Starting your Regular session.", VoiceReplies.Result(VoiceAction.Start, 1, null));
        Assert.Equal(VoiceReplies.NeedsCharging,
            VoiceReplies.Result(VoiceAction.Start, 1, new BlueBuzzahCommandException("Battery too low")));
        Assert.Equal(VoiceReplies.NoSecondary,
            VoiceReplies.Result(VoiceAction.Start, 1, new BlueBuzzahCommandException("Secondary not connected")));
        Assert.Equal(VoiceReplies.StartFailed,
            VoiceReplies.Result(VoiceAction.Start, 1, new InvalidOperationException("boom")));
    }

    [Fact]
    public void Result_SwitchThenStart()
    {
        Assert.Equal(VoiceReplies.Switching("Noisy"), VoiceReplies.Result(VoiceAction.SwitchThenStart, 2, null));
        Assert.Equal(VoiceReplies.StartFailed,
            VoiceReplies.Result(VoiceAction.SwitchThenStart, 2, new TimeoutException()));
    }

    [Fact]
    public void Result_PauseResumeStop()
    {
        var error = new InvalidOperationException("boom");
        Assert.Equal(VoiceReplies.Paused, VoiceReplies.Result(VoiceAction.Pause, 0, null));
        Assert.Equal(VoiceReplies.PauseFailed, VoiceReplies.Result(VoiceAction.Pause, 0, error));
        Assert.Equal(VoiceReplies.Resumed, VoiceReplies.Result(VoiceAction.Resume, 0, null));
        Assert.Equal(VoiceReplies.ResumeFailed, VoiceReplies.Result(VoiceAction.Resume, 0, error));
        Assert.Equal(VoiceReplies.Ended, VoiceReplies.Result(VoiceAction.ConfirmStop, 0, null));
        Assert.Equal(VoiceReplies.StopFailed, VoiceReplies.Result(VoiceAction.ConfirmStop, 0, error));
    }

    [Fact]
    public void Strings_AreVerbatim()
    {
        Assert.Equal("Your gloves aren't connected.", VoiceReplies.NotConnected);
        Assert.Equal("Your gloves are restarting. Your session will start soon.", VoiceReplies.Restarting);
        Assert.Equal("Your gloves need charging before a session.", VoiceReplies.NeedsCharging);
        Assert.Equal("Couldn't start your session. Open BuzzahBuddy to try again.", VoiceReplies.StartFailed);
        Assert.Equal("Session paused.", VoiceReplies.Paused);
        Assert.Equal("Your session is already paused.", VoiceReplies.AlreadyPaused);
        Assert.Equal("No session is running.", VoiceReplies.NoSession);
        Assert.Equal("Session resumed.", VoiceReplies.Resumed);
        Assert.Equal("Your session is already running.", VoiceReplies.ResumeWhileRunning);
        Assert.Equal("End your session?", VoiceReplies.ConfirmStop);
        Assert.Equal("Session ended.", VoiceReplies.Ended);
        Assert.Equal("Okay, your session keeps going.", VoiceReplies.StopDeclined);
        Assert.Equal("Couldn't pause your session. Open BuzzahBuddy to try again.", VoiceReplies.PauseFailed);
        Assert.Equal("Couldn't resume your session. Open BuzzahBuddy to try again.", VoiceReplies.ResumeFailed);
        Assert.Equal("Couldn't end your session. Open BuzzahBuddy to try again.", VoiceReplies.StopFailed);
    }
}
