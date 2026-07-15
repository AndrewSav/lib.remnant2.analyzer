using System.Diagnostics;
using lib.remnant2.analyzer.Enums;
using lib.remnant2.analyzer.Model.Prism;
using lib.remnant2.analyzer.Model.Prism.Plan;

namespace lib.remnant2.analyzer.Engine.PrismPath;

// The lex solver's legendary tail search — its target-aware half, mirroring how StagedSolver splits into
// OpeningSearch + ClimbSearch. Re-searches a solved natural build's ENDING, from the handoff just before its
// last fuse, to minimise the cleanse re-roll count k: sweep the pair's overshoot, fuse, refill the freed slot,
// then min-k level-to-50; a multi-fusion pass overshoots each earlier fusion and lets LexSearch finish the
// build from that state. Everything is a pure function of the handoff state, so the delegated build stays
// plan-stable; the wrapper keeps the natural plan whenever steering can't do better.
internal static class LegendaryTailSearch
{
    // A steered plan: the full step sequence from the start to the +50 gate, and its re-roll count k.
    internal sealed record Steered(IReadOnlyList<SolveStep> Steps, int Rerolls);

    // The whole steering strategy, tiered by cost: the cheap last-fuse pass always runs (its ending is directly
    // constructible — nothing after the last fuse needs searching); only if it still needs re-rolls does the
    // expensive earlier-fusions pass get its turn (each earlier ending must be re-searched by LexSearch).
    // Returns the best steered plan found, or null when no tail can be built at all.
    internal static Steered? Steer(
        LexSearch engine,
        IReadOnlyList<SolveStep> script,
        PrismState start,
        Dictionary<string, PrismRollRow> byName,
        IReadOnlyList<string> caredSingles,
        Dictionary<string, int> startFeed,
        IReadOnlyDictionary<string, int> feedLevels,
        string legendary,
        double budgetMs,
        long startTimestamp,
        CancellationToken cancel,
        Action<Steered>? onImprove = null)
    {
        Steered? best = SteerLastFuse(script, start, byName, caredSingles, feedLevels, legendary, budgetMs, startTimestamp, cancel);
        if (best is not null) onImprove?.Invoke(best);          // the last-fuse best is the first streamed improvement
        if (best is { Rerolls: > 0 })
            best = TryEarlierFusions(engine, script, start, byName, caredSingles, startFeed, feedLevels, legendary, budgetMs, startTimestamp, cancel, best, onImprove);
        return best;
    }

