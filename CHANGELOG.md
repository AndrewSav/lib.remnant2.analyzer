# Changelog

## v0.0.11 (unreleased)
- Check if items can be obtained based on their prerequisites - work in progress

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