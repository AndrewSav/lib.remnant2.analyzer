using Newtonsoft.Json.Linq;

namespace lib.remnant2.analyzer.Model;

public class Dataset
{
    public required List<Character> Characters;
    public int ActiveCharacterIndex;
}
