# Remnant 2 save analyzer library

This library piggy backs on the low level <https://github.com/AndrewSav/lib.remnant2.saves> and provide a limited but easier to use interface for Remnant 2 save files. It was created as a middle level library to be used from a save manager UI.

Currently following methods are supplied:

- `public static string GetProfileStringCombined(string? folderPath = null)` - take saves folder path and returns a single string describing all the characters, e.g. `Medic, Alchemist (578), Explorer, Invader (1085), Summoner, Challenger (227)`. Here are 3 character are described, for each primary and secondary archetypes are given and an ever increasing number that goes up as the character acquires more in game.
- `public static string[] GetProfileStrings(string? folderPath = null)` - same as above but each character is an array item.
- `public static void Export(string targetFolder, string? folderPath, bool exportCopy, bool exportDecoded, bool exportJson)` - exports saves from given saves folder to a given target folder. The Boolean flags govern what to export: the copy of the save, the decompressed save, or the save converted to `json` format.
- `public static Dataset Analyze(string? folderPath = null)` - this method returns a Dataset describing all characters, worlds and adventures at the given save path.

Here is the brief description is what included in the `Dataset`.

- Dataset has the list of all Characters and the zero based number representing currently active character slot, a couple of collections to help with debugging: parsing debug messages and performance traces, it also exposes the underlying save reader library data in case the above is not enough. 
- Character includes index, Profile, and SaveSlot. There is also an indicator to specify if Campaign or Adventure is currently active. SaveDateTime exposes the last written time of the save file so we could avoid re-loading it if it has not changed, it also exposes the underlying save reader library data in case the above is not enough. 
- Profile has information about character Inventory, Traits, Missing Items, mats for crafting, archetypes. There are also HasWormhole and HasFortuneGunter flags for checking Achron archetype attainability. The number of acquired items and missing items is also provided. There are also trait rank, trait allocated points, gender, hardcore character indicator, power level, and item level for the character.
- SaveSlot has a RolledWord for campaign and for adventure. It also has a list of completed quests (for Quilt) and HasTree indicator, that tells if the seed for planted in Ward 13 for the save slot
- RolledWorld consists of lists of zones, e.g N'Erud or Yaesha, etc. It also keeps Quest Inventory, which is separate from the character regular inventory and holds quest items
- Zone consists of a list of locations, e.g. Morrow Parrish or Forbidden Grove
- Location contains: World stones, connection to neighbouring locations, indicators if it hold a trait book and simulacrum, a list of world drops and a list of loot groups. The latter two are populated from the list of lower level DropReferences, which is also kept here.
- A lot group contains LootItems situated at a single DropReference (e.g at a boss, injectable, side dungeon, vendor, etc.)
- LootItem is the lowest level. It represent an in-game item, such as a ring or a gun.

The library embeds `db.json` file which has the list of all in-game items and is used for the analysis above.
