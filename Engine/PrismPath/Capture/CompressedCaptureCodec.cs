using lib.remnant2.analyzer.Enums;
using lib.remnant2.analyzer.Model.Prism;
using lib.remnant2.analyzer.Model.Prism.Plan;
using lib.remnant2.analyzer.Model.Prism.Capture;
using System.Buffers.Binary;
using System.IO.Compression;

namespace lib.remnant2.analyzer.Engine.PrismPath.Capture;

// The compact "RPC1" store format: inputs + decision script, with everything else replayed deterministically
// on decode. Four dot-joined base64url sections (state.goal.feed.plan). The first three sections are the
// *lookup key* — InputPrefix reproduces them from a solver call's exact inputs so a caller can match an
// existing stored entry before re-solving. State.Feed and FeedAvailability are canonicalized to
// OrderIdMap.SegmentOrder ascending on encode (their stored/insertion order carries no meaning), which is
// what makes that lookup key order-independent; Slots and Goal.Segments keep their stored sequence, since
// position there is meaningful. Decode reverses all of this: it rebuilds the inputs, replays the build via
// PrismPlanMapper.Replay, and re-wraps through CaptureCodec.FromRun, so the decoded capture matches the readable
// form by construction.
public static class CompressedCaptureCodec
{
    public const string Prefix = "RPC1:";

    public static string Encode(PlanCapture capture) =>
        InputPrefix(capture.State, capture.Goal, capture.FeedAvailability) + EncodePlanSection(capture);

    // The lookup/match key: state + goal + feed availability, with no result-dependent data.
    public static string InputPrefix(CaptureState state, CaptureGoal goal, Dictionary<string, int> feedAvailability) =>
        $"{Prefix}{ToBase64Url(EncodeState(state))}.{ToBase64Url(EncodeGoal(goal))}.{ToBase64Url(EncodeFeed(feedAvailability))}.";

    // Rebuild a full PlanCapture from an Encode string. Reconstructs the inputs, replays the build through
    // PrismPlanMapper.Replay, and re-wraps via CaptureCodec.FromRun — so the decoded capture equals the readable form
    // by construction. Any structural problem (missing/wrong prefix, section count != 4, bad base64url,
    // out-of-range ids/levels/picks, trailing bytes, a re-derived legendary re-roll count that disagrees with
    // the stored one) throws FormatException with its cause preserved.
    public static PlanCapture Decode(string encoded)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(encoded);
            if (!encoded.StartsWith(Prefix, StringComparison.Ordinal))
                throw new FormatException("Not an RPC1 capture: missing 'RPC1:' prefix.");

            string[] sections = encoded[Prefix.Length..].Split('.');
            if (sections.Length != 4)
                throw new FormatException($"An RPC1 capture has 4 dot-separated sections; found {sections.Length}.");

            PrismState state = DecodeState(FromBase64Url(sections[0]));
            (List<string> goalSegments, string? legendary) = DecodeGoal(FromBase64Url(sections[1]));
            Dictionary<string, int> availability = DecodeFeed(FromBase64Url(sections[2]));
            (PlanOutcome outcome, long elapsedMs, int legendaryRerolls, List<int> picks, List<PlanDecisionFeed> feeds) =
                DecodePlan(FromBase64Url(sections[3]));

            PrismPlan plan = PrismPlanMapper.Replay(state, picks, feeds, legendary, outcome,
                                                    TimeSpan.FromMilliseconds(elapsedMs));

            // Determinism check: the legendary tail isn't stored, only replayed. If the re-derived re-roll count
            // disagrees with the stored one, the build script didn't reproduce the same +50-gate seed — the store
            // is inconsistent, not merely stale.
            if (plan.LegendaryRerolls != legendaryRerolls)
                throw new FormatException(
                    $"Decoded plan re-derives {plan.LegendaryRerolls} legendary re-rolls, but the capture stored {legendaryRerolls}.");

