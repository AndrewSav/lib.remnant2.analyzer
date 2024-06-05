namespace lib.remnant2.analyzer.Model;

public class RespawnPoint(string name, RespawnPoint.RespawnPointType type)
{
    public enum RespawnPointType
    {
        None,
        Waypoint,
        Checkpoint,
        ZoneTransition
    }

    public string Name { get; set; } = name;
    public RespawnPointType Type { get; set; } = type;

    public override string ToString()
    {
        return $"{Name} ({Type})";
    }
}
