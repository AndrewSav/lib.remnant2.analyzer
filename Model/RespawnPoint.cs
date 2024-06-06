using lib.remnant2.analyzer.Enums;

namespace lib.remnant2.analyzer.Model;

public class RespawnPoint(string name, RespawnPointType type)
{
    public string Name { get; set; } = name;
    public RespawnPointType Type { get; set; } = type;

    public override string ToString()
    {
        return $"{Name} ({Type})";
    }
}