    // Steer a build's LAST fuse: replay `script` from `start` once, snapshotting the state just before every
    // candidate handoff — the tail begins at the LAST fuse (so its pair can be overshot), or where level-to-50
    // starts when no fuse lies ahead (F=0, or a mid-build start already past its last fuse — nudge only; the
    // script's fuse count can be less than the goal's F, so the handoff comes from the script, not the goal).
    // Returns the full plan (prefix + steered tail) + its k, or null when the script has no tail to steer.
    private static Steered? SteerLastFuse(
        IReadOnlyList<SolveStep> script,
        PrismState start,
        Dictionary<string, PrismRollRow> byName,
        IReadOnlyList<string> caredSingles,
        IReadOnlyDictionary<string, int> feedLevels,
        string legendary,
        double budgetMs,
        long startTimestamp,
        CancellationToken cancel)
    {
        Dictionary<string, int> segments = start.Slots.ToDictionary(s => s.RowName, s => s.Level);
        Dictionary<string, int> feed = start.Feed.ToDictionary(f => f.RowName, f => f.FedLevel);
        // one snapshot pair serves both handoff candidates: the level-50 snapshot fires at most once and only
        // before any fuse, and every fuse overwrites — so the pair always holds the winning handoff's state.
        Dictionary<string, int>? segmentsHandoff = null, feedHandoff = null;
        int? atFuse = null, atFirstLevel50 = null;
        for (int i = 0; i < script.Count; i++)
        {
            SolveStep st = script[i];
            if (st.Action == "feed") { feed[st.Item] = st.SegmentLevel; continue; }
            if (st.Action == "fuse")
            {
                (segmentsHandoff, feedHandoff, atFuse) = (new(segments), new(feed), i);   // keep overwriting — the last fuse wins
                PrismRollRow row = byName[st.Item];
                segments.Remove(row.FusionPart1!); segments.Remove(row.FusionPart2!); segments[st.Item] = 1;
                continue;
            }
            if (atFuse is null && atFirstLevel50 is null && st.Action == "level" && segments.Count == 5)
                (segmentsHandoff, feedHandoff, atFirstLevel50) = (new(segments), new(feed), i);
            segments[st.Item] = st.SegmentLevel;
        }
        if ((atFuse ?? atFirstLevel50) is not { } split) return null;   // no fuse and no level-to-50 — nothing to steer
        uint seed = script[split].Seed;

        // a fuse split overshoots that fusion; a level split is nudge-only.
        string? lastFusion = script[split].Action == "fuse" ? script[split].Item : null;
        string? p1 = lastFusion is not null ? byName[lastFusion].FusionPart1 : null;
        string? p2 = lastFusion is not null ? byName[lastFusion].FusionPart2 : null;

        // Steer the tail from the handoff (lastFusion null => F=0 or a start already past its last fuse — nudge
        // only). Sweeps the overshoot amounts o and keeps the steered tail with the fewest re-rolls, then the least
        // overshoot. Returns null only if no tail can be built at all (shouldn't happen from a solved build)
        int maxOvershoot = lastFusion is null ? 0 : 10 - segmentsHandoff!.GetValueOrDefault(p1!, 10) + (10 - segmentsHandoff!.GetValueOrDefault(p2!, 10));

        SteeredTail? best = null;
        for (int o = 0; o <= maxOvershoot; o++)
        {
            // o = 0 always runs (the baseline valid plan); the sweep past it shares the Plan deadline — on
            // timeout, keep the best found so far.
            if (o > 0)
            {
                if (best is { Rerolls: 0 }) break;                        // can't beat zero re-rolls
                if (Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds > budgetMs) break;
                cancel.ThrowIfCancellationRequested();
            }
            SteeredTail? trial = BuildTail(segmentsHandoff!, feedHandoff!, seed, lastFusion, p1, p2, caredSingles, feedLevels, legendary, o, cancel);
            if (trial is null) continue;                                  // this overshoot amount isn't offer-reachable
            if (best is null || trial.Rerolls < best.Rerolls || (trial.Rerolls == best.Rerolls && trial.Overshoot < best.Overshoot))
                best = trial;
        }

        if (best is null) return null;
        return new Steered([.. script.Take(split), .. best.Steps], best.Rerolls);
    }

