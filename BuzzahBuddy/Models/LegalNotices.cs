namespace BuzzahBuddy.Models;

/// <summary>Legal wording that appears verbatim wherever the design system requires it. Never reword.</summary>
public static class LegalNotices
{
    /// <summary>
    /// Effective date shown on the Terms and Safety page. Bump it together with the page: the
    /// apps re-prompt when the date a person accepted differs from this one.
    /// </summary>
    public const string TermsEffectiveDate = "2026-11-10";

    public const string TermsUrl = "https://bluebuzzah.com/terms.html";
    public const string PrivacyUrl = "https://bluebuzzah.com/privacy.html";

    public const string MedicalDisclaimer =
        "BlueBuzzah devices are not FDA cleared or approved medical devices and not intended to diagnose, treat, cure, mitigate, or prevent any disease. The information provided through BlueBuzzah is for general wellness purposes only and not a substitute for professional medical advice, diagnosis, or treatment.";

    public const string ImplantedDeviceNotice =
        "Important: People with implanted electrodes from deep brain stimulation (DBS), heart pacemakers, or other internal electronic devices should take special precautions around all electronic devices, including BlueBuzzah. Consult your physician before use.";
}
