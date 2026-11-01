using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

public class DeviceNounTests
{
    [Theory]
    [InlineData(HostDevice.Phone, "phone")]
    [InlineData(HostDevice.IPad, "iPad")]
    [InlineData(HostDevice.Tablet, "tablet")]
    public void For_NamesTheHostDevice(HostDevice host, string noun) =>
        Assert.Equal(noun, DeviceNoun.For(host));

    [Fact]
    public void Current_IsPhoneUntilTheAppSetsIt() => Assert.Equal("phone", DeviceNoun.Current);
}
