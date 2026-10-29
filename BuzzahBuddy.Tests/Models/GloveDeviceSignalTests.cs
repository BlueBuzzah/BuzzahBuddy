using BuzzahBuddy.Helpers;
using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

/// <summary>A found glove's signal reads as one of three words with the matching Lucide bars.</summary>
public class GloveDeviceSignalTests
{
    [Theory]
    [InlineData(-45, "Signal Strong", LucideIcons.SignalHigh)]
    [InlineData(-50, "Signal Strong", LucideIcons.SignalHigh)]
    [InlineData(-51, "Signal OK", LucideIcons.SignalMedium)]
    [InlineData(-70, "Signal OK", LucideIcons.SignalMedium)]
    [InlineData(-71, "Signal Weak", LucideIcons.SignalLow)]
    public void Rssi_maps_to_words_and_bars(int rssi, string text, string glyph)
    {
        var device = new GloveDevice { SignalStrength = rssi };
        Assert.Equal(text, device.SignalStrengthDisplay);
        Assert.Equal(glyph, device.SignalGlyph);
    }
}
