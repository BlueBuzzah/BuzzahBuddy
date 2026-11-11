using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

public class MotorCheckTests
{
    private const string Ok = "OK,OK,OK,OK,OPEN";

    private const string OpenLine =
        "Tap a finger to test it. If it doesn't buzz, check its connection, then restart the glove.";

    [Fact]
    public void Parse_FirmwareTokens_InFingerOrder()
    {
        Assert.Equal(
            new[] { MotorCheck.Ok, MotorCheck.Ok, MotorCheck.Open, MotorCheck.NoDriver, MotorCheck.Unknown },
            MotorChecks.Parse("OK,OK,OPEN,NODRIVER,UNKNOWN"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Parse_MissingOrEmpty_IsNull(string? value)
    {
        Assert.Null(MotorChecks.Parse(value));
    }

    [Theory]
    [InlineData("OK,,OK")]
    [InlineData("OK,OK,")]
    [InlineData(",OK")]
    [InlineData(" , ")]
    [InlineData("OK, ,OK")]
    public void Parse_EmptyToken_IsNull(string value)
    {
        Assert.Null(MotorChecks.Parse(value));
    }

    [Fact]
    public void Parse_UnknownToken_IsUnknown_AndTrims()
    {
        Assert.Equal(new[] { MotorCheck.Ok, MotorCheck.Unknown }, MotorChecks.Parse(" OK , BROKEN"));
    }

    [Fact]
    public void ProblemFingers_OpenOrNoDriver_ThumbOnlyNoDriver_NeverUnknown()
    {
        Assert.Equal(new[] { 2, 4 }, MotorChecks.ProblemFingers(MotorChecks.Parse("OK,OK,OPEN,UNKNOWN,NODRIVER")));
        Assert.Equal(new[] { 0, 1 }, MotorChecks.ProblemFingers(MotorChecks.Parse("NODRIVER,OPEN,OK,OK,OPEN")));
        Assert.Empty(MotorChecks.ProblemFingers(MotorChecks.Parse("UNKNOWN,UNKNOWN,UNKNOWN,UNKNOWN,UNKNOWN")));
        Assert.Empty(MotorChecks.ProblemFingers(MotorChecks.Parse(Ok)));
        Assert.Empty(MotorChecks.ProblemFingers(null));
    }

    [Fact]
    public void ProblemFingers_ShortList_ChecksOnlyWhatItHas()
    {
        Assert.Equal(new[] { 1 }, MotorChecks.ProblemFingers(MotorChecks.Parse("OK,OPEN")));
    }

    [Theory]
    [InlineData(0, MotorCheck.Open, true)]
    [InlineData(3, MotorCheck.NoDriver, true)]
    [InlineData(0, MotorCheck.Unknown, false)] // Unknown is never a problem
    [InlineData(3, MotorCheck.Unknown, false)]
    [InlineData(2, MotorCheck.Ok, false)]
    [InlineData(4, MotorCheck.Open, false)]    // an empty thumb port is normal
    [InlineData(4, MotorCheck.Unknown, false)]
    [InlineData(4, MotorCheck.Ok, false)]
    [InlineData(4, MotorCheck.NoDriver, true)]
    [InlineData(5, MotorCheck.NoDriver, false)]
    [InlineData(1, null, false)]                // no report
    public void IsMarked_OneProblemRule(int finger, MotorCheck? check, bool marked)
    {
        Assert.Equal(marked, MotorChecks.IsMarked(finger, check));
    }

    [Theory]
    [InlineData(2, MotorCheck.Open, ", didn't respond when it turned on")]
    [InlineData(0, MotorCheck.NoDriver, ", driver didn't start")]
    [InlineData(4, MotorCheck.NoDriver, ", driver didn't start")]
    [InlineData(1, MotorCheck.Unknown, null)]
    [InlineData(4, MotorCheck.Unknown, null)]
    [InlineData(4, MotorCheck.Open, null)]
    [InlineData(0, MotorCheck.Ok, null)]
    [InlineData(0, null, null)]
    public void AccessibleSuffix_NamesTheProblem(int finger, MotorCheck? check, string? suffix)
    {
        Assert.Equal(suffix, MotorChecks.AccessibleSuffix(finger, check));
    }

    [Theory]
    [InlineData(new[] { 2 }, "ring")]
    [InlineData(new[] { 2, 3 }, "ring and little")]
    [InlineData(new[] { 1, 2, 3 }, "middle, ring and little")]
    [InlineData(new[] { 0, 1, 2, 3, 4 }, "index, middle, ring, little and thumb")]
    public void JoinFingers_LowercaseWithAnd(int[] fingers, string expected)
    {
        Assert.Equal(expected, MotorChecks.JoinFingers(fingers));
    }

    [Theory]
    // Open only, one and two fingers
    [InlineData("OK,OK,OPEN,OK,OPEN", null,
        "Your primary glove's ring motor didn't respond when it turned on. Sessions still run. Tap Test Motors to check it.")]
    [InlineData("OK,OK,OPEN,OPEN,OPEN", null,
        "Your primary glove's ring and little motors didn't respond when it turned on. Sessions still run. Tap Test Motors to check them.")]
    // NoDriver only, including the thumb
    [InlineData("OK,OK,OK,OK,NODRIVER", null,
        "Your primary glove's thumb driver didn't start. Sessions still run. Tap Test Motors to check it.")]
    [InlineData(null, "NODRIVER,NODRIVER,OK,OK",
        "Your secondary glove's index and middle drivers didn't start. Sessions still run. Tap Test Motors to check them.")]
    // Mixed on one glove; Unknown is left out
    [InlineData(null, "UNKNOWN,NODRIVER,OPEN,OK,OPEN",
        "Your secondary glove's ring motor didn't respond when it turned on. Its middle driver didn't start. Sessions still run. Tap Test Motors to check them.")]
    // Both gloves
    [InlineData("OK,OK,OK,OPEN,OK", "OPEN,OK,OK,OK,OK",
        "Your primary glove's little motor didn't respond when it turned on. Your secondary glove's index motor didn't respond when it turned on. Sessions still run. Tap Test Motors to check them.")]
    [InlineData("OK,OK,OPEN,OPEN,NODRIVER", "OK,OK,OK,OK,NODRIVER",
        "Your primary glove's ring and little motors didn't respond when it turned on. Its thumb driver didn't start. Your secondary glove's thumb driver didn't start. Sessions still run. Tap Test Motors to check them.")]
    public void BannerMessage_NamesGlovesFingersAndProblem(string? primary, string? secondary, string expected)
    {
        Assert.Equal(expected, MotorChecks.BannerMessage(MotorChecks.Parse(primary), MotorChecks.Parse(secondary)));
    }

    [Theory]
    [InlineData(Ok, Ok)]
    [InlineData(null, null)]
    [InlineData("UNKNOWN,UNKNOWN,UNKNOWN,UNKNOWN,UNKNOWN", "UNKNOWN,OK,OK,OK")] // Unknown never raises it
    [InlineData("OK,OK,OK,OK,UNKNOWN", null)]
    public void BannerMessage_NoProblem_IsNull(string? primary, string? secondary)
    {
        Assert.Null(MotorChecks.BannerMessage(MotorChecks.Parse(primary), MotorChecks.Parse(secondary)));
    }

    [Theory]
    [InlineData("OK,OK,OPEN,OK,OPEN",
        "Ring motor didn't respond when it turned on. " + OpenLine)]
    [InlineData("OK,OK,OPEN,OPEN,OPEN",
        "Ring and little motors didn't respond when it turned on. " + OpenLine)]
    [InlineData("OK,OK,OK,OK,NODRIVER",
        "Thumb driver didn't start. Charge the glove, then restart it.")]
    [InlineData("NODRIVER,NODRIVER,OK,OK",
        "Index and middle drivers didn't start. Charge the glove, then restart it.")]
    [InlineData("OK,OPEN,UNKNOWN,OK,NODRIVER",
        "Middle motor didn't respond when it turned on. " + OpenLine + " Thumb driver didn't start. Charge the glove, then restart it.")]
    public void GloveLine_NamesMarkedFingersAndNextStep(string checks, string expected)
    {
        Assert.Equal(expected, MotorChecks.GloveLine(MotorChecks.Parse(checks)));
    }

    [Theory]
    [InlineData(Ok)]
    [InlineData("UNKNOWN,UNKNOWN,UNKNOWN,UNKNOWN,UNKNOWN")]
    [InlineData(null)]
    public void GloveLine_NothingMarked_IsNull(string? checks)
    {
        Assert.Null(MotorChecks.GloveLine(MotorChecks.Parse(checks)));
    }
}
