namespace lib.remnant2.analyzer.Model;

public class Dataset
{
    public required List<Character> Characters;
    public int ActiveCharacterIndex;
    // Inform about data found in saves that we cannot find in the database
    // which may indicate that the database is out of date
    public required List<string> DebugMessages;
    // Tracks performance of Dataset creation
    public required Dictionary<string, TimeSpan> DebugPerformance;
}
