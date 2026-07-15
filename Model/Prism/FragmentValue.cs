namespace lib.remnant2.analyzer.Model.Prism;

// A relic fragment's computed stat value plus its unit. The library computes the number and reports the
// unit; rendering it (sign, decimals, suffix) is the consumer's job. Unit is the fragment's db.json unit
// token — in practice "%", "cm", "/s", or "" (flat / no suffix).
public readonly record struct FragmentValue(double Value, string Unit);
