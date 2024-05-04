# Changelog

## v0.0.5 (unreleased)
- Add quests to the database, fix "challenge" spelling, 
- Improve support for looted items detection, when possible
- Add account awards, achievements, challanges, Cass shop, character level and gender, trait points, time played, difficuly, hardcore to returned data
- Add ability to hide items that cannot be obtained without rerolling - work in progress
- Detect which challanges can be achieved without rerolling

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
