namespace BuzzahBuddy.Views.Controls;

/// <summary>
/// One choice in a <see cref="SegmentedControl"/>. <paramref name="Spoken"/> is what screen
/// readers say instead of the label, for abbreviations ("500ms" as "500 milliseconds").
/// </summary>
public sealed record SegmentOption(int Value, string Label, string? Spoken = null);
