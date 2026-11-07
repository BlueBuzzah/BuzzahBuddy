using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

public class TermsGateTests
{
    [Fact]
    public void Current_date_accepted_does_not_prompt() =>
        Assert.False(TermsGate.NeedsAcceptance(LegalNotices.TermsEffectiveDate));

    [Fact]
    public void Older_date_prompts_again() =>
        Assert.True(TermsGate.NeedsAcceptance("2025-01-01"));

    [Fact]
    public void Nothing_stored_prompts() =>
        Assert.True(TermsGate.NeedsAcceptance(null));

    // Before the Terms existed, Welcome stored only a bool. Those installs have no accepted
    // date, so they see Welcome once more; the bool itself never counts as acceptance.
    [Fact]
    public void Legacy_bool_without_a_date_prompts() =>
        Assert.True(TermsGate.NeedsAcceptance(acceptedDate: null));

    [Fact]
    public void Effective_date_is_an_iso_date() =>
        Assert.True(DateOnly.TryParseExact(LegalNotices.TermsEffectiveDate, "yyyy-MM-dd", out _));
}
