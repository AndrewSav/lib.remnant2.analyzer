using lib.remnant2.analyzer.Enums;

namespace lib.remnant2.analyzer.Model;

// Represents part of the data from a single save_N.sav: either adventure data or campaign data
public class RolledWorld
{
    public RolledWorld()
    {
        Ward13 = new(this, null) { Locations = [ Location.GetWard13() ] };
    }
    public List<Zone> Zones = [];
    public required List<string> QuestInventory;
    public Zone Ward13;
    public List<Zone> AllZones => [ Ward13,..Zones ];

    private Character? _character;
    public Character ParentCharacter
    {
        get => _character ?? throw new InvalidOperationException("Character is not set for RolledWorld, this is unexpected");
        set => _character = value;
    }

    public required string Difficulty;
    public TimeSpan? Playtime;
    public string? RespawnPoint;
    public RespawnPointType RespawnPointType;

    public List <LootGroup> AdditionalItems =[];

    public bool CanGetItem(string item)
    {
        return AllZones.Any(x => x.CanGetItem(item));
    }

    public bool CanGetAccountAward(string award)
    {
        string[] challengeIds = ItemDb.GetItemById(award).Properties["Challenge"].Split(',').Select(x => x.Trim()).ToArray();
        return challengeIds.All(id => ParentCharacter.Profile.IsObjectiveAchieved(id) || CanGetChallenge(id));
    }

    public bool CanGetChallenge(string challenge)
    {
        return CustomScripts.CanGetChallenge(this, challenge);
    }
    
    public bool IsCampaign => Zones.Exists(x => x.Name == "Labyrinth");
}