    // Multi-fusion pass: for each fusion BEFORE the last, overshoot its pair by o = 1..max, finish the build with
    // LexSearch from the overshoot point, then steer that build's last fuse. Keeps only a STRICTLY lower k —
    // deliberately stricter than the last-fuse sweep's least-overshoot tie-break: an overshoot tie must not churn
    // the plan onto the more invasive earlier-fusion rebuild for no k gain.
    private static Steered TryEarlierFusions(
        LexSearch engine,
        IReadOnlyList<SolveStep> script,
        PrismState start,
        Dictionary<string, PrismRollRow> byName,
        IReadOnlyList<string> caredSingles,
        Dictionary<string, int> startFeed,
        IReadOnlyDictionary<string, int> feedLevels,
        string legendary,
        double budgetMs,
        long startTimestamp,
        CancellationToken cancel,
        Steered best,
        Action<Steered>? onImprove = null)
    {
        List<int> fuseIdx = [];
        for (int i = 0; i < script.Count; i++)
            if (script[i].Action == "fuse") fuseIdx.Add(i);
        if (fuseIdx.Count < 2) return best;   // one fusion => nothing earlier to overshoot

        for (int fi = 0; fi < fuseIdx.Count - 1; fi++)   // skip the last fuse — SteerLastFuse already covers it
        {
            int j = fuseIdx[fi];
            // reconstruct the state before fuse j + the fragments fed up to there (so the finishing solve doesn't re-feed)
            Dictionary<string, int> seg = start.Slots.ToDictionary(s => s.RowName, s => s.Level);
            Dictionary<string, int> feed = new(startFeed);
            HashSet<string> fed = [];
            for (int i = 0; i < j; i++)
            {
                SolveStep st = script[i];
                if (st.Action == "feed") { feed[st.Item] = st.SegmentLevel; fed.Add(st.Item); continue; }
                if (st.Action == "fuse")
                {
                    PrismRollRow row = byName[st.Item];
                    seg.Remove(row.FusionPart1!); seg.Remove(row.FusionPart2!); seg[st.Item] = 1;
                }
                else seg[st.Item] = st.SegmentLevel;
            }
            uint seedJ = script[j].Seed;
            PrismRollRow fusion = byName[script[j].Item];
            string p1 = fusion.FusionPart1!, p2 = fusion.FusionPart2!;
            int maxO = 10 - seg.GetValueOrDefault(p1, 10) + (10 - seg.GetValueOrDefault(p2, 10));
            Dictionary<string, int> availReduced = feedLevels.Where(kv => !fed.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);

            for (int o = 1; o <= maxO; o++)
            {
                if (best.Rerolls == 0) return best;
                double remaining = budgetMs - Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
                if (remaining <= 0) return best;
                cancel.ThrowIfCancellationRequested();

                (List<SolveStep> Steps, Dictionary<string, int> Segments, uint Seed)? ov =
                    OvershootPair(seg, feed, seedJ, p1, p2, o);
                if (ov is null) continue;

                LexSearch.Result r2 = engine.SearchFrom(ov.Value.Segments, feed, ov.Value.Seed, availReduced, remaining, cancel: cancel);
                if (r2.Status != SolveOutcome.Solved || r2.Script is null) continue;

                PrismState postState = new()
                {
                    CurrentSeed = unchecked((int)ov.Value.Seed),
                    Slots = [.. ov.Value.Segments.Select(kv => new PrismSlot { RowName = kv.Key, Level = kv.Value })],
                    Feed = [.. feed.Select(kv => new PrismFeed { RowName = kv.Key, FedLevel = kv.Value })],
                };
                Steered? steered = SteerLastFuse(r2.Script, postState, byName, caredSingles, availReduced, legendary, budgetMs, startTimestamp, cancel);
                if (steered is not null && steered.Rerolls < best.Rerolls)
                {
                    best = steered with { Steps = [.. script.Take(j), .. ov.Value.Steps, .. steered.Steps] };
                    onImprove?.Invoke(best);
                }
            }
        }
        return best;
    }

    // --- the steered tail — pure functions of the handoff state, so the delegated build stays plan-stable ---

    // A steered tail from the handoff to the +50 gate: its steps, the overshoot spent, and the re-roll count k.
    private sealed record SteeredTail(IReadOnlyList<SolveStep> Steps, int Overshoot, int Rerolls);

