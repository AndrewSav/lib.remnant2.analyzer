using lib.remnant2.analyzer.Enums;

namespace lib.remnant2.analyzer.Model;
public class RespawnPoint
{
    public RespawnPoint(string? name, RespawnPointType type)
    {
        Name = name;
        Type = type;
    }

    public string? Name { get; set; }
    public RespawnPointType Type { get; set; }
}
