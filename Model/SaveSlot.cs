﻿using lib.remnant2.analyzer.Model.Mechanics;

namespace lib.remnant2.analyzer.Model;

// Represents data in a single save_N.sav
public class SaveSlot
{
    public required RolledWorld Campaign;
    public RolledWorld? Adventure;
    public required List<string> QuestCompletedLog;
    public required List<LootItem> CassShop;
    public TimeSpan? Playtime;
    public ThaenFruit? ThaenFruit;
}
