namespace lib.remnant2.analyzer.Model;

// Represents part of the data from a single save_N.sav: either adventure data or campaign data
public class RolledWorld
{
    public RolledWorld()
    {
        Ward13 = new(this) { Locations = [ Location.Ward13 ] };
    }
    public List<Zone> Zones;
    public List<string> QuestInventory;
    public Zone Ward13;
    public List<Zone> AllZones => [ Ward13,..Zones ];

    public bool CanGetItem(string item)
    {
        return AllZones.Any(x => x.CanGetItem(item));
    }
}