            return CaptureCodec.FromRun(state, goalSegments, legendary, availability, plan);
        }
        catch (FormatException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new FormatException("Malformed RPC1 capture.", ex);
        }
    }

    private static byte[] EncodeState(CaptureState state)
    {
        ByteWriter w = new();

        Span<byte> seed = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(seed, state.Seed);
        w.WriteBytes(seed);

        w.WriteVarint((ulong)state.Slots.Count);
        foreach (CaptureSlot slot in state.Slots)
        {
            w.WriteByte((byte)OrderIdMap.SegmentOrder(slot.RowName));
            w.WriteByte((byte)slot.Level);
        }

        List<CaptureFeed> feed = [.. state.Feed.OrderBy(f => OrderIdMap.SegmentOrder(f.RowName))];
        w.WriteVarint((ulong)feed.Count);
        foreach (CaptureFeed f in feed)
        {
            w.WriteByte((byte)OrderIdMap.SegmentOrder(f.RowName));
            w.WriteByte((byte)f.FedLevel);
        }

        return w.ToArray();
    }

    private static PrismState DecodeState(byte[] bytes)
    {
        ByteReader r = new(bytes);

        int seed = r.ReadInt32LittleEndian();

        int slotCount = checked((int)r.ReadVarint());
        List<PrismSlot> slots = new(slotCount);
        for (int i = 0; i < slotCount; i++)
        {
            string rowName = OrderIdMap.SegmentRowName(r.ReadByte());
            int level = r.ReadByte();
            slots.Add(new PrismSlot { RowName = rowName, Level = level });
        }

        int fedCount = checked((int)r.ReadVarint());
        List<PrismFeed> feed = new(fedCount);
        for (int i = 0; i < fedCount; i++)
        {
            string rowName = OrderIdMap.SegmentRowName(r.ReadByte());
            int fedLevel = r.ReadByte();
            feed.Add(new PrismFeed { RowName = rowName, FedLevel = fedLevel });
        }

        r.ExpectEnd();
        return new PrismState { Slots = slots, Feed = feed, CurrentSeed = seed };
    }

    private static byte[] EncodeGoal(CaptureGoal goal)
    {
        ByteWriter w = new();

        w.WriteVarint((ulong)goal.Segments.Count);
        foreach (string segment in goal.Segments)
            w.WriteByte((byte)OrderIdMap.SegmentOrder(segment));

        w.WriteByte((byte)(goal.Legendary is null ? 0 : OrderIdMap.LegendaryOrder(goal.Legendary)));

        return w.ToArray();
    }

    private static (List<string> Segments, string? Legendary) DecodeGoal(byte[] bytes)
    {
        ByteReader r = new(bytes);

        int segCount = checked((int)r.ReadVarint());
        List<string> segments = new(segCount);
        for (int i = 0; i < segCount; i++)
            segments.Add(OrderIdMap.SegmentRowName(r.ReadByte()));

        byte legendaryByte = r.ReadByte();
        string? legendary = legendaryByte == 0 ? null : OrderIdMap.LegendaryRowName(legendaryByte);

        r.ExpectEnd();
        return (segments, legendary);
    }

    private static byte[] EncodeFeed(Dictionary<string, int> feedAvailability)
    {
        ByteWriter w = new();

        List<KeyValuePair<string, int>> availability =
            [.. feedAvailability.OrderBy(kv => OrderIdMap.SegmentOrder(kv.Key))];
        w.WriteVarint((ulong)availability.Count);
        foreach ((string rowName, int level) in availability)
        {
            w.WriteByte((byte)OrderIdMap.SegmentOrder(rowName));
            w.WriteByte((byte)level);
        }

        return w.ToArray();
    }

    private static Dictionary<string, int> DecodeFeed(byte[] bytes)
    {
        ByteReader r = new(bytes);

        int count = checked((int)r.ReadVarint());
        Dictionary<string, int> availability = new(count);
        for (int i = 0; i < count; i++)
        {
            string rowName = OrderIdMap.SegmentRowName(r.ReadByte());
            int level = r.ReadByte();
            availability[rowName] = level;
        }

        r.ExpectEnd();
        return availability;
    }

    private static string EncodePlanSection(PlanCapture capture)
    {
        (List<int> picks, List<PlanDecisionFeed> feeds) = CaptureCodec.ExtractDecisions(capture);

        ByteWriter w = new();
        w.WriteByte(OutcomeByte(capture.Result.Outcome));
        w.WriteVarint((ulong)capture.Result.ElapsedMs);
        w.WriteVarint((ulong)capture.Result.LegendaryRerolls);
        w.WriteBytes(EncodeDecisionBlock(picks, feeds));

        return ToBase64Url(w.ToArray());
    }

    private static (PlanOutcome Outcome, long ElapsedMs, int LegendaryRerolls, List<int> Picks, List<PlanDecisionFeed> Feeds)
        DecodePlan(byte[] bytes)
    {
        ByteReader r = new(bytes);

        PlanOutcome outcome = OutcomeFromByte(r.ReadByte());
        long elapsedMs = checked((long)r.ReadVarint());
        int legendaryRerolls = checked((int)r.ReadVarint());

        (List<int> picks, List<PlanDecisionFeed> feeds) = DecodeDecisionBlock(r.ReadRemaining());
        return (outcome, elapsedMs, legendaryRerolls, picks, feeds);
    }

    // The flag byte + decision body: bit0 of the flag = the body is Brotli-compressed (set only when that is
    // strictly smaller than the raw body). EncodeDecisionBlock and DecodeDecisionBlock are inverses over this
    // block (the legendary tail is not part of it — Decode replays it from the goal's legendary target).
    internal static byte[] EncodeDecisionBlock(IReadOnlyList<int> picks, IReadOnlyList<PlanDecisionFeed> feeds)
    {
        byte[] body = EncodeBodyBytes(picks, feeds);
        byte[] compressed = BrotliCompress(body);
        bool useBrotli = compressed.Length < body.Length;

        ByteWriter w = new();
        w.WriteByte((byte)(useBrotli ? 1 : 0));
        w.WriteBytes(useBrotli ? compressed : body);
        return w.ToArray();
    }

    internal static (List<int> Picks, List<PlanDecisionFeed> Feeds) DecodeDecisionBlock(byte[] block)
    {
        ByteReader r = new(block);
        byte flags = r.ReadByte();
        byte[] rest = r.ReadRemaining();
        byte[] body = (flags & 1) != 0 ? BrotliDecompress(rest) : rest;
        return DecodeBodyBytes(body);
    }

    private static byte[] EncodeBodyBytes(IReadOnlyList<int> picks, IReadOnlyList<PlanDecisionFeed> feeds)
    {
        ByteWriter w = new();
        w.WriteVarint((ulong)picks.Count);
        foreach (int pick in picks)
            w.WriteByte((byte)pick);

        w.WriteVarint((ulong)feeds.Count);
        foreach (PlanDecisionFeed feed in feeds)
        {
            w.WriteVarint((ulong)feed.BeforeStep);
            w.WriteByte((byte)OrderIdMap.SegmentOrder(feed.RowName));
            w.WriteByte((byte)feed.FedLevel);
        }

        return w.ToArray();
    }

    private static (List<int> Picks, List<PlanDecisionFeed> Feeds) DecodeBodyBytes(byte[] body)
    {
        ByteReader r = new(body);

        int pickCount = checked((int)r.ReadVarint());
        List<int> picks = new(pickCount);
        for (int i = 0; i < pickCount; i++)
            picks.Add(r.ReadByte());

        int feedCount = checked((int)r.ReadVarint());
        List<PlanDecisionFeed> feeds = new(feedCount);
        for (int i = 0; i < feedCount; i++)
        {
            int beforeStep = checked((int)r.ReadVarint());
            string rowName = OrderIdMap.SegmentRowName(r.ReadByte());
            int fedLevel = r.ReadByte();
            feeds.Add(new PlanDecisionFeed(beforeStep, rowName, fedLevel));
        }

        r.ExpectEnd();
        return (picks, feeds);
    }

    private static byte OutcomeByte(string outcome) => Enum.Parse<PlanOutcome>(outcome) switch
    {
        PlanOutcome.Complete => 0,
        PlanOutcome.Incomplete => 1,
        PlanOutcome.Unsolved => 2,
        PlanOutcome other => throw new ArgumentOutOfRangeException(nameof(outcome), other, "Unknown PlanOutcome."),
    };

    private static PlanOutcome OutcomeFromByte(byte b) => b switch
    {
        0 => PlanOutcome.Complete,
        1 => PlanOutcome.Incomplete,
        2 => PlanOutcome.Unsolved,
        _ => throw new FormatException($"Unknown outcome byte {b}."),
    };

    private static byte[] BrotliCompress(byte[] data)
    {
        using MemoryStream output = new();
        using (BrotliStream brotli = new(output, CompressionLevel.SmallestSize, leaveOpen: true))
            brotli.Write(data, 0, data.Length);
        return output.ToArray();
    }

    private static byte[] BrotliDecompress(byte[] data)
    {
        using MemoryStream input = new(data);
        using BrotliStream brotli = new(input, CompressionMode.Decompress);
        using MemoryStream output = new();
        brotli.CopyTo(output);
        return output.ToArray();
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string section)
    {
        string padded = section.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(padded);
    }

    // Unsigned LEB128 byte accumulator shared by every section encoder.
    private sealed class ByteWriter
    {
        private readonly List<byte> _bytes = [];

        public void WriteByte(byte b) => _bytes.Add(b);

        public void WriteBytes(ReadOnlySpan<byte> bytes)
        {
            foreach (byte b in bytes)
                _bytes.Add(b);
        }

        // 7 bits per byte, low group first, high bit = continuation.
        public void WriteVarint(ulong value)
        {
            do
            {
                byte b = (byte)(value & 0x7F);
                value >>= 7;
                if (value != 0) b |= 0x80;
                _bytes.Add(b);
            } while (value != 0);
        }

        public byte[] ToArray() => [.. _bytes];
    }

    // Checked reader over a decoded section — every read is bounds-guarded so a truncated or trailing-byte
    // section fails loudly (EndOfStream/FormatException) rather than silently mis-parsing.
    private sealed class ByteReader
    {
        private readonly byte[] _bytes;
        private int _pos;

        public ByteReader(byte[] bytes) => _bytes = bytes;

        public byte ReadByte()
        {
            if (_pos >= _bytes.Length) throw new EndOfStreamException("Unexpected end of section.");
            return _bytes[_pos++];
        }

        public int ReadInt32LittleEndian()
        {
            if (_pos + 4 > _bytes.Length) throw new EndOfStreamException("Unexpected end of section.");
            int value = BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(_pos, 4));
            _pos += 4;
            return value;
        }

        // 7 bits per byte, low group first, high bit = continuation. Guarded against a runaway (all-continuation)
        // encoding so malformed input throws rather than looping.
        public ulong ReadVarint()
        {
            ulong result = 0;
            int shift = 0;
            while (true)
            {
                if (shift > 63) throw new FormatException("Varint is too long.");
                byte b = ReadByte();
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
            }
        }

        public byte[] ReadRemaining()
        {
            byte[] rest = _bytes[_pos..];
            _pos = _bytes.Length;
            return rest;
        }

        public void ExpectEnd()
        {
            if (_pos != _bytes.Length) throw new FormatException("Unexpected trailing bytes in section.");
        }
    }
}
