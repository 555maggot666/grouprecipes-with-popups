# Changelog

## 1.0.2
- Clears the held back piece list when the connection or world changes, so leftovers cannot be applied
  in the next world.

## 1.0.1
- Rebuilds the build-piece catalogue once when an incoming piece is unknown, so mods that add build pieces
  without adding items or recipes are picked up instead of being skipped.
- Holds back piece unlocks while the build ui is still loading and retries them once the hud is ready,
  instead of letting the vanilla call half-finish without a popup.
- `Tick`-based retry for those held back pieces, capped at 200 entries.

## 1.0.0
- Initial release.
- Shares materials/items, recipes, build pieces and crafting-station unlocks between all players of a world.
- Shows the original Valheim unlock popups (icon + name) for every received entry instead of a plain text line.
- Catches up players on join from a persistent per-world ledger; popups are paced and capped on join.
