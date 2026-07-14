using BuzzahBuddy.Helpers;
using Xunit;

namespace BuzzahBuddy.Tests.Helpers;

public class IntMatchTests
{
    [Theory]
    [InlineData(20, "20")]
    [InlineData("100", "100")]
    [InlineData(0, "0")]
    [InlineData(-5, "-5")]
    public void Matches_EqualIntegers_ReturnsTrue(object value, string parameter)
    {
        Assert.True(IntMatch.Matches(value, parameter));
    }

    [Theory]
    [InlineData(20, "40")]
    [InlineData("100", "1000")]
    public void Matches_DifferentIntegers_ReturnsFalse(object value, string parameter)
    {
        Assert.False(IntMatch.Matches(value, parameter));
    }

    [Fact]
    public void Matches_NullValue_ReturnsFalse()
    {
        Assert.False(IntMatch.Matches(null, "20"));
    }

    [Fact]
    public void Matches_NullParameter_ReturnsFalse()
    {
        Assert.False(IntMatch.Matches(20, null));
    }

    [Theory]
    [InlineData("abc", "20")]
    [InlineData(20, "abc")]
    [InlineData("", "")]
    public void Matches_NonIntegerInput_ReturnsFalse(object value, object parameter)
    {
        Assert.False(IntMatch.Matches(value, parameter));
    }
}
