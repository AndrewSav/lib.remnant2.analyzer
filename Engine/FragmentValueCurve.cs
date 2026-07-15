namespace lib.remnant2.analyzer.Engine;

// Computes the value the game displays for a relic fragment, both for a standalone fragment at its own
// level (1...31, e.g. an owned inventory fragment) and when the fragment is leveled as a prism segment
// (+1...+10, mapped onto the 1...31 scale). The value follows a fixed cubic curve between MinValue and
// MaxValue, jumping to CustomMaxValue at max level:
//
//   standalone:     ValueAtFragmentLevel(level 1...31)
//   prism segment:  ValueAtPrismSegmentLevel(segmentLevel +1...+10) — maps onto the 1...31 scale, then evaluates
//   value:          fragmentLevel >= MaxFragmentLevel && CustomMaxValue != 0 ? CustomMaxValue
//                       : Lerp(MinValue, MaxValue, Curve(fragmentLevel / MaxFragmentLevel))
internal static class FragmentValueCurve
{
    // Maximum relic-fragment level.
    public const int MaxFragmentLevel = 31;

    // Maximum prism segment level (+1...+10).
    public const int MaxPrismSegmentLevel = 10;

    // --- the fixed fragment value curve ----------------------------------------------------------
    // A single cubic Bezier segment from (0,0) to (1,1). The two interior control points are
    // reconstructed below from the curve's key tangents and weights.
    private static readonly double P1X, P1Y, P2X, P2Y;

    static FragmentValueCurve()
    {
        const double v0 = 0.0, leaveTan = 0.6837475, leaveW = 0.20159891;
        const double v1 = 1.0, arriveTan = 2.4205856, arriveW = 0.7202413;
        const double t0 = 0.0, t1 = 1.0;

        double a1 = Math.Atan(leaveTan);
        P1X = (Math.Cos(a1) * leaveW + t0 - t0) / (t1 - t0);
        P1Y = Math.Sin(a1) * leaveW + v0;

        double a2 = Math.Atan(arriveTan);
        P2X = (-Math.Cos(a2) * arriveW + t1 - t0) / (t1 - t0);
        P2Y = -Math.Sin(a2) * arriveW + v1;
    }

    // Evaluate the value curve at normalized time t in [0,1].
    public static double Curve(double t)
    {
        if (t <= 0.0) return 0.0;
        if (t >= 1.0) return 1.0;
        // X(u) is monotonic on [0,1]; solve X(u) = t for u by bisection, then return Y(u). 64 iterations
        // converge to ~1e-19, far tighter than any display rounding.
        double lo = 0.0, hi = 1.0;
        for (int i = 0; i < 64; i++)
        {
            double mid = (lo + hi) * 0.5;
            if (Bezier(0.0, P1X, P2X, 1.0, mid) < t) lo = mid;
            else hi = mid;
        }
        return Bezier(0.0, P1Y, P2Y, 1.0, (lo + hi) * 0.5);
    }

    private static double Bezier(double p0, double p1, double p2, double p3, double u)
    {
        double mt = 1.0 - u;
        return mt * mt * mt * p0 + 3.0 * mt * mt * u * p1 + 3.0 * mt * u * u * p2 + u * u * u * p3;
    }

    // The value shown for a fragment at the given fragment level (1...MaxFragmentLevel). At max level it
    // shows CustomMaxValue (when set); otherwise it lerps MinValue...MaxValue along the curve.
    public static double ValueAtFragmentLevel(int fragmentLevel, double minValue, double maxValue, double customMaxValue)
    {
        if (fragmentLevel >= MaxFragmentLevel && customMaxValue != 0.0) return customMaxValue;
        double ct = Curve((double)fragmentLevel / MaxFragmentLevel);
        return minValue + ct * (maxValue - minValue);
    }

    // The value shown for a fragment leveled as a prism segment (+1...+MaxPrismSegmentLevel): the prism level is
    // mapped onto the 1...MaxFragmentLevel fragment scale (round to nearest, ties up), then evaluated.
    public static double ValueAtPrismSegmentLevel(int prismSegmentLevel, double minValue, double maxValue, double customMaxValue)
    {
        double lerp = MaxFragmentLevel * (double)(prismSegmentLevel - 1) / (MaxPrismSegmentLevel - 1);
        int fragmentLevel = (int)Math.Floor(lerp + 0.5);
        return ValueAtFragmentLevel(fragmentLevel, minValue, maxValue, customMaxValue);
    }
}
