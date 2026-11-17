# Fork notice

Personal fork of **AllQuestsCheckmarks** ("MoreCheckmarks") 1.4.1 by ZGFueDkx (danx91).

- Upstream: https://github.com/danx91/AllQuestsCheckmarks (tag v1.4.1)
- License: GPL-3.0 (see `LICENSE`); this fork stays GPL-3.0 with its source available.
- Build dependency: https://github.com/danx91/SPT-ZGFueDkxCommonLibrary (no license file upstream), cloned as a sibling folder and compiled in, as upstream's README describes. It is not modified or redistributed here.
- Branch `master` mirrors upstream; work lives on `hideout-merge`.
- AI use: code in this fork is written with Claude (Claude Code) assistance. Do not send it upstream as-is.

## Changes (planned)

- Tooltip shows when hovering the item itself, not only the checkmark.
- Hideout section and combined quest + hideout totals (FiR / non-FiR), read from the WishlistExtended fork (soft dependency).
- More visible "Item found in raid" label.

Build changes: csproj uses `TarkovDir` (from an uncommitted `.csproj.user`) instead of assuming the repo sits in the game folder; deploys the DLL only, keeping `.orig`/`.bak`.
