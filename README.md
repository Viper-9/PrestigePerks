# PrestigePerks

A prestige overhaul for SPT 4.1.x. Each prestige makes your next run a little stronger instead of just wiping it.

Work in progress.

## Planned

- Lower prestige requirements, close to SPT 5.0 (prestige 1-6)
- Perk slots: one more slot per prestige level, filled by random rolls you can lock and reroll
- Prestige coins: items handed in at prestige are converted to coins by their rouble value
- Leftover coins carry over to your next prestige automatically
- Works with the Prestige Trader mod, which sells for coins

## Requirements

- SPT 4.1.x
- [Content Backport - Prestiges](https://github.com/ArchangelWTF/ContentBackportPrestiges) 1.1.0+
- WTT-CommonLib 3.x (server and client)
- WTT-ContentBackport 2.0.x (server and client)

## Building

1. Copy `PrestigePerks.local.props.example` to `PrestigePerks.local.props` and set `SptRoot` to your SPT folder.
2. `dotnet build PrestigePerks.slnx`
