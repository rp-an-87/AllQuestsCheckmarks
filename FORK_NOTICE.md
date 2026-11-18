# Fork notice

Personal fork of **AllQuestsCheckmarks** ("MoreCheckmarks") 1.4.1 by ZGFueDkx (danx91).

- Upstream: https://github.com/danx91/AllQuestsCheckmarks (tag v1.4.1)
- License: GPL-3.0 (see `LICENSE`); this fork stays GPL-3.0 with its source available.
- Build dependency: https://github.com/danx91/SPT-ZGFueDkxCommonLibrary (no license file upstream), cloned as a sibling folder and compiled in, as upstream's README describes. It is not modified or redistributed here.
- Branch `master` mirrors upstream; work lives on `hideout-merge`.
- AI use: code in this fork is written with Claude (Claude Code) assistance. Do not send it upstream as-is.

## What this fork adds

- **Need overlays** drawn on the game's wishlist overlay of an item (no wishlist needed): what to loot in raid (including the Collector), what you can hand in or still have to buy out of raid, an icon for buyable items in raid, an icon for items to keep in the stash. Strong when needed now, dashed border when needed later. The checkmark itself is only the grey found in raid marker (the original colored checkmarks are one setting away: "Classic colored checkmarks").
- **A snapshot of needs** (`NeedsSnapshot`): quests (started ones from the profile, the rest from the quest data), hideout upgrades (from the Wishlist Extended fork), your stash shared out between all of them, per item and per source.
- **Quest conditions that accept several items for one shared count** are evaluated together over the stash.
- **Tooltip:** shows when hovering the item, a separated layout, a checklist (can hand in, can buy, keep, must get FiR), shared-condition notes, compact numbers, hideout section.
- Non-FiR copies of items that only accept FiR are handled per copy.

## Where the changes are (for merging upstream)

New files hold almost everything: `NeedsSnapshot`, `NeedsOverlay`, `DashedBorder`, `OverlayIcons`, `WishlistBridge`, `ItemHoverPatches`, `NumberFormat`, `AllQuestsCheckmarksApi`, `SettingsFork`.
Upstream files with edits: `QuestItemViewPanelPatch.cs` (the tooltip; the main place for merge conflicts), `QuestsData.cs` (keeps the target lists of conditions), `StashHelper.cs` (one pass count), `QuestsHelper.cs` (non-FiR relevance), `Plugin.cs`, `QuestClassPatch.cs`, `locales/en.json`, and `Settings.cs` (three lines: `partial` and a call to `InitFork`).
Settings: upstream's sections (1. General, 2. Colors, 3. Text, 9. Debug) are unchanged; ours are 4 to 8. Advanced settings (overlay look, testing) are hidden unless F12 shows advanced settings.

Build changes: csproj uses `TarkovDir` (from an uncommitted `.csproj.user`) instead of assuming the repo sits in the game folder; deploys the DLL and the locales, keeping `.orig`/`.bak`.
