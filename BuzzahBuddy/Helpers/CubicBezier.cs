namespace BuzzahBuddy.Helpers;

/// <summary>CSS cubic-bezier timing functions, for the design system's easing tokens.</summary>
public static class CubicBezier
{
    /// <summary><c>ease-standard</c>: cubic-bezier(0.2, 0, 0, 1).</summary>
    public static double Standard(double x) => Ease(x, 0.2, 0, 0, 1);

    /// <summary>Progress y for time x (both 0–1) on the curve through (x1, y1) and (x2, y2).</summary>
    public static double Ease(double x, double x1, double y1, double x2, double y2)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;

        static double Bezier(double t, double p1, double p2) =>
            3 * (1 - t) * (1 - t) * t * p1 + 3 * (1 - t) * t * t * p2 + t * t * t;
        static double Slope(double t, double p1, double p2) =>
            3 * (1 - t) * (1 - t) * p1 + 6 * (1 - t) * t * (p2 - p1) + 3 * t * t * (1 - p2);

        // Solve x(t) = x with Newton's method from t = x; bisect if the slope flattens out.
        var t = x;
        for (var i = 0; i < 8; i++)
        {
            var slope = Slope(t, x1, x2);
            if (Math.Abs(slope) < 1e-6)
                break;
            t -= (Bezier(t, x1, x2) - x) / slope;
        }
        if (t < 0 || t > 1 || Math.Abs(Bezier(t, x1, x2) - x) > 1e-5)
        {
            double lo = 0, hi = 1;
            for (var i = 0; i < 40; i++)
            {
                t = (lo + hi) / 2;
                if (Bezier(t, x1, x2) < x) lo = t; else hi = t;
            }
        }
        return Bezier(t, y1, y2);
    }
}
