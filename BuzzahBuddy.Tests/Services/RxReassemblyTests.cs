using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Bluetooth;
using Xunit;

namespace BuzzahBuddy.Tests.Services;

public class RxReassemblyTests
{
    [Fact]
    public void MultiPacketResponse_IsReassembledAtEot()
    {
        var assembler = new RxFrameAssembler();

        var frames1 = assembler.Append("COMMAND:INFO\nCOMMAND:BAT"); // packet 1, no EOT
        Assert.Empty(frames1);

        var frames2 = assembler.Append("TERY\nCOMMAND:PING\n\x04");  // packet 2, EOT
        Assert.Single(frames2);
        Assert.Equal("COMMAND:INFO\nCOMMAND:BATTERY\nCOMMAND:PING\n\x04", frames2[0]);
    }

    [Fact]
    public void TwoFramesInOnePacket_BothDelivered()
    {
        var assembler = new RxFrameAssembler();

        // NOTE: "\x04" is a variable-length hex escape in C# -- "\x04B" would greedily
        // consume "04B" as a single 3-digit hex escape (0x04B = 'K'), swallowing the EOT.
        // Concatenating the two frames avoids that trap.
        var frames = assembler.Append("PONG:\n\x04" + "BATP:3.7\n\x04");

        Assert.Equal(2, frames.Count);
        Assert.Equal("PONG:\n\x04", frames[0]);
        Assert.Equal("BATP:3.7\n\x04", frames[1]);
    }

    [Fact]
    public void PartialFrame_IsRetainedAcrossAppends()
    {
        var assembler = new RxFrameAssembler();

        var frames1 = assembler.Append("PARTIAL");
        Assert.Empty(frames1);

        var frames2 = assembler.Append("REMAINDER\n\x04");
        Assert.Single(frames2);
        Assert.Equal("PARTIALREMAINDER\n\x04", frames2[0]);
    }

    [Fact]
    public void RunawayPartialFrame_BeyondCap_IsDiscarded()
    {
        var assembler = new RxFrameAssembler();

        // No EOT anywhere, and the partial buffer exceeds the 4096-char cap, so it
        // should be discarded rather than retained/grown unbounded.
        var oversized = new string('X', 5000);
        var frames1 = assembler.Append(oversized);
        Assert.Empty(frames1);

        // Because the oversized partial was discarded, this frame should NOT include
        // any of the previous 'X' characters.
        var frames2 = assembler.Append("PING\n\x04");
        Assert.Single(frames2);
        Assert.Equal("PING\n\x04", frames2[0]);
    }

    [Fact]
    public void InternalGloveMessage_NoEot_IsDroppedNotBuffered()
    {
        var assembler = new RxFrameAssembler();

        // Glove↔glove sync traffic (no EOT) must not be glued onto the next frame
        Assert.Empty(assembler.Append("EXECUTE_BUZZ:1:80:500"));
        Assert.Empty(assembler.Append("SYNC_ADJ:12345"));

        var frames = assembler.Append("PONG:\n\x04");
        Assert.Single(frames);
        Assert.Equal("PONG:\n\x04", frames[0]);
    }

    [Fact]
    public void InternalPrefix_MidFrame_IsNotDropped()
    {
        var assembler = new RxFrameAssembler();

        // A continuation chunk that happens to start with an internal prefix must
        // still be buffered — the filter only applies at frame start.
        Assert.Empty(assembler.Append("SESSION_STATUS:RUNNING\nREASON:"));
        var frames = assembler.Append("PING_LOST\n\x04");

        Assert.Single(frames);
        Assert.Equal("SESSION_STATUS:RUNNING\nREASON:PING_LOST\n\x04", frames[0]);
    }

    [Fact]
    public void ReassembledFrame_ParsesToMergedCommandResponse()
    {
        var assembler = new RxFrameAssembler();
        assembler.Append("COMMAND:INFO\nCOMMAND:BAT");
        var frames = assembler.Append("TERY\n\x04");

        var response = CommandResponse.Parse(frames[0]);

        Assert.Contains("INFO", response.GetAllStrings("COMMAND"));
        Assert.Contains("BATTERY", response.GetAllStrings("COMMAND"));
    }
}
