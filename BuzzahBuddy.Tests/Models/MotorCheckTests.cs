using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

public class MotorCheckTests
{
    private const string Ok = "OK,OK,OK,OK,OPEN";

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

    [Fact]
    public void Parse_UnknownToken_IsUnknown_AndTrims()
    {
        Assert.Equal(new[] { MotorCheck.Ok, MotorCheck.Unknown }, MotorChecks.Parse(" OK , BROKEN"));
    }

    [Fact]
    public void ProblemFingers_IndexToLittleNotOk_NeverThumb()
    {
        Assert.Equal(new[] { 2, 3 }, MotorChecks.ProblemFingers(MotorChecks.Parse("OK,OK,OPEN,UNKNOWN,NODRIVER")));
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
    [InlineData(3, MotorCheck.Unknown, true)]
    [InlineData(2, MotorCheck.Ok, false)]
    [InlineData(4, MotorCheck.Open, false)]    // an empty thumb port is normal
    [InlineData(4, MotorCheck.Unknown, false)]
    [InlineData(4, MotorCheck.NoDriver, true)]
    [InlineData(5, MotorCheck.NoDriver, false)]
    [InlineData(1, null, false)]                // no report
    public void IsMarked_IndexToLittleNotOk_ThumbOnDriverFault(int finger, MotorCheck? check, bool marked)
    {
        Assert.Equal(marked, MotorChecks.IsMarked(finger, check));
    }

    [Theory]
    [InlineData(2, MotorCheck.Open, ", didn't respond at startup")]
    [InlineData(1, MotorCheck.Unknown, ", didn't respond at startup")]
    [InlineData(0, MotorCheck.NoDriver, ", driver fault")]
    [InlineData(4, MotorCheck.NoDriver, ", driver fault")]
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
    [InlineData("OK,OK,OPEN,OK,OPEN", null,
        "Your primary glove's ring motor didn't respond when it turned on. Sessions still run. Test it to check.")]
    [InlineData("OK,OK,OPEN,OPEN,OPEN", null,
        "Your primary glove's ring and little motors didn't respond when it turned on. Sessions still run. Test them to check.")]
    [InlineData(Ok, "UNKNOWN,OK,OK,OK,OPEN",
        "Your secondary glove's index motor didn't respond when it turned on. Sessions still run. Test it to check.")]
    [InlineData(null, "OK,NODRIVER,OPEN,OK,OPEN",
        "Your secondary glove's middle and ring motors didn't respond when it turned on. Sessions still run. Test them to check.")]
    [InlineData("OK,OK,OK,OPEN,OK", "OPEN,OK,OK,OK,OK",
        "Your primary glove's little motor and your secondary glove's index motor didn't respond when they turned on. Sessions still run. Test them to check.")]
    [InlineData("OK,OK,OPEN,OPEN,OK", "OPEN,OPEN,OK,OK,OK",
        "Your primary glove's ring and little motors and your secondary glove's index and middle motors didn't respond when they turned on. Sessions still run. Test them to check.")]
    public void BannerMessage_NamesGlovesAndFingers(string? primary, string? secondary, string expected)
    {
        Assert.Equal(expected, MotorChecks.BannerMessage(MotorChecks.Parse(primary), MotorChecks.Parse(secondary)));
    }

    [Theory]
    [InlineData(Ok, Ok)]
    [InlineData(null, null)]
    [InlineData("OK,OK,OK,OK,NODRIVER", null)] // the thumb never raises the banner
    public void BannerMessage_NoProblem_IsNull(string? primary, string? secondary)
    {
        Assert.Null(MotorChecks.BannerMessage(MotorChecks.Parse(primary), MotorChecks.Parse(secondary)));
    }

    [Theory]
    [InlineData("OK,OK,OPEN,OK,OPEN",
        "Ring motor didn't respond at startup. Tap a finger to test it. If it doesn't buzz, check its connection, then restart the glove.")]
    [InlineData("OK,OK,OPEN,OPEN,OPEN",
        "Ring and little motors didn't respond at startup. Tap a finger to test it. If it doesn't buzz, check its connection, then restart the glove.")]
    [InlineData("OK,OK,OK,OK,NODRIVER",
        "Thumb motor didn't respond at startup. Tap a finger to test it. If it doesn't buzz, check its connection, then restart the glove.")]
    public void GloveLine_NamesMarkedFingers(string checks, string expected)
    {
        Assert.Equal(expected, MotorChecks.GloveLine(MotorChecks.Parse(checks)));
    }

    [Theory]
    [InlineData(Ok)]
    [InlineData(null)]
    public void GloveLine_NothingMarked_IsNull(string? checks)
    {
        Assert.Null(MotorChecks.GloveLine(MotorChecks.Parse(checks)));
    }
}
