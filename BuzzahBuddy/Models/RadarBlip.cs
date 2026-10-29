namespace BuzzahBuddy.Models;

/// <summary>How bright a radar blip is: full as the sweep passes it, then easing out.</summary>
public static class RadarBlip
{
    /// <summary>Degrees of sweep the afterglow lasts: about a second of a 2.5-second turn.</summary>
    public const double FadeDegrees = 144;

    /// <summary>0–1. Both angles are clockwise degrees in the same frame; any number of turns.</summary>
    public static double Glow(double sweepDegrees, double blipDegrees)
    {
        var since = ((sweepDegrees - blipDegrees) % 360 + 360) % 360;
        if (since >= FadeDegrees)
            return 0;
        var left = 1 - since / FadeDegrees;
        return left * left;
    }
}