    // Overshoot a fusion's present pair by `o` levels (gate-safe), WITHOUT fusing — the shared overshoot walk
    // both passes use: BuildTail runs it then fuses the pair itself; the multi-fusion pass runs it on an earlier
    // fusion and hands the returned state to LexSearch to finish the build and steer ITS last fuse.
    // Returns the emitted steps + the resulting segments and seed (feed is unchanged), or null if the offer
    // stream can't deliver `o` overshoots.
    private static (List<SolveStep> Steps, Dictionary<string, int> Segments, uint Seed)? OvershootPair(
        IReadOnlyDictionary<string, int> segments,
        IReadOnlyDictionary<string, int> feed,
        uint seed,
        string part1,
        string part2,
        int o)
    {
        Dictionary<string, int> s = new(segments);
        uint sd = seed;
        List<SolveStep> steps = [];
        int done = 0;
        while (done < o)
        {
            Debug.Assert(steps.Count <= 300, "overshoot loop ran past the roll bound — every iteration must level a segment or exit");
            PrismRollResult r = PrismRollEvaluator.Evaluate(s, feed, sd);
            string? part = s.GetValueOrDefault(part1, 99) < 10 && r.Offers.Any(y => y.RowName == part1) ? part1
                         : s.GetValueOrDefault(part2, 99) < 10 && r.Offers.Any(y => y.RowName == part2) ? part2 : null;
            int prism = s.Values.Sum();
            if (part is not null)
            {
                steps.Add(SolveStep.Of(sd, "level", part, s[part] + 1, prism, "legendary:overshoot", r.ToOffersString()));
                s[part]++; sd = r.NextSeed; done++;
                continue;
            }
            string? survive = SafeSurvive(s, r, part1, part2);
            if (survive is null) return null;
            steps.Add(SolveStep.Of(sd, "survive", survive, s[survive] + 1, prism, "legendary:overshoot", r.ToOffersString()));
            s[survive]++; sd = r.NextSeed;
        }
        return (steps, s, sd);
    }

    // One steered tail: overshoot the pair by `o`, fuse, refill the freed slot, then min-k level-to-50. Emits real
    // SolveSteps. Returns null if the offer stream can't deliver `o` overshoots or the build dead-ends.
    private static SteeredTail? BuildTail(
        IReadOnlyDictionary<string, int> segments,
        IReadOnlyDictionary<string, int> feed,
        uint seed,
        string? lastFusion,
        string? part1,
        string? part2,
        IReadOnlyList<string> caredSingles,
        IReadOnlyDictionary<string, int> feedLevels,
        string target,
        int o,
        CancellationToken cancel)
    {
        Dictionary<string, int> s = new(segments);
        Dictionary<string, int> f = new(feed);
        uint sd = seed;
        List<SolveStep> steps = [];
        int overshoot = 0;

        void Take(PrismRollResult r, string alias, string item, int level, string phase)
        {
            steps.Add(SolveStep.Of(sd, alias, item, level, s.Values.Sum(), phase, r.ToOffersString()));
            sd = r.NextSeed;
        }

        if (lastFusion is not null)
        {
            // overshoot phase — level the pair past +5 (OvershootPair), then fuse it below.
            (List<SolveStep> Steps, Dictionary<string, int> Segments, uint Seed)? ov = OvershootPair(s, f, sd, part1!, part2!, o);
            if (ov is null) return null;
            steps.AddRange(ov.Value.Steps);
            s = ov.Value.Segments;
            sd = ov.Value.Seed;
            overshoot = o;

            // fuse phase — wait for the fusion offer, surviving elsewhere meanwhile (never on the pair, to keep `o` exact)
            while (true)
            {
                Debug.Assert(steps.Count <= 300, "fuse-wait loop ran past the roll bound — every iteration must level a segment or exit");
                PrismRollResult r = PrismRollEvaluator.Evaluate(s, f, sd);
                if (r.Offers.Any(y => y.RowName == lastFusion))
                {
                    Take(r, "fuse", lastFusion, 1, "legendary:fuse");
                    s.Remove(part1!); s.Remove(part2!); s[lastFusion] = 1;
                    break;
                }
                string? survive = SafeSurvive(s, r, part1!, part2!);
                if (survive is null) return null;
                Take(r, "survive", survive, s[survive] + 1, "legendary:fuse");
                s[survive]++;
            }

            // refill — the one freed slot: a still-missing cared single (goal-required, so it takes priority) else a
            // wildcard single. Exactly one slot frees per fuse, so at most one is due. Feed the cared single up front
            // if lex left it unfed (its feed fell in the tail this replaces), so it surfaces fast instead of stalling
            // the tail on the base offer rate (mirrors the climb's refill feeding).
            string? refillCared = caredSingles.FirstOrDefault(c => !s.ContainsKey(c));
            if (refillCared is not null && feedLevels.TryGetValue(refillCared, out int fl) && f.GetValueOrDefault(refillCared) == 0)
            {
                int fed = Math.Min(PrismFeedLevel.Max, f.GetValueOrDefault(refillCared) + fl);
                f[refillCared] = fed;
                steps.Add(SolveStep.Of(sd, "feed", refillCared, fed, s.Values.Sum(), "legendary:refill", ""));
            }
            while (s.Count < 5)
            {
                Debug.Assert(steps.Count <= 300, "refill loop ran past the roll bound — every iteration must level a segment or exit");
                PrismRollResult r = PrismRollEvaluator.Evaluate(s, f, sd);
                string? hit = refillCared is not null
                    ? r.Offers.Any(y => y.RowName == refillCared) ? refillCared : null
                    : r.Offers.Where(y => y.NextLevel == 1 && y.Kind != PrismOfferKind.Fusion
                                          && !caredSingles.Contains(y.RowName) && !s.ContainsKey(y.RowName))
                              .Select(y => y.RowName).FirstOrDefault();
                if (hit is not null) { Take(r, "refill", hit, 1, "legendary:refill"); s[hit] = 1; continue; }
                string? survive = r.Offers.Where(y => s.ContainsKey(y.RowName) && s[y.RowName] < 10)
                                          .Select(y => y.RowName).FirstOrDefault();
                if (survive is null) return null;
                Take(r, "survive", survive, s[survive] + 1, "legendary:refill");
                s[survive]++;
            }
        }

        // min-k level-to-50 — the free residue nudge; emit the levels in the chosen order
        (int Rerolls, List<string> Order)? tail = LevelTo50.MinK(s, sd, f, target, cancel);
        if (tail is null) return null;
        foreach (string row in tail.Value.Order)
        {
            PrismRollResult r = PrismRollEvaluator.Evaluate(s, f, sd);
            Take(r, "level", row, s[row] + 1, "legendary:final");
            s[row]++;
        }
        return new SteeredTail(steps, overshoot, tail.Value.Rerolls);
    }

