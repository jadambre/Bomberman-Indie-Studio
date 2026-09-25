# Simulation and balance validation

Validated on 25 September 2026.

## Automated rules tests

All 16 EditMode tests passed in the actual Unity editor through the project test runner. The report is saved in `QA/simulation-tests.json`.

Coverage includes pillar and crate blast blocking, drop reveal, transitive chain reactions, bomb ray blocking, movement interpolation and collision, walk-out grace, planting while moving, bomb-capacity recovery, all four power-ups and their caps, cooperative friendly fire, exact serialized continuation, malformed saved movement rejection, five-stage progression and teammate revival, timeout, deterministic procedural generation, spawn escape circuits, connected arena space, and AI escape / refusal of suicidal bombing.

The procedural-map check exercises 40 different seeds across all five stages (200 arena generations). Every spawn has at least eight connected initial floor cells, every power-up is available beneath a crate, and all non-pillar cells are connected when destructible crates are removed.

## AI activity and survival sample

A standalone C# simulation harness using the same source and actual Unity math assemblies sampled eight seeds for each stage, running 90 simulated seconds per seed. The human stood idle with an extended shield so survival measurements describe bot behavior rather than premature human defeat. These measurements are development diagnostics, not a claim about player win rate.

| Stage | Starting bots | Average bots alive after 90 s | Average bombs placed | Average crates destroyed |
| --- | ---: | ---: | ---: | ---: |
| 1 / Mossbound | 2 | 1.875 | 38 | 35 |
| 2 / Sunstone | 3 | 2.875 | 46 | 32 |
| 3 / Frostline | 4 | 3.750 | 48 | 29 |
| 4 / Afterglow | 5 | 4.000 | 49 | 27 |
| 5 / The Core | 6 | 5.125 | 54 | 27 |

Bots actively open routes and largely survive their own bombs. Danger prediction accounts for bomb fuse times, existing flames, chain reactions, and movement overlap with a departing tile. A bot plants only when a timed escape search finds shelter. Bots have four variations: scavenger, hunter, long-range tactician, and runner. Later arenas add opponents and raise their base speed and blast range; late tacticians can maintain two bombs.

## Gameplay rules relevant to testing

- One or two humans cooperate. Every explosion is dangerous to every character, including its owner and the other human.
- A stage is won when all bots are eliminated and at least one human survives. Simultaneous elimination of all humans and bots is a loss.
- A defeated partner revives at the next stage with base abilities.
- Human upgrades last only for the current stage. Every new stage resets both players to one bomb, two blast cells, 3.4 movement cells per second, and Wall Pass off. Within a stage, caps remain five bombs, seven blast cells, and 5.8 movement cells per second.
- Wall Pass crosses crates only. Pillars and non-permitted bombs remain solid, and flames remain lethal.
- The initial shield lasts 1.2 simulation seconds; the first bomb fuse lasts 2.25 seconds, so it cannot be used to safely absorb the first self-explosion.
- Drops revealed by an explosion survive that reveal, cannot be collected while their tile is flaming, and can be destroyed by a later explosion.
- The saved session contains movement interpolation, fuses, flames, walk-out permissions, upgrades, AI timers, and RNG state. Loading owns a deep copy.
