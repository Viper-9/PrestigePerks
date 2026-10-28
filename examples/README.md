# PrestigePerks add-ons

You can add your own perks to PrestigePerks without changing PrestigePerks. Add-on perks show up in the perk list after the 20 built-in perks. They work like built-in perks everywhere: rerolls, buying tiers with coins, fixed slots, the collection count, and tier scaling.

This guide is for mod authors. A complete working add-on is in [`ExampleAddon/`](ExampleAddon).

Requires PrestigePerks 0.1.6 or later on SPT 4.1.x.

## Contents

- [Add-on types](#add-on-types)
- [Where the files go](#where-the-files-go)
- [perks.json](#perksjson)
- [Bonus types](#bonus-types)
- [Texts and translations](#texts-and-translations)
- [Icons](#icons)
- [Client code perks](#client-code-perks)
- [Server code perks](#server-code-perks)
- [Tiers and scale](#tiers-and-scale)
- [Removing an add-on](#removing-an-add-on)
- [Troubleshooting](#troubleshooting)
- [The example add-on](#the-example-add-on)

## Add-on types

| Type | What you ship | The effect comes from |
|---|---|---|
| Data | `perks.json` (and icons) | Profile bonuses that SPT and EFT already read, such as quest money or fuel use. No code. |
| Client code | `perks.json` + a BepInEx plugin | Your client code: a Harmony patch, a new key binding, anything. PrestigePerks tells you whether the perk is on and how strong it is. |
| Server code | `perks.json` + an SPT server mod | Your server code, for effects that the server calculates. PrestigePerks gives you the same on/off and strength for a PMC. |

A perk is either a data perk (`"kind": "bonus"`) or a code perk (`"kind": "code"`). Code perks get their effect from your client plugin, your server mod, or both.

## Where the files go

Every add-on puts its `perks.json` in a folder of its own inside the PrestigePerks server folder:

```
SPT_Runtime/user/mods/PrestigePerks/perks/<your-addon>/perks.json
SPT_Runtime/user/mods/PrestigePerks/perks/<your-addon>/icons/yourname.perk.png   (optional)
BepInEx/plugins/<YourAddon>/<YourAddon>.dll                               (client code perks)
SPT_Runtime/user/mods/<YourServerMod>/...                                         (server code perks)
```

- The data goes inside the PrestigePerks folder because SPT 4.1 logs an error for any `user/mods` folder that has no DLL.
- PrestigePerks reads `perks/` once at server start. Restart the server after changing it.
- Add-on folders are read in ordinal name order, and that is the order in the perk list.
- If two add-ons use the same perk id, the first one in folder order wins and the other perk is skipped with a warning.
- The PrestigePerks release zip has no `perks/` folder, so installing a PrestigePerks update over the old one keeps add-ons in place.

## perks.json

```jsonc
{
  "perks": [
    {
      "id": "yourname.medic",           // required, see the id rules below
      "kind": "bonus",                  // required: "bonus" (data perk) or "code"
      "unit": "points",                 // required: "percent", "points" or "flat"
      "bonuses": [                      // bonus perks only: 1 to 4 bonuses
        { "type": "HealthRegeneration", "value": 20 }
      ],
      "icon": "icons/yourname.medic.png", // optional
      "texts": {                        // optional
        "en": { "name": "Field Medic", "effect": "Health regeneration {0}", "detail": "Out of raid" }
      }
    },
    {
      "id": "yourname.sprint",
      "kind": "code",
      "unit": "percent",
      "amount": 10                      // code perks only: the tier III amount shown in the list
    }
  ]
}
```

Comments and trailing commas are allowed.

### Fields

| Field | Rules |
|---|---|
| `id` | Lower case `author.perk`: `[a-z0-9_-]+` then a dot then `[a-z0-9_.-]+`, at most 64 characters, not ending with a dot. Built-in perk ids have no dot, so a dotted id never clashes with them. **Never change an id after release.** Players' ownership and tiers are stored by id. |
| `kind` | `"bonus"` or `"code"`. |
| `unit` | How the number is shown. `percent` shows `%`. `points` shows `%` in English and `%p` in Korean; use it for bonuses that add to an existing percentage. `flat` shows the bare number. |
| `bonuses` | Bonus perks only. 1 to 4 entries `{ "type", "value" }`. All values must be the same, because the list shows one number (the first value). `value` is the tier III value: not 0, at most 100 either way. `SkillGroupLevelingBoost` also needs `"skillType"`. |
| `amount` | Code perks only, and required for them. The tier III amount shown in the list: not 0, at most 100 either way. It is for display; your code decides the real effect, so keep the two in step. A bonus perk ignores `amount` (with a warning). |
| `icon` | Optional. A path relative to your add-on folder. See [Icons](#icons). |
| `texts` | Optional. See [Texts and translations](#texts-and-translations). |

The sign of the value is shown as is, so a fee reduction is written as a negative value and shows as `-20%`.

## Bonus types

A bonus perk adds normal profile bonuses to the PMC, the same kind the hideout gives. PrestigePerks adds them while the perk is in an open slot and removes them before the profile is saved, so the profile file never holds them.

Only these types are accepted. They are the ones SPT and EFT read as a sum of the PMC's bonuses. Types that change structure, like stash size, are not accepted, because removing the add-on could make items disappear.

| `type` | What it changes |
|---|---|
| `ExperienceRate` | Experience gained |
| `SkillGroupLevelingBoost` | Leveling speed of one skill group. Needs `"skillType"`: `Physical`, `Combat`, `Special`, `Practical` or `Mental` |
| `ScavCooldownTimer` | Scav cooldown (negative is shorter) |
| `HealthRegeneration` | Out-of-raid health regeneration |
| `EnergyRegeneration` | Out-of-raid energy regeneration |
| `HydrationRegeneration` | Out-of-raid hydration regeneration |
| `QuestMoneyReward` | Money in quest rewards |
| `RagfairCommission` | Flea market listing fee (negative is cheaper) |
| `InsuranceReturnTime` | Insurance return time (negative is faster) |
| `FuelConsumption` | Generator fuel use (negative is less) |
| `RepairWeaponBonus` | Repair kit points needed to repair weapons. Trader repairs are not affected. |
| `RepairArmorBonus` | Repair kit points needed to repair armor. Trader repairs are not affected. |

Your value is added on top of the hideout's own bonuses of the same type and of other perks, the same way the game adds hideout bonuses. Test your values in game with the hideout fully built.

Bonus perks apply to the PMC only, never to the Scav.

## Texts and translations

`texts` maps an EFT language code (`en`, `ru`, `kr`, `ge`, ...) to up to three strings:

| Key | Shown as |
|---|---|
| `name` | Perk name |
| `effect` | Effect line. Must contain `{0}`, which becomes the value for the tier, such as `+10%`. An effect without `{0}` is dropped with a warning. |
| `detail` | Optional extra line |

The client looks for each text in this order and uses the first one it finds:

1. The player's translation file for the game language (`BepInEx/plugins/PrestigePerks/Locales/<lang>.json`)
2. Your `texts` for the game language
3. The player's or the shipped English file
4. Your `texts.en`
5. The perk id as the name, and the bare value as the effect

Always include `en`. Players can translate or rename your perk without touching your files by adding these keys to their `Locales/<lang>.json`:

```json
{
  "perk.yourname.medic.name": "...",
  "perk.yourname.medic.effect": "... {0}",
  "perk.yourname.medic.detail": "..."
}
```

## Icons

- PNG only, at most 256 KB, inside your add-on folder (no absolute paths, no `..` out of it).
- The built-in icons and the example icons are 128 × 128.
- The client downloads add-on icons from the server once per game start.
- A perk without an icon, or with a bad one, uses the PrestigePerks taskbar icon. A bad icon only drops the icon, not the perk.

## Client code perks

Define the perk in `perks.json` with `"kind": "code"`, then ship a BepInEx plugin that gives it its effect.

### Project

Reference `BepInEx/plugins/PrestigePerks/PrestigePerks.Client.dll` and do not copy it into your output (`<Private>false</Private>`). Declare the dependency so PrestigePerks loads first:

```csharp
[BepInPlugin("yourname.prestigeperks.sprint", "Sprint Perk", "1.0.0")]
[BepInDependency("com.viper.prestigeperks")]
public class SprintPlugin : BaseUnityPlugin
```

### API: `PrestigePerks.Api.PerkAddons` (client)

| Member | Use |
|---|---|
| `int Register(string id)` | Call once in `Awake`. Returns a slot number for your perk, or `-1` if the id is malformed or already registered (a warning is logged). The id must match `perks.json`. |
| `bool Has(int slot)` | Is the perk on: in an open slot of the PMC with a tier above 0. |
| `float Scale(int slot)` | Strength for the active tier, 0 when off. Multiply your tier III effect by it. |
| `bool IsPmc(Profile)`, `IsPmc(Player)`, `IsPmc(SkillManager)` | Is this the main PMC (not the Scav, not a bot). Perks are PMC only. |
| `bool IsPmcOwnerId(string ownerId)` | Is this item owner id the main PMC's. |
| `void Fail(int slot, Exception error)` | Call when your code throws. Turns the perk off for the rest of the session and logs one warning. |

`Has` and `Scale` are plain array reads with no allocation, so they are safe to call every frame or from a hot getter. They also work outside patches, for example in `Update`.

### Pattern

```csharp
private void Awake()
{
    SprintSlot = PerkAddons.Register("yourname.sprint");
    if (SprintSlot < 0)
    {
        return;   // id rejected, do not patch
    }

    new Harmony("yourname.prestigeperks.sprint").CreateClassProcessor(typeof(SprintPatch)).Patch();
}

[HarmonyPatch]
internal static class SprintPatch
{
    private const float Bonus = 0.10f;   // tier III effect, same as "amount": 10 in perks.json

    private static MethodBase TargetMethod() => /* your target */;

    private static void Postfix(SkillManager __instance, ref float __result)
    {
        var slot = SprintPlugin.SprintSlot;
        if (!PerkAddons.Has(slot))   // cheapest check first
        {
            return;
        }

        try
        {
            if (PerkAddons.IsPmc(__instance))
            {
                __result += Bonus * PerkAddons.Scale(slot);
            }
        }
        catch (Exception e)
        {
            PerkAddons.Fail(slot, e);
        }
    }
}
```

- Register in `Awake`, before the main menu. The perk turns on with the next perk state the client receives, which happens on every main menu visit.
- Check `Has` before anything else, so the patch costs almost nothing when the perk is off.
- If the server knows a code perk that no client plugin registered, the client logs `perk <id> is a code perk but no client add-on registered it; it has no effect` once. The perk still shows in the list.

See [`ExampleAddon/client/ExamplePlugin.cs`](ExampleAddon/client/ExamplePlugin.cs) for a complete plugin.

## Server code perks

For effects the server calculates, ship an SPT server mod that references the PrestigePerks server DLL (`SPT_Runtime/user/mods/PrestigePerks/PrestigePerks.dll`) and depends on mod GUID `com.viper.prestigeperks`.

### API: `PrestigePerks.Api.PerkAddons` (server)

| Member | Use |
|---|---|
| `double Scale(PmcData? pmc, string id)` | Strength for that PMC's active tier, 0 when off or when the id is unknown. Multiply your tier III effect by it. |
| `bool Has(PmcData? pmc, string id)` | `Scale(pmc, id) > 0`. |

```csharp
var scale = PerkAddons.Scale(pmcData, "yourname.crafter");
if (scale > 0)
{
    time *= 1 - 0.10 * scale;   // tier III: 10% faster
}
```

- Pass the PMC of the request (the session's `PmcData`), not the Scav.
- Each call reads PrestigePerks' perk data under a lock. In a loop, read it once per request and reuse the value.
- Perk data can be read after load order `OnLoadOrder.Preload + 10`.
- The server cannot tell whether a server mod uses a code perk, so there is no warning for a server code perk without code.

The example add-on has no server part.

## Tiers and scale

- Players buy tiers I, II and III with prestige coins. Prices and when tiers unlock are set by the PrestigePerks config, the same as for built-in perks. Add-ons cannot change them.
- The strength of each tier comes from `tierEffectRatio` in the PrestigePerks config. The default is `[0.5, 0.75, 1.0]`, so tier I is half of your tier III value.
- Bonus perks: PrestigePerks multiplies each bonus value by the ratio for you.
- Code perks: multiply your tier III effect by `Scale`. Do not hard-code the tier ratios.
- The list shows `amount`, or the first bonus value, times the ratio for the tier.

## Removing an add-on

When a player removes an add-on:

- its perks disappear from the list, rerolls and the collection count, and slots that held them become empty;
- PrestigePerks keeps the player's ownership and tiers for those ids, so putting the same add-on back restores them (slots stay empty).

This is why ids must never change.

## Troubleshooting

PrestigePerks checks every add-on at server start and reports each problem as a `[PrestigePerks]` warning in the SPT server log. Each warning starts with the file (`perks/<your-addon>/perks.json`) and the perk id.

| Result | Causes |
|---|---|
| The whole add-on is skipped | `perks.json` is missing or is not valid JSON, or it has no `"perks"` list |
| One perk is skipped | The id is not a lower-case dotted `author.perk`; `kind` or `unit` is not one of the listed values; a bonus type is not in [Bonus types](#bonus-types) (names are case-sensitive); bonus values differ, are 0 or are over 100; `skillType` is missing or misplaced; a code perk has `bonuses` or no valid `amount`; the id is used twice in the file or by an add-on earlier in folder order |
| The perk stays, one part is dropped | The icon is missing, too large, not a PNG or outside your folder (the default icon is used); an `effect` text has no `{0}` (that text is dropped); a bonus perk has `amount` (ignored) |

For example:

```
[PrestigePerks] perks/yourname/perks.json: yourname.medic: unknown unit ("percent", "points" or "flat"). Skipping this perk
```

On a successful start the server log lists the installed add-on perks:

```
[PrestigePerks] add-on perks (2): yourname.medic, yourname.sprint
```

## The example add-on

[`ExampleAddon/`](ExampleAddon) is used for testing PrestigePerks and is not part of any release.

| Path | What it shows |
|---|---|
| `perks/perks.json` | One client code perk (`example.porter`), one data perk for each bonus type, missing translations and texts, and one deliberately broken entry (`noauthor`) |
| `perks/icons/` | Three 128 × 128 icons |
| `client/ExamplePlugin.cs` | A client plugin that adds carry weight while `example.porter` is on |

Its values are large on purpose so they are easy to see in game. Do not copy them as they are.

To try it, copy `perks/` to `SPT_Runtime/user/mods/PrestigePerks/perks/example/` and build `client/` into `BepInEx/plugins/PrestigePerks.ExampleAddon/`. Inside this repository, `tools/deploy-dev.ps1 -Example` does both.
