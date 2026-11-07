namespace BuzzahBuddy.Models;

/// <summary>Decides whether Welcome must be shown: the accepted Terms date must match the current one.</summary>
public static class TermsGate
{
    /// <param name="acceptedDate">The Terms effective date the person accepted, or null when none is stored.</param>
    public static bool NeedsAcceptance(string? acceptedDate) => acceptedDate != LegalNotices.TermsEffectiveDate;
}
