# Merging upstream into this fork

Fork of AllQuestsCheckmarks 1.4.1 ("MoreCheckmarks"). `master` mirrors upstream, our work is on `hideout-merge`.

## Update steps

```
git fetch upstream
git checkout master && git merge --ff-only upstream/master
git checkout hideout-merge && git rebase master      # or: git merge master
dotnet build AllQuestsCheckmarksClient/AllQuestsCheckmarksClient.csproj -c Release
```

Push (if there is ever an origin) with `--force-with-lease` after a rebase. Always ask the owner first.

## Where conflicts will happen

Almost everything we added is in new files that upstream does not have, so those never conflict. Upstream files we edited, most likely conflict first:

| File | What we changed | How to resolve |
|---|---|---|
| `Patches/QuestItemViewPanelPatch.cs` | The tooltip method: separated layout, current block then later block, pool notes, compact numbers, hideout section, `ClassicCheckmarks` switch, vanilla checkmark in hideout | Take upstream's new version of the method and re-apply our additions. Search the file for `Settings.` entries from `SettingsFork.cs` to find them all |
| `Helpers/QuestsData.cs` | Keeps the target lists of conditions (`QuestPool`, `Pools`) | Re-add the pool list where conditions are read |
| `Helpers/StashHelper.cs` | `CountAll` (one pass count), `MoneyIds` | Keep upstream's helpers, re-add ours |
| `Helpers/QuestsHelper.cs` | Non-FiR relevance (`CheckmarkOnNonFir`) | 6 lines |
| `Plugin.cs`, `Patches/QuestClassPatch.cs`, `locales/en.json` | Enable our patches, snapshot invalidation, new locale keys | Small |
| `Helpers/Settings.cs` | Three lines: `partial` and one `InitFork(config);` call before `config.SettingChanged += ...` | Re-add; take everything else from upstream |
| `AllQuestsCheckmarksClient.csproj` | `TarkovDir` from `.csproj.user`, deploys DLL and locales, keeps `.orig`/`.bak` | Keep ours |

New files (ours, no upstream counterpart): `NeedsSnapshot`, `NeedsOverlay`, `DashedBorder`, `OverlayIcons`, `HideoutNeedsData` (hideout requirements, was read from the Wishlist Extended fork before 2026-11-18), `HideoutUpgradeZonePatch`, `ItemHoverPatches`, `NumberFormat`, `TooltipSeparator`, `AllQuestsCheckmarksApi`, `SettingsFork`.

## What to check after a merge

1. It builds. The most fragile points are game types used by the overlay: `WishlistGridView` (fields `_glow`, `_border`, `_icon`, method `Show`, private `_templateId`), `QuestItemViewPanel` (`_tooltipText`), `ItemRequirementPanel.Show`, `GridItemView.ShowTooltip`, `SimpleTooltip.Show`. If the game changed them, the patches fail at startup and the log says which.
2. If upstream added or removed settings, check section numbering. Upstream owns sections 1 to 3 and 9. Ours are 4 to 8.
3. In game: a quest item and a hideout item show the overlay; hover shows the tooltip; classic mode shows the old checkmarks.

## Settings

| Setting | Owner | State with overlays on |
|---|---|---|
| 1. General: Include Collector, loyalty regain, non-FiR, raid items, unreachable | upstream | used by tooltip and snapshot |
| 1. General: Show only active quests | upstream | now also applies to the overlays (no future quests anywhere) |
| 1. General: Hide fulfilled, Mark enough items | upstream | classic checkmarks only; advanced unless classic is on |
| 1. General: Squad quests | upstream | tooltip and classic only; overlays have no squad case yet |
| 2. Colors: checkmark, non-FiR, collector, enough, squad, custom quest colour | upstream | classic only; advanced unless classic is on (done in `SettingsFork.InitFork`, upstream file untouched) |
| 3. Text, 9. Debug | upstream | used |
| 4. Needs and overlays | fork | see the in-game descriptions. Hideout: "Include hideout upgrades", "Locked upgrades count as later" |
| 5. Overlay colors, 6. Tooltip, 7. Overlay look (advanced), 8. Testing (advanced) | fork | used |

Config migration: renamed or moved fork keys are not migrated by code. When a key changes, edit the owner's `BepInEx\config\com.zgfuedkx.allquestscheckmarks.cfg` with a backup first (see git history of this file for earlier renames).

Full settings cross reference of both forks: `dev-own\docs\checkmarks-settings-reference.md` (workspace, not in this repo). Fork settings start their F12 description with `[Fork]`.
