namespace lib.remnant2.analyzer.Enums;

// Outcome of a Prism path solver
public enum SolveOutcome
{
    Solved,     // solution was found
    Unsolvable, // exhausted search, this solver cannot find a solution
    TimedOut,   // time ran out before search was exhausted
}
