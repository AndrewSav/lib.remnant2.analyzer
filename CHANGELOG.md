# Changelog

## v0.0.42 (25 Jan 2026)
- Fix crash with Necklace of Flowing Life

## v0.0.40 (28 Jun 2025)
- Add Wallace and account-unlocked archetypes that he offers

## v0.0.39 (21 Jan 2025)
- Add HasRequiredMaterial propery to loot items, which is set if the Weapon/Mod has the required crafted material in the inventory
- Fixed a crash when an inventory item does not have an associated object in save data

## v0.0.38 (15 Jan 2025)
- Add Band of The Fanatic and Dandy Tooper IsLooted detection
- Add IsLooted detection for Executioner, Dreadful and all engrams found in the world (e.g Ritualist, Engeneer, etc)
- Reverted stamina cost fragment change from v0.0.32, as I cannot remember where I saw that fragment. It should not appear in up-to-date saves

## v0.0.37 (13 Jan 2025)
- Add Thaen fruit and Blood moon data

## v0.0.36 (13 Jan 2025)
- Fix Override Pin name and location

## v0.0.35 (12 Jan 2025)
- Band Of The Fanatic is now detected as present but missing prerequisite for Forlorn coast if you do not have the cultist outfit
- Added IsLooted detection for Faelin / Faerin sigil
- Improved detection for One True King Sigil
- Allow detection of Nimue's Ribbon, One True King Sigil, Quilted Heart, Void Heart, Anguish, Polygun, Trinity Crossbow, Genesis and Redeemer at Ward 13 vendors when they are unlocked as account award

## v0.0.34 (12 Jan 2025)
- Fixed a bunch of game pass issues
- Fixed a subtle issue with evaluation oreder which caused Nimue Ribbon to be detected incorrectly in case both dark and light potential hosts for the ribbon event injectable spawn

## v0.0.33 (12 Jan 2025)
- Add support for Microsoft Game pass saves

## v0.0.32 (11 Jan 2025)
- Fix Zohee's Ring description, add back stamina cost fragment

## v0.0.31 (4 Dec 2024)
- Fix a spelling mistake in items notes

## v0.0.30 (3 Dec 2024)
- Update dependencies
- Add warning if the save files game build is less than expected
- Suppress bogus naked armor warning

## v0.0.29 (15 Nov 2024)
- Suppress warning about unknow relic charge item
- Fix engram names displayed instead of archetype names

## v0.0.28 (2 Nov 2024)
- Fix Cessation Bulbel detection

## v0.0.27 (30 Oct 2024)
- Improved looted detection for Repair tool
- Fixed Mortal Coil not detecting when Forbidden Grove is rolled
- Fixed prerequisite detectction counting zero quantity items as present

## v0.0.26 (28 Oct 2024)
- Add DLC3 items

## v0.0.25 (12 Oct 2024)
- Fix a DLC3 crash

## v0.0.24 (20 July 2024)
- Added missing prerequisite of Savior to Corrupted Savior in the database

## v0.0.23 (20 July 2024)
- Fixed compatibility with changes in the saves library
- Added Relic Charges
- Fixed notes for Game Master's Pride
- Added check for 10 corrupted shards for corrupted weapon prerequisites

## v0.0.22 (2 July 2024)
- Updated dependencies

## v0.0.21 (2 July 2024)
- Fixed regression with connections showing x2 for non-Yaesha zones
- Formatting and typo fixes

## v0.0.20 (13 June 2024)
- Fixed a crash when a load out has one archetype only
- Fixed a crash on new character creation
- Added names for unremovable weapon mods and archetype skills

## v0.0.19 (8 June 2024)
- Added support for respawn points orther than Worldstones
- Added support for loadouts. Had to add non-removable weapon mods, archetype skills and relic fragments to the DB because of that. Outstanding: non-removable weapon mods and archetype skills have to be verified
- Added equipment
- Added concoctions, consumables, currencies, and remaining awards to the databases
- Make the number next to character archetype correspond to the number of aquired items instead of being abstract
- Add New/Favourite marker to inventory items
- Another pass of performance optimizations

## v0.0.18 (5 June 2024)
- Amended Fix Necklace of Flowing Life crash fix from yesterday to not crash on Void Heart

## v0.0.17 (4 June 2024)
- Fixed Necklace of Flowing Life crash

## v0.0.16 (3 June 2024)
- Added Huntress', Nimue's, Dran's and Root Walker's dreams consumables to craftable items

## v0.0.15 (3 June 2024)
- Added craftable items if character has materials in the inventory
- Added performance counters for loot groups

## v0.0.14 (3 June 2024)
- Use new release of the saves library with performance improvements
- Added Dran Dream item in ignore list for unknown items

## v0.0.13 (31 May 2024)
- Refactored GetAdventure/GetCompaign to a single method
- Refactored to replace store/finished in Zone class with a DropReference
- Bumped saves library version
- Added Anguish detection

## v0.0.12 (28 May 2024)
- Added last respawn point for characters
- Added correct detection of all outstanding items except Anguish
- Added inventory items count, for future improvements (e.g. consumables, corrupted items cost check)
- Typos and corrections in db.json

## v0.0.11 (17 May 2024)
- Re-tag botched build

## v0.0.10 (17 May 2024)
- Fixed a few database typos
- Fixed an issue where loading profile only was not updating profile correctly

## v0.0.9 (10 May 2024)
- Reworked logging and implemeted debug logging for prerequisites

## v0.0.8 (9 May 2024)
- Detect Resolute trait aquisition
- Improvements for challenge detection (Resolute, Participation Medal, Revivalist)
- Fixes for rare crashes when the save does not have an archetype or a campaign

## v0.0.7 (8 May 2024)
- Fix a crash

## v0.0.6 (8 May 2024)
- Detect which challanges can be achieved without rerolling
- Minor fixes and improvements

## v0.0.5 (6 May 2024)
- Add quests to the database, fix "challenge" spelling, 
- Improve support for looted items detection, when possible
- Add account awards, achievements, challanges, Cass shop, character level and gender, trait points, time played, difficuly, hardcore to returned data
- Split up the huge analyzer file into several files grouping up functions in similar areas, also broke down the huge analyzer method to smaller chunks

## v0.0.4 (4 May 2024)
- Add missing prerequisites for some corrupted weapons to the database


## v0.0.3 (3 May 2024)

- Add items from The Forgotten Kingom DLC
- Add warnings for items in player saves not found in the item database. The helps catch typo and general database maintenance
- Also display world drops name in the name column to be more in line with original RSG
- Performance improvements
- Add raw data to the Analyzer output in case the client requires something that is not in the parsed data
- Add internal performance metrics
- Minor refactoring to address Visual Studio / Resharper warnings
