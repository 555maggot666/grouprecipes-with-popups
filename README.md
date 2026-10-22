# GroupRecipes with Popups

A Valheim BepInEx mod that shares **material, recipe, build-piece and crafting-station discoveries** between
all players of a world — including players who were offline when something was discovered — and shows the
**original Valheim unlock popup** (icon plus "New material" / "Recipe learned" / "New piece" / "New station") for
every entry a player receives, not just a line of text.

## How it works

* Four kinds of discoveries are shared: **materials/items**, **recipes**, **build pieces** and **crafting stations**
  (with their level; a level is only ever raised, never lowered).
* The server (dedicated server or the hosting player) keeps a persistent, append-only *ledger* per world in
  `BepInEx/config/GroupRecipesWithPopups/<world>_<worldUID>.txt`. Nothing is ever removed from it.
* Every client checks its own knowledge once per second. New entries are sent to the server, which stores them
  and forwards them to everybody else (the game's own routed-RPC forwarding for "Everybody").
* When a player joins, the server sends the complete ledger, so entries discovered while the player was offline
  are caught up. Entries the player already knows are ignored.
* Applying an entry uses the game's own unlock path, so the popup is the vanilla one:
  * materials: `Player.AddKnownItem` (public),
  * recipes and pieces: the private `AddKnownRecipe` / `AddKnownPiece` (via reflection),
  * stations: the vanilla behaviour is rebuilt (station level raised, then the vanilla "new station" /
    "station level" popup is queued), because the game method needs a live station object.
  If a method is missing in some game version, the mod logs one warning, writes the entry directly and queues the
  popup itself.
* Popups are paced (`PopupIntervalSeconds`) so they appear one after another. When a large batch arrives (joining
  a world, or another player uploading their whole knowledge) only the first `MaxPopupsOnJoin` entries show a
  popup — materials first, then recipes/pieces, then stations. **All other entries are still applied, silently**,
  followed by one summary line (if `ShowNotifications` is on).
* Entries already present in the ledger are ignored completely, which prevents popup loops.

## Installation

Requires [BepInExPack for Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) 5.4.2351.

**Remove other recipe-sync mods and old copies of this mod's DLLs first**, otherwise two mods fight over the same
knowledge data and every unlock is applied (and shown) twice.

### r2modman / Thunderstore Mod Manager

Install *GroupRecipes_with_Popups* into the profile used for the client **and** into the dedicated server profile (if you
use one).

### Manually

Copy **both** `GroupRecipesWithPopups.dll` and `GroupRecipesWithPopups.Core.dll` into `BepInEx/plugins/`
(for example `BepInEx/plugins/GroupRecipesWithPopups/`).

* **Client:** every player who should take part installs it.
* **Server:** install it on the dedicated server (or on the hosting player's game) to get the persistent ledger and
  the join catch-up.

### Missing mod on the server

Unknown RPC names are ignored by peers that do not have the mod, so a vanilla server never crashes. Without the mod
on the server there is no ledger and no catch-up; modded clients can still exchange live unlocks, because the
server's routing forwards the messages, but players who were offline will not receive what they missed.

## Configuration

File: `BepInEx/config/555maggot666.GroupRecipesWithPopups.cfg`

| Section | Key | Type | Default | Effect |
|---|---|---|---|---|
| General | `ShowNotifications` | bool | `true` | Only controls the summary line ("N more unlocks were synchronised without popup"). The vanilla popups are not affected. |
| Sync | `Items` | bool | `true` | Share materials/items (sending and receiving). |
| Sync | `RecipesAndPieces` | bool | `true` | Share recipes and build pieces (sending and receiving). |
| Sync | `CraftingStations` | bool | `true` | Share crafting-station levels (sending and receiving). |
| Display | `EnablePopups` | bool | `true` | Show the vanilla unlock popup for entries received from others. When `false`, entries are applied silently. |
| Display | `MaxPopupsOnJoin` | int | `25` | Upper limit of popups for a large batch (join catch-up, bulk upload). Further entries are applied without popup. |
| Display | `PopupIntervalSeconds` | float | `0.3` | Delay between two applied entries. |

## Building

The build reads the reference locations from environment variables (or `-p:NAME=value`); it fails with a clear
message when one is missing. No game or BepInEx assembly is stored in the repository.

```bash
VH_MANAGED_DIR=/path/to/valheim_Data/Managed \
BEPINEX_CORE_DIR=/path/to/BepInEx/core \
dotnet build -c Release

dotnet run --project tests/GroupRecipesWithPopups.Tests -c Release
```

`GroupRecipesWithPopups.Core` contains all logic that does not need the game (ledger, file format, snapshot diff,
popup planning and pacing) and is covered by the console test project.

## Thunderstore

The Thunderstore categories are chosen in the web upload, not in `manifest.json`. Set these when publishing:
`AI Generated`, `Client-side`, `Server-side`, `Crafting`.

## AI disclosure

The code of this mod was created with generative AI on behalf of 555maggot666.

## License

MIT — see [LICENSE](LICENSE). Copyright (c) 2026 555maggot666.