    // The tail's gate-safety guard, stricter than the exact rule ClimbSearch and LexSearch apply (forbid
    // only the pick completing 5×+10 on an incomplete build): survives only level a non-part segment currently
    // below +9, so no non-part ever reaches +10 pre-fuse and the gate can't trip regardless of the pair (which
    // overshoot may hold at +10/+10). It also excludes the pair itself, keeping the overshoot count `o` exact.
    private static string? SafeSurvive(Dictionary<string, int> s, PrismRollResult r, string part1, string part2) =>
        r.Offers.Where(y => s.ContainsKey(y.RowName) && y.RowName != part1 && y.RowName != part2 && s[y.RowName] < 9)
                .Select(y => y.RowName).FirstOrDefault();

    // Total grind XP of a step sequence replayed from `start` (each level-up costs 5000 + 300 * display-before).
    internal static long ScriptXp(IReadOnlyList<SolveStep> steps, PrismState start, Dictionary<string, PrismRollRow> byName)
    {
        Dictionary<string, int> seg = start.Slots.ToDictionary(s => s.RowName, s => s.Level);
        long xp = 0;
        foreach (SolveStep st in steps)
        {
            if (st.Action == "feed") continue;
            xp += 5000 + 300L * seg.Values.Sum();
            if (st.Action == "fuse")
            {
                PrismRollRow row = byName[st.Item];
                seg.Remove(row.FusionPart1!); seg.Remove(row.FusionPart2!); seg[st.Item] = 1;
            }
            else seg[st.Item] = st.SegmentLevel;
        }
        return xp;
    }
}
