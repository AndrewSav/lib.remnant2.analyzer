namespace lib.remnant2.analyzer.Model.Prism;

// Result of one roll evaluation; surfaced via PrismData (NextRoll / IsLegendaryRoll / NextRollPoolSize / NextSeed).
// NextSeed is the post-draw seed — becomes the prism's CurrentSeed when an offer is accepted, driving the next roll.
internal sealed record PrismRollResult(IReadOnlyList<PrismOffer> Offers, bool IsLegendaryRoll, int PoolSize, uint NextSeed)
{
    // The roll's offers as a compact "RowName+NextLevel/…" string for the SolveStep diagnostic trail.
    public string ToOffersString() => string.Join("/", Offers.Select(o => $"{o.RowName}+{o.NextLevel}"));
}
