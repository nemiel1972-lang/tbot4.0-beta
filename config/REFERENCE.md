# Config reference (`config.vorlage.json`)

Every entry of the template with a short description. Values shown are the template defaults, not recommendations.
Missing fields fall back to the code default. Order inside a block: `Active`, `Origin`, content groups, then
`CheckIntervalMin/Max` (minutes between two runs of a worker).

Coordinates everywhere are objects `{ "Galaxy", "System", "Position", "Type": "Planet"|"Moon" }`.

Conventions of the template:
- All workers are `Active: false`. Switch on only what you want.
- All building/research caps (`Max…`) are `0`: enter your own limits before enabling a worker.
- Coordinates are `1:1:1` placeholders. Replace them with your own.
- Passwords/tokens: put the real values into `secrets/` (see `README.md`), never into chats or screenshots.

Contents: [New account quick start](#new-account-quick-start) · [Credentials](#credentials) · [General](#general) ·
[DiscordWebhook](#discordwebhook) · [SleepMode](#sleepmode) · [GhostSleep](#ghostsleep) · [DebrisSave](#debrissave) ·
[Defender](#defender) · [Brain](#brain) · [Expeditions](#expeditions) · [AutoFarm](#autofarm) · [AutoHarvest](#autoharvest) ·
[AutoColonize](#autocolonize) · [AutoDiscovery](#autodiscovery)

## New account quick start

A fresh account does not need most of this file. A sensible order:

1. `Credentials`, `General.Host/Port`: get the bot logged in.
2. `Brain.AutoMine` + `Brain.AutoResearch` (fill the `Max…` caps, e.g. mines 20-30, lab 10) so the account grows.
3. `Expeditions` as soon as Astrophysics and some cargo ships exist.
4. `Defender` for attack alerts (Telegram/Discord).
5. `SleepMode` if you want fixed sleep hours.
6. `AutoHarvest`, `AutoDiscovery`, `AutoColonize` later. `AutoFarm` last.

**AutoFarm for a new account.** AutoFarm spies inactive players and attacks the profitable ones. It needs probes,
cargo ships, a few free slots and some combat power. Everything below `# Escort and simulation` in the table is only
relevant once you farm *defended* targets; start with undefended targets only:

| Setting | Suggested start |
|---|---|
| `Origin` | one planet/moon with your cargo ships |
| `SystemRange` | 30-50 (small radius around the origin), `ScanRange` is then ignored |
| `AttackDefendedTargets` | `false` (skips the whole escort/simulation machinery) |
| `MinimumResources` | 100000 and up (raise it as your fleet grows) |
| `CargoType` | `LargeCargo` (or `SmallCargo`) |
| `UseSpeedFleet` | `false` |
| `NumProbes` | 5-10 |
| `MaxSlots` / `SlotsToLeaveFree` | 1-2 / 1 |
| `MaxFlightTime` | 1800-3600 seconds |

Leave everything else on its default. The escort fields (`Escort…`, `MinWinRate`, `SimulationRuns`,
`TargetCombatTechBonus`) are ignored while `AttackDefendedTargets` is `false`.

**SpeedFleet.** `UseSpeedFleet: true` makes AutoFarm use Pathfinders as its cargo ship instead of `CargoType`. Pathfinders are fast and carry cargo, which helps with
far targets, but you need to have researched and built them. A new account should keep it `false`.

---

## Credentials

| Field | Meaning |
|---|---|
| `Universe` | Universe name exactly as shown in the lobby. |
| `Email`, `Password` | Gameforge login. Keep real values in `secrets/credentials`. |
| `Language` | Server language code, e.g. `en`, `de`. |
| `LobbyPioneers` | `true` if the account is in the Pioneers lobby. |
| `BasicAuth.Username/Password` | Only for servers/proxies that require HTTP basic auth. Usually empty. |
| `DeviceConf.*` | Browser fingerprint sent at login (device name, system, browser, user agent, memory, CPU threads, colour depth, screen size, timezone, language). Keep it consistent; do not change it often. |

## General

| Field | Meaning |
|---|---|
| `Host`, `Port` | Address of the local `ogamed` connector the bot talks to. |
| `Proxy.Enabled` | Route traffic through a proxy. |
| `Proxy.Address`, `Type`, `Username`, `Password` | Proxy address, type (e.g. `socks5`) and login. Real values in `secrets/proxy`. |
| `Proxy.LoginOnly` | `true` = proxy is used only for the login, not for game requests. |
| `AnonymizeLogs` | Optional, default `false`. `true` replaces player name, universe and e-mail in all log lines by `[Player]`, `[Universe]`, `[Email]`. `false` shows the real names. Read at start. |
| `CaptchaAPIKey` | API key of the captcha solving service (optional). |
| `LoginPause.*` | Protects the IP: after `FailuresBeforePause` (2) failed logins in a row the bot waits `PauseMinutes` (30), doubling with each further failure up to `MaxPauseMinutes` (120), instead of retrying. The manual captcha page `/bot/captcha` stays usable; solving it ends the pause. State in `data/login_pause_<alias>.json`. `Active` switches it off. |
| `SlotsToLeaveFree` | Fleet slots no worker may use, kept free for you. |
| `SlotPriorityLevel.*` | Who gets free slots first when workers compete. Lower number = served first. Keys: `Brain`, `Expeditions`, `AutoFarm`, `AutoColonize`/`Colonize`, `AutoDiscovery`, `AutoHarvest`. |

## DiscordWebhook

Mirrors Telegram messages to Discord (send only; commands still go through Telegram).

| Field | Meaning |
|---|---|
| `Active` | Switch. |
| `Url` | Webhook URL (secret, keep it in `secrets/discord`). |
| `Username` | Display name of the posts. |

## SleepMode

Fixed daily sleep window: no fleets are sent while asleep.

| Field | Meaning |
|---|---|
| `Active` | Switch. |
| `GoToSleep`, `WakeUp` | Server-time clock times, e.g. `"23:00"`, `"07:00"`. |
| `TelegramMessenger.Active` | Message on sleep/wake-up. |
| `AutoFleetSave.Active` | Fleetsave all ships when going to sleep. |
| `AutoFleetSave.OnlyMoons` | Only save fleets on moons. |
| `AutoFleetSave.DeutToLeave` | Deuterium left on the celestial when saving (the rest goes with the fleet as fuel). |
| `AutoFleetSave.Recall` | Recall the saving fleet so it is back at wake-up. |
| `AutoFleetSave.DefaultMission` | Mission used for the save (`Deploy` etc.). |
| `AutoFleetSave.DeployTarget` | Coordinates the save flies to. |
| `PreventIfThereAreFleets` (optional) | `true` = delay sleeping while fleets would come back during the sleep window. |

## GhostSleep

Sleep variant that fleetsaves everything with a ghost flight but keeps Expeditions and Defender running.

| Field | Meaning |
|---|---|
| `Active` | Switch. |
| `GoToSleep`, `WakeUp` | Clock times (server time). |
| `PreSleepExpeditionMinutes` | Minutes before `GoToSleep` in which all expedition slots are refilled first (0 = off). |
| `PriorityFleetsaveCelestials` | Coordinates (moon and planet) that are fleetsaved first. |
| `AnchorMode` | While asleep, the Defender only checks for attacks around expedition returns. `"LastFleet"` (default): one check shortly before the **last** returning expedition lands (random lead of 45-120 s). Once all remaining expeditions land within about 2.5 minutes, the next anchor is set after the next wave has started, so it again points at the last fleet of the new wave. `"Random"`: the old behaviour (a random returning expedition, 60 s before it lands). |
| `ExpeditionDrain.Active` | `true` = replaces the time-based pre-phase (`PreSleepExpeditionMinutes`): shortly before `GoToSleep` no new expeditions are started if they would not be back in time. At `GoToSleep` the bot waits for stragglers, sends **one** simultaneous wave of all expeditions, fleetsaves and goes to sleep. |
| `ExpeditionDrain.BufferMinutes` | Tolerance: an expedition may still start if it is back at most this many minutes after `GoToSleep` (default 10). |
| `ExpeditionDrain.StragglerWaitMinutes` | At `GoToSleep`, wait at most this long for expeditions that are still out (default 20). Stragglers that are still out afterwards come home during the sleep and are included in the next wave. |
| `ExpeditionDrain.DefaultDurationMinutes` | Expedition duration assumed until the bot has measured at least 3 real ones (default 90). The bot measures the real duration of every expedition per origin (`data/expedition_durations_*.json`) and uses the 90th percentile of the last 30 values. |

## DebrisSave

| Field | Meaning |
|---|---|
| `Celestials` | Celestials used by the Telegram command `/debrissave <duration>`; each sends its fleet to the nearest debris field and recalls it so it is back after the duration. |

## Defender

Detects incoming attacks and reacts.

| Field | Meaning |
|---|---|
| `Active` | Switch. |
| `IgnoreProbes` | Do not react to spy probes. |
| `DefendFromMissiles` | React to missile attacks too. |
| `IgnoreWeakAttack`, `WeakAttackRatio` | Ignore attackers whose fleet points are less than your fleet points divided by the ratio. |
| `IgnoreAttackIfIHave.*` | Ignore the attack if you have less than `MinResourcesToSave` resources and less than `MinFleetToSave` fleet points on the target (nothing worth saving). |
| `RandomActivity` | On: each defender check opens the defences page of a random celestial; off: always of `Home`. Only generates normal-looking activity. |
| `Home` | Home celestial: used for the activity call when `RandomActivity` is off, and as default AutoHarvest origin (the moon at these coordinates) when AutoHarvest has no `Origin`. |
| `Autofleet.Active` | Fleetsave the attacked celestial automatically. |
| `Autofleet.SafetyMarginSeconds` | Seconds before impact at which the fleet must be gone. |
| `Autofleet.TelegramMessenger.Active` | Message when a fleetsave happens. |
| `Discord.Active`, `WebhookUrl` | Attack alert to a Discord webhook (secret). |
| `WhiteList` | Player IDs that are never treated as attackers. |
| `SpyAttacker.Active`, `Probes` | Spy the attacker with this many probes. |
| `MessageAttacker.Active`, `Messages` | Send one of these messages to the attacker. |
| `TelegramMessenger.Active` | Attack alert via Telegram. |
| `Alarm.Active` | Raise an audible/visible alarm level in the alert. |
| `Alarm.SoundFile` | Optional full path to a WAV file played on an attack (Windows SoundPlayer; Linux `paplay`/`aplay`; macOS `afplay`). Empty, missing or unplayable = the old beep. |

## Brain

Automation of building, research and transports. `Brain.Active` is the master switch; each sub-worker has its own `Active`.

### Brain.Transports

Shared settings for all Brain workers that move resources.

| Field | Meaning |
|---|---|
| `Active` | Allow Brain workers to transport resources between your planets. |
| `Origin` | Default celestial resources are collected from. |
| `CargoType`, `CargoFallback` | Preferred cargo ship and fallbacks (`SmallCargo`/`LargeCargo`). Pathfinders are never used for transports. |
| `KeepOnOrigin.*` | Ships that stay on the origin (fixed amount per type, or per free expedition slot; `UseExpeditionManualShips` reads the amount from `Expeditions.ManualShips`). |
| `MixFleet` | As a last step mix small and large cargo (flies at the speed of the slowest). |
| `UseTransportChain`, `CompactSlotPlan` | Plan transports as a chain; replan with fewer shipments if slots are short. |
| `DeutToLeaveOnMoons` | Deuterium that stays on moons. Transports leaving a moon only go out if payload deuterium + this reserve + the flight's own fuel fit into the stock, so the reserve is never eaten by fuel. |
| `DeutToLeaveOnPlanets` | The same reserve for transports leaving a planet (default 0 = nothing kept back). |
| `RoundResources` | Round the amount sent so fleets are not sent for tiny differences. |
| `SendToTheMoonIfPossible` | Deliver to the moon instead of the planet when one exists. |
| `MaxSlots` | Slots Brain transports may use at the same time. |
| `CheckMoonOrPlanetFirst` | Check the moon or the planet of a location first when looking for resources. |
| `DoMultipleTransportIsNotEnoughShipButSamePosition` | If one ship type is not enough, send several transports from the same position. |
| `MultipleOrigins.*` | Collect from several celestials: `Active`, `OnlyFromMoons`, `MinimumResourcesToSend`, `PriorityToProximityOverQuantity`, `Exclude` list. |

### Brain.AutoMine

Builds mines, storage, energy and facilities on all planets.

| Field | Meaning |
|---|---|
| `Active` | Switch. |
| `Transports.Active` | Allow this worker to fetch missing resources by transport. |
| `HomeWorld` | Your main planet (gets the `Max…` caps of the top block). |
| `Max…` (top level) | Caps for the home world: `MaxMetalMine`, `MaxCrystalMine`, `MaxDeuteriumSynthetizer`, `MaxSolarPlant`, `MaxFusionReactor`, `MaxMetalStorage`, `MaxCrystalStorage`, `MaxDeuteriumTank`, `MaxRoboticsFactory`, `MaxShipyard`, `MaxResearchLab`, `MaxMissileSilo`, `MaxNaniteFactory`, `MaxTerraformer`, `MaxSpaceDock`, `MaxLunarBase`, `MaxLunarShipyard`, `MaxLunarRoboticsFactory`, `MaxSensorPhalanx`, `MaxJumpGate`. 0 = do not build. |
| `Colony.*` | Same caps for all other planets. |
| `ColonyOverrides` | List of single planets with their own caps; matched by exact coordinate, only the fields you set override `Colony`. |
| `RandomOrder` | Process planets in random order. |
| `OptimizeForStart` | Use the early-game build order (robotics/shipyard first). |
| `PrioritizeRobotsAndNanites` | Build robotics and nanite factories early. |
| `BuildDepositIfFull` | Build storage when it is full. |
| `BuildSolarSatellites` | Build solar satellites when energy is short. |
| `SolarSatelliteBatch` | Satellites are built in bundles of this size (the need is rounded up); default 20, 0/1 = exactly the missing amount. |
| `EnergyReservePercent` | Before a mine upgrade (or a building with an energy cost such as the Terraformer) that would push energy into deficit, the energy source (solar plant/fusion/satellites) is built first, sized for the missing amount plus this percentage; default 10, 0 = exact. |
| `BuildCrawlers` | Build crawlers. |
| `FixedCrawlerTarget` | Fixed crawler count target; 0 = calculate. |
| `DepositHours` | Storage should hold this many hours of production. |
| `DeutToLeaveOnMoons` | Deuterium that stays on moons. |
| `MaxDaysOfInvestmentReturn` | Skip builds whose payback time is longer than this many days (9999 = no limit). |
| `Exclude` | Celestials never touched. |

### Brain.AutoResearch

| Field | Meaning |
|---|---|
| `Active`, `Transports.Active` | Switch; allow transports for missing resources. |
| `Max…` | Research caps: `MaxEnergyTechnology`, `MaxLaserTechnology`, `MaxIonTechnology`, `MaxHyperspaceTechnology`, `MaxPlasmaTechnology`, `MaxCombustionDrive`, `MaxImpulseDrive`, `MaxHyperspaceDrive`, `MaxEspionageTechnology`, `MaxComputerTechnology`, `MaxAstrophysics`, `MaxIntergalacticResearchNetwork`, `MaxWeaponsTechnology`, `MaxShieldingTechnology`, `MaxArmourTechnology`. 0 = do not research. |
| `Target` | Planet the research is started on (usually the one with the best lab). |
| `OptimizeForStart` | Early-game research order. |
| `EnsureExpoSlots` | Research Computer Technology / Astrophysics as needed to get expedition slots. |
| `PrioritizeAstrophysics`, `PrioritizeComputerTechnology`, `PrioritizePlasmaTechnology`, … | Research these first (one flag per technology). |
| `ForceResearchWhateverTheLabLevel` | Research even if the lab is still low. |

### Brain.LifeformAutoMine

Builds lifeform buildings using the table in `lifeform_targets.json`.

| Field | Meaning |
|---|---|
| `Active`, `Transports.Active` | Switch; allow transports. |
| `StartFromCrystalMineLvl` | Start lifeform building on a planet once its crystal mine reaches this level. |
| `TargetSlot` | Row (1-18) of `lifeform_targets.json` to build towards; 0 = fallback caps only. |
| `MaxBasePopulationBuilding`, `MaxBaseFoodBuilding`, `MaxBaseTechBuilding` | Extra upper limits for the first three building positions (small accounts); the result is the lower of table and limit. |
| `PreventIfMoreExpensiveThanNextMine` | Do not build a lifeform building that costs more than the next mine upgrade. |
| `Exclude` | Celestials never touched. |

### Brain.LifeformAutoResearch

| Field | Meaning |
|---|---|
| `Active`, `Transports.Active` | Switch; allow transports. |
| `PriorityTechs` | Lifeform tech IDs researched first, in this order. |
| `MaxResearchLevel` | Highest level researched per lifeform tech. |
| `MaxTechs11` … `MaxTechs36` | Per-tech level caps (first digit = race, second = tech slot). |
| `Exclude` | Celestials never touched. |

### Brain.AutoCargo (shown as AutoShips)

Builds cargo ships and other ship types. The config key stays `AutoCargo`.

| Field | Meaning |
|---|---|
| `Active` | Switch. |
| `CargoType` | Ship type to build. |
| `MaxCargosToBuild`, `MaxCargosToKeep` | `MaxCargosToBuild` = most ships ordered per run; `MaxCargosToKeep` = target amount per celestial (0 = nothing is built). |
| `LimitToCapacity` | Only build as many as the stored resources need. |
| `MaxProductionHours` | Optional. Upper limit in hours for the production time of one order per celestial (shipyard and nanite level included). 0 or missing = no limit. |
| `Ships` | Optional list of extra ship types, built after the cargo ships: `[{"Type": "Recycler", "Target": 50, "MaxPerRun": 20}]`. `Target` = amount per celestial, `MaxPerRun` = most ships per order (0 = no limit). One order per celestial and run, the first entry whose target is not reached. Pathfinder needs the Discoverer class, Reaper the General class, Crawlers are capped by the game's maximum and only built on planets. |
| `SupplyMoonFromPlanet` | Moons get ships from the planet at the same position. |
| `ExcludeMoons`, `RandomOrder`, `SkipIfIncomingTransport`, `Exclude` | Skip moons; random order; skip a celestial that already has a transport coming; excluded celestials. |

### Brain.AutoDefence

| Field | Meaning |
|---|---|
| `Active` | Switch. |
| `DefenceToReach.*` | Target count per defence type (launchers, lasers, cannons, turret, missiles). Shield domes are `true/false`. 0 = do not build. |
| `ExcludeMoons`, `RandomOrder`, `Exclude` | Skip moons; random order; excluded celestials. |
| `MaxProductionHours` | Optional. Upper limit in hours for the production time of one order per celestial (shipyard and nanite level included). 0 or missing = no limit. The remainder is ordered in later runs. |

### Brain.AutoRepatriate

Sends resources from your planets to one target.

| Field | Meaning |
|---|---|
| `Active` | Switch. |
| `Target` | Target celestial(s) for the resources. |
| `TargetAssociateMoon` | Deliver to the moon at the target position. |
| `MinimumResources` | Only send if at least this much can be sent. |
| `LeaveDeut.OnlyOnMoons`, `DeutToLeave` | Keep this much deuterium behind (optionally only on moons). |
| `CargoType` | Ship type used. |
| `Reverse.*` | Reverse repatriation (fetch resources back out for a build): `Active`, `ReturnFuelFactor` (deuterium taken along = fuel of the return flight x factor, 1.1 = +10 %), `PendingTtlMinutes`. The outbound leg carries 10 % more cargo space than needed (fixed). The return flight takes exactly the ships that flew out. Advanced. |
| `ExcludeMoons`, `RandomOrder`, `SkipIfIncomingTransport`, `Exclude` | As above. |

### Brain.KeepBoosters

Keeps resource boosters running. When no booster of a type is running on a planet, one from your inventory is activated (never bought with Dark Matter).

| Field | Meaning |
|---|---|
| `Active` | Switch. |
| `DryRun` | `true` = only write to the log what would be activated. Check the log first, then set `false`. |
| `Types.Metal`, `Types.Crystal`, `Types.Deuterium`, `Types.Energy` | Which booster types to keep running. |
| `Tiers.bronze`, `.silver`, `.gold`, `.platinum` | Which tiers may be used. Bronze is used first, then silver, gold, platinum. (An old list like `["bronze","silver"]` still works.) |
| `Reserve.bronze`, `.silver`, `.gold`, `.platinum` | Per tier: this many of each booster stay in the inventory; one is only activated while the stock is above it. The Config page shows the current stock next to it. (A single number for all tiers still works.) |
| `MinMineLevel.Metal`, `.Crystal`, `.Deuterium` | A booster is only used on a planet whose matching mine has at least this level (0 = no limit). Saves boosters on planets where the mine is too low to profit. The overview of running boosters is on the Mine plan page. |
| `LogItems` | Log the active items of every planet and the inventory at most every 12 hours (shows event and special items too). |
| `Exclude` | Planets that are left alone. |
| `CheckIntervalMin`, `CheckIntervalMax` | Minutes between checks. |

### Brain.BuyOfferOfTheDay and Brain.AutoFleetJumpGate

| Field | Meaning |
|---|---|
| `BuyOfferOfTheDay.Active` | Buys the daily merchant offer. |
| `AutoFleetJumpGate.Active` | Switch. |
| `AutoFleetJumpGate.Target` | Moon the fleet jumps to. |
| `AutoFleetJumpGate.Exclude` | Moons that never jump (optional list of coordinates with `Type: Moon`). |
| `MinimumAmountOfShipsToBeLeftOnMoon.*` | Ships per type that stay behind. |
| `MinimumAmountOfShipPointsToTeleport` | Only jump if at least this many fleet points would go. |

## Expeditions

| Field | Meaning |
|---|---|
| `Active` | Switch. |
| `Origin` | Celestials expeditions start from. |
| `PrimaryShip` | Auto mode (`ManualShips.Active` = false): main cargo ship type per expedition, e.g. `LargeCargo`. The bot sends as many as are needed to carry the largest possible find (depends on the server's top score, class and lifeform bonus), or all that are available. It adds 1 espionage probe, the single strongest combat ship (Reaper, Destroyer, Bomber, Battlecruiser, Battleship, Pathfinder, Cruiser, Heavy Fighter, Light Fighter) and a Pathfinder if none is in yet. |
| `PrimaryToKeep` | Auto mode: this many primary ships always stay at home. |
| `MinPrimaryToSend` | Auto mode: minimum number of primary ships per expedition. If the origin has fewer left after `PrimaryToKeep`, no expedition is sent from it (0 = no minimum). |
| `ManualShips.Active`, `Ships` | Use a fixed fleet per expedition instead of the automatic one. |
| `FuelToCarry` | Deuterium taken along. |
| `MinDeuteriumOnMoon` | Moons used as expedition origin need at least this much deuterium (0 = no check). Below it the moon is skipped and a Telegram/Discord message is sent (at most every 6 h per moon). |
| `MaxExpeditionsPerOrigin` | Max parallel expeditions per origin. |
| `Origin[].MaxExpeditions` | Optional per origin: max parallel expeditions for this origin. Absent or 0: with one origin `MaxExpeditionsPerOrigin` applies; with several origins the expedition slots are distributed round-robin (13 slots: 2 origins 7+6, 3 origins 5+4+4; capped by `MaxExpeditionsPerOrigin`). |
| `SplitExpeditionsBetweenSystems.Active`, `Range` | Spread targets over systems within this range. |
| `RandomizeOrder` | Random origin order. |
| `IgnoreSleep` | Keep sending expeditions during `SleepMode`. |
| `SleepWave.Active`, `ReturnAt`, `SpeedPercent` | One extra expedition wave right before the bot falls asleep (`SleepMode`, and the pre-sleep phase of `GhostSleep`). **`ReturnAt`** (optional) is the time the wave should be back: `"WakeUp"`, `"WakeUp+15"` (15 min after waking up), `"WakeUp-10"` or a fixed `"HH:mm"`. For each expedition the bot then picks the combination of target system (within `SplitExpeditionsBetweenSystems.Range`) and speed whose return is closest to that time; the log shows the chosen system, speed and the deviation in minutes. Without split systems only the speed can vary, so the match is coarser. **`SpeedPercent`** (10-100, steps of 10) is used when `ReturnAt` is empty, not understood or cannot be calculated. Only free expedition slots are filled; if `SleepMode.PreventIfThereAreFleets` is on, going to sleep is first delayed while fleets would return during the sleep window, so the wave then usually finds all slots free. The return time is the normal case: random delay / early-return events of expeditions cannot be predicted (the delay depends on the holding time, not on the speed).
| `WaitForAllExpeditions`, `WaitForMajorityOfExpeditions` | Send a new round only after all (or more than half of) the expeditions are back. |
| `MinWaitNextFleet`, `MaxWaitNextFleet` | Seconds between two fleets of a round. |
| `MinWaitNextRound`, `MaxWaitNextRound` | Seconds added after the earliest return before the next round. |

## AutoFarm

Spies inactive players and attacks the profitable ones. See the [quick start](#new-account-quick-start) for a new account.

| Field | Meaning |
|---|---|
| `Active` | Switch. |
| `Origin` | Celestials the fleets start from. |
| **Scan area** | |
| `ScanRange` | Galaxy/system ranges to scan. |
| `SystemRange` | Radius around the origin; if set it replaces `ScanRange`. 0 = use `ScanRange`. |
| `Exclude` | Coordinates never touched. |
| `ExcludeMoons` | Skip moons. |
| `DistanceBasedScanOrder` | Scan the nearest systems first. |
| `ScanBlockSize` | Stop scanning after this many systems and attack first (0 = off). |
| `TargetsProbedBeforeAttack` | Attack after this many probed targets (0 = off). |
| `SequentialMode`, `SequentialModeProbesPerCycle` | Handle one target per cycle (probe, then attack) instead of scanning first; probes per cycle. |
| `StopAfterFullScan` | Stop the worker after one full pass. |
| **Target choice** | |
| `MinimumResources` | Minimum loot to attack a target. |
| `MinimumResourcesDefended` | Minimum loot to attack a defended target. |
| `MinimumPlayerRank` | Ignore players ranked better (smaller number) than this. |
| `MaxFlightTime` | Maximum flight time in seconds; 0 = no limit. |
| `MinLootFuelRatio` | Loot must be at least this multiple of the fuel cost. |
| `PreferedResource` | `Metal`, `Crystal` or empty; sort targets by that resource. |
| `AttackDefendedTargets` | Attack targets with fleet/defence (needs the escort simulation). |
| `MaxDefensePoints` | Skip targets above this defence value. |
| `MinLootPerDefensePoint` | Skip defended targets whose loot per defence point is too low. |
| `KeepReportFor` | Minutes a spy report stays valid. |
| `MaxReportAgeMinutesForAttack` | Hard limit for the age of a spy report right before the defence check (default 120, 0 = off). Older reports are not attacked; the target is queued for a fresh probe. |
| `MaxAttackDelaySec` | Maximum delay an attack may be postponed to fit the timing (default 600). |
| `DebrisCreditPercent` | Share of the debris field that counts as profit in the profit calculation, minus recycler fuel (default 80). |
| `MinDebrisToHarvest` | Minimum debris after recycler fuel before recyclers are sent (default 0). |
| `MinSpeedPercentForProfitRetry` | Lowest fleet speed (%) the profit re-check may fall back to (default 50). |
| `ReduceDeuterium` | `true` = save deuterium: the attack fleet (cargo + escort) and the recyclers fly at the lowest sensible speed. Sensible means: `MaxFlightTime` is kept, and the recycler arrives at most `RecyclerArrivalGapMaxSec` after the main fleet (it waits at home if it would arrive too early). Never faster than the normal calculation, never below `ReduceDeuteriumMinSpeedPercent` (default `false`). |
| `ReduceDeuteriumMinSpeedPercent` | Lowest speed (%) `ReduceDeuterium` may use. Values below 30 are treated as 30 (default 30). |
| `DeuteriumGuard` | `true` = low-deuterium guard: if a start celestial has less than `DeuteriumGuardMin` deuterium, attacks on defended targets (escort) from it are paused; if no start celestial has enough, the speed fleet (`UseSpeedFleet`) is switched off too. The AutoFarm page shows the stock and the warning "Deuterium too low for bigger fleets" (default `true`). |
| `DeuteriumGuardMin` | Deuterium threshold for the guard (default 10000000). |
| `DeuteriumGuardOverride` | `true` = bridge the guard manually and keep working with low deuterium (default `false`). |
| `GlobalSpacing` | Spreads several bot instances over different start points of the scan range. With one instance leave it `false`: every pass then starts at the first `ScanRange` entry. |
| `ReportQueryLimitForNonActionable` | Limits how many reports of non-attackable targets are queried per run (default 3). |
| `RepeatedLogIntervalMinutes` | Repeated identical log messages appear at most this often (default 360; 0 = every time). |
| `Blacklist.*` | Temporary blacklist: `Active`, `ResetAfterHours`, `UnprofitableResetAfterHours`, `PermanentResetAfterHours`. |
| `AllianceWhitelist.*` | Never spy or attack members of your own alliance (and optional partners): `Active`; `ServerHost` = host of your universe (e.g. `sNNN-xx.ogame.gameforge.com` (NNN = universe number, xx = language code of your server)); `AllianceId` = **your alliance ID** (the `id` of your alliance in `https://<ServerHost>/api/alliances.xml`); `AdditionalAllianceIds` = further alliance IDs to protect, e.g. NAP partners (list). The member lists come from OGame's public API (reloaded about every 20 h, the API itself updates once a day). The check runs in the shared target filter, so it covers scanning, spying and attacking; known targets of members are removed from the farm list. Moons are skipped when their planet belongs to a member. Both `ServerHost` and a valid `AllianceId` are required when `Active` is true (otherwise a warning is logged and nothing is filtered). |
| **Probes and slots** | |
| `NumProbes` | Probes per spy mission. |
| `BuildProbes` | Build missing probes. |
| `FillSlots`, `FillSlotsMaxProbeWaitSec` | Keep all allowed slots busy; maximum seconds to wait for probe reports. |
| `MaxSlots`, `SlotsToLeaveFree` | Slots AutoFarm may use / keeps free. |
| **Cargo and fleet** | |
| `FleetSpeed` | Speed in percent (10-100). |
| `CargoType` | Cargo ship type (`SmallCargo`, `LargeCargo`, …). |
| `MinCargosToSend`, `MinCargosToKeep` | Minimum ships per attack; ships kept at home. |
| `CargoSurplusPercentage` | Extra cargo on top of the calculated amount. |
| `CargoReservePercent`, `RecyclerReservePercent` | Share of ships held back as a reserve. |
| `BuildCargos` | Build missing cargo ships. |
| `UseSpeedFleet` | Use Pathfinders as cargo; new accounts: `false`. |
| `MaxCargoLossPercent` | Maximum accepted loss of cargo value. |
| `FleetToDebrisPercent` | Expected share of fleet losses that becomes debris (used to plan recyclers). |
| `AutoHarvestDebrisAfterAttack` | Send recyclers to the debris field of the attack. |
| **Escort and simulation (defended targets only)** | |
| `TargetCombatTechBonus` | Combat tech levels added to the target in simulations (safety margin). |
| `EscortShipTypes`, `EscortFallbackShipTypes` | Warship types used as escort and their fallbacks. |
| `EscortShipCaps` | Absolute maximum per ship type. |
| `EscortShipsStep`, `MaxEscortShips`, `MaxEscortFleetPercent` | Step size, absolute and relative cap for the escort. |
| `EscortReservePercent` | Share of escort ships held back. |
| `EscortMaxIterations`, `EscortBisectionIterations`, `EscortFineTuneMaxSims` | Search effort for the best escort size. |
| `MaxEscortLossPercent` | Highest simulated loss, in % of the value of the whole attack fleet. Escort compositions above it are rejected. `0` = limit OFF (any loss is accepted). |
| `EscortLossWeight` | Weight of ship losses versus fuel (1 = equal, >1 prefers a larger escort). |
| `MinWinRate` | Minimum simulated win rate (0.9 = 90 %). |
| `SimulationRuns` | Battles simulated per check. |
| **Timing** | |
| `CheckIntervalMin/Max` | Minutes between two runs. |

## AutoHarvest

| Field | Meaning |
|---|---|
| `Active` | Switch. |
| `HarvestOwnDF`, `HarvestForeignDF`, `HarvestDeepSpace` | Which debris fields to harvest. |
| `MinimumResourcesOwnDF`, `…ForeignDF`, `…DeepSpace` | Minimum content worth a trip. |
| `DeepSpaceRange` | Systems around the origin searched for deep-space fields. |
| `MaxSlots` | Slots used. |

## AutoColonize

| Field | Meaning |
|---|---|
| `Active` | Switch. |
| `Origin` | Where colony ships start. |
| `SlotsToLeaveFree` | Slots kept free for this worker's rounds. |
| `Abandon.*` | Abandon new planets that are too small or the wrong temperature: `Active`, `MinFields`, `MinTemperatureAcceptable`, `MaxTemperatureAcceptable`. The home planet (lowest planet ID) is never abandoned and never skipped by AutoMine for this reason. |
| `IntensiveResearch.*` | Colonise in bursts: `Active`, `MaxSlots`, `MinWaitNextFleet`, `MaxWaitNextFleet`. |
| `Targets` | Ranges to colonise: `Galaxy`, `StartSystem`, `EndSystem`, `StartPosition`, `EndPosition`, `MaxPlanets` (stop after this many colonies in that range). |
| `RandomPosition` | Pick positions randomly inside the targets. |

## AutoDiscovery

| Field | Meaning |
|---|---|
| `Active` | Switch. |
| `Origin` | Celestials discovery missions start from. |
| `MaxSlots` | Slots used. |
| `StopAtArtifacts` | Stops sending discoveries once at least this many artifacts are collected (the "Collected artifacts: 1840 / 3600" counter on the lifeform pages). `0` = no limit. Discoveries resume when the count drops below again. If the counter cannot be read, discoveries keep running (warning in the log). |

## FastFarm

Resource farming mode with one goal: collect as much deuterium as possible. It works in rounds per galaxy: scan all systems for inactive players, spy every target once, sort by weighted loot, then re-spy the best ones with fresh probes right before the attack (`ProbesPerTarget` probes per fleet) and raid them with small and/or large cargos; targets whose loot shrank by `ShrinkDiscardPercent` or more are dropped, attacked ones leave the storage. Stored targets can be sorted and deleted in the WebUI. While `Active` is on, all other workers except the Defender stay off (`OnlyWorker`). The WebUI menu entry **AutoFarm** then shows the FastFarm overview instead of the AutoFarm report; the switch is on top of the AutoFarm page and on the Config page.

| Field | Meaning |
|---|---|
| `Active` | Master switch of the mode. |
| `OnlyWorker` | `true` (default) = every other worker except Defender and sleep mode stays off. `false` = they keep running and compete for slots. |
| `KeepExpeditions` | Keeps the expeditions running although the other workers are off. |
| `BuildCheckMinutes` | Default 60. While FastFarm is active, AutoMine, AutoResearch, LifeformAutoMine and LifeformAutoResearch run when transports with resources are on their way to your celestials, and otherwise once every this many minutes to build what is possible. They only spend resources already on the celestial and order no new transports during FastFarm. 0 = they stay off. |
| `Origin` | Celestials the probes and cargo ships start from. The nearest one with enough ships is used per target. Empty = all planets. |
| `ScanRange` | Galaxy and system ranges that are scanned, nearest to the origins first. Empty = 100 systems around every origin. |
| `CargoType` | `SmallCargo` (default), `LargeCargo` or `Both` (small cargos first, large cargos for the rest). |
| `ProbesPerTarget` | Probes per spying fleet (default 20). |
| `SlotsToLeaveFree` | Slots that are never used, so the Defender can always send (default 1). |
| `WeightMetal`, `WeightCrystal`, `WeightDeuterium` | Weights of the loot value that ranks the targets (default 3 : 2 : 1, the usual MSE ratio). |
| `MinLoot` | Minimum lootable resources (metal + crystal + deuterium) for an attack. |
| `MinDeuterium` | Minimum lootable deuterium for an attack. |
| `TopTargets` | Highest number of attacks per round. |
| `ReportMaxAgeMinutes` | A fresh check report older than this is not attacked; the target is checked again. |
| `DeleteReports` | Deletes each report after reading it (keeps the inbox from filling up). |
| `FleetSpeedPercent` | Speed of the cargo fleets (10-100). |
| `CargoSurplusPercent` | Extra cargo space on top of the expected loot. |
| `MinFillPercent` | A target is skipped when the available ships carry less than this share of its loot. |
| `MaxSystemsPerScanStep` | Systems the full scan of a round visits per step. |
| `ShrinkDiscardPercent` | Drop a target when its fresh weighted loot is at least this many percent below the value from the full sweep (default 10). `MinLoot`/`MinDeuterium` still apply. |

State and statistics are kept in `data/fastfarm_<Alias>.json` (written by the worker, shown on the overview page).

## Experimental

New functions that are still in test. They have their own page in the WebUI (**Testing**), so new and proven functions stay apart. When a function has proven itself it moves to the normal page of its area. The block is optional: without it the defaults below apply.

| Field | Meaning |
|---|---|
| `BotPause.Active` | Switches the pause function on or off (default on). |
| `BotPause.DurationMinutes` | Button **Pause** in the browser tile and Telegram `/pause [minutes]`: all workers except Defender and SleepMode hold their cycles for this long (default 15) and continue on their own (with `Mode` = `Stop` they are stopped and restarted). `/resume` or the button ends it earlier; pressing Pause again prolongs it. Recalls, fleetsave and the Defender keep running. |
| `BotPause.RecallMarginMinutes` | Safety distance (default 2). A pause is refused if a recall is due within the pause time plus this margin. A pause is also refused while sleeping and during an attack lockdown. |
| `BotPause.MaxDurationMinutes` | Longest pause you can ask for with `/pause` (default 120). |
| `BotPause.StopDefender` | Default off. On: the Defender stops during the pause too. Fleetsave and recalls already planned still run. |
| `BotPause.Mode` | `Gate` (default): workers are not stopped; during the pause each one skips its cycle and runs again after the pause, staggered by up to 60 seconds. `Stop`: the old hard pause (all workers stopped and restarted). |
| `BotPause.AttackMarginMinutes` | Default 15. A pause is refused if an attack arrives within this time (with `StopDefender` on: plus the pause length). While paused, spy, attack, expedition, colonize, discovery and harvest missions are blocked (fleetsave, transports and Telegram commands stay possible). |
| `DefenderRecheck.Active` | Default off. Shortly before the fleetsave the Defender fetches the attack list once more: attack recalled -> fleetsave skipped; attack later -> fleetsave moved; question fails or times out -> fleetsave sent as planned. |
| `DefenderRecheck.TimeoutSeconds` | How long to wait for the answer (default 4, 1-10). |
| `ExtraDebugLog.Active` | Default on (a missing block counts as on; `false` switches it off). Files older than 7 days are deleted automatically. Writes extra diagnosis lines to their own file `devlog/TBot-debug-<date>.log` (anonymized: player, universe, celestial names, mail, IP, tokens; coordinates become x:x:position, amounts stay) (max. 50 MB per day), the normal log stays small. Topics (all default on, they only apply when the log is active): `Repatriate`, `Ledger`, `BotPause`, `DefenderRecheck`, `FleetsaveSlot`, `MinePlanRanking`, `AsteroidHunt`, `ShadowFleet`, `GalaxyCache`, `FleetTargets`, `Requests` (request counters every 10 minutes), `BrowserProxy` (error 100 retries of the browser proxy) - one for every experimental function. Useful when a function does not send and you need to see why. |
| `RepatriateLedgerHold.Ignore` | Default off. On: repatriate, reverse repatriate and `/collect` ignore the resource ledger completely (all stock is free). |
| `RepatriateLedgerHold.MinPercent` | Default 30. Repatriate and /collect leave stock alone that a waiting build claim already holds at least this share of its total need (metal+crystal+deuterium). Claims below that (far-away goals such as a metal mine to 995 M) no longer freeze the whole stock. Claims with flights or fully covered always protect. 0 = every held amount is protected (old behavior), 100 = only fully covered claims. |
| `MinePlanRanking.Mode` | How the page Minenplan ranks the next investments (display only): `Payback` (default, like AutoMine), `Cheapest` (lowest cost first), `Npv` (highest gain over the horizon minus cost). The page can override this in your browser. |
| `MinePlanRanking.HorizonDays` / `MaxPaybackDays` | Horizon for `Npv` (default 90 days); longest payback that counts as sensible (default 1000 days). |
| `MinePlanRanking.OnlySensible` / `IncludeResearch` | Hide steps with a longer payback than the limit; rank plasma technology next to the mine steps (default on). |
| `MinePlanRanking.UsePlanForAutoMine` / `PlanLookahead` | Default off / 12. When on, AutoMine only builds a mine on a planet that is among the first `PlanLookahead` steps of the build plan (page Minenplan: best payback first across all planets). Other planets wait. A planet builds the mine its own plan step names instead of the mine the old ROI logic picked, but only within the AutoMine limits (max mine level, `MaxDaysOfInvestmentReturn`); energy and facility priorities of AutoMine stay in front. Without a fresh plan (older than 6 hours or empty) nothing is held back. Only mines are held; research, power plants and facilities run as before. Lifeform buildings are not part of the plan yet. |
| `MinePlanRanking.DailyBudgetDeut` | Default 0. Daily budget in deuterium equivalent (for example your expedition income) for the build plan on the page Minenplan. 0 = the page asks for it and remembers it in your browser. The plan is display only. |
| `FleetsaveSlot.Active` | Default on. The last free fleet slot is kept for the fleetsave (and DebrisSave): all other fleets, even forced ones, are refused when only the reserved slots are left. Refusals show in the log as `SLOTCOLLISION #n` (counter since start) - this also tells whether a central slot ledger would be worth it. |
| `FleetsaveSlot.Slots` | Number of reserved slots (default 1). Works on top of `General.SlotsToLeaveFree`. |
| `AsteroidHunt.Active` | Default off. Item event: once per game day one ship attacks the asteroid on position 17 of your own system. After arrival the inventory is compared with the one from before; one short log line per attempt, no messenger message. Assumes one find per day and account. |
| `AsteroidHunt.ShipType` / `ShipCount` | Ship sent (default `Pathfinder`, 1). Any ship name of the game works (e.g. `Cruiser`, `Reaper`, `Destroyer`); no espionage probes. The planet with the most of that ship is used. |
| `AsteroidHunt.MaxAttemptsPerDay` / `SearchSystems` | Attempts per game day (default 0 = unlimited, max 60; the hunter stops for the day after the first find); `SearchSystems` (default 10) = how many systems around the home system are searched for the asteroid (own, +1, -1, +2 ...); a refused target costs no attempt, the last hit system is tried first the next day. |
| `AsteroidHunt` (Erkennung) | The result is read from the report of the Flottenkommando under Messages -> Sonstiges (found / no result, retry / "sleep on it" = day over). If no new report arrives or the text is not recognised, the inventory before and after the attack is compared instead. |
| `AsteroidHunt.SaveJson` | Default on. Log of all attempts and finds in `data/asteroiden_funde_<alias>.json`. |
| `GalaxyCache.Active` | Default on. Shared store of all galaxy scans (`data/galaxycache_<alias>.json`). Every scan of any worker is saved with a time stamp; FastFarm and Colonize read the cache first. Off: everything queries live again. |
| `GalaxyCache.MaxAgeHours` | Default 12. How old an entry may be to be used instead of a live query. |
| `GalaxyCache.RangeSystems` / `DelayMinSeconds` / `DelayMaxSeconds` | Background scan: systems left and right of every own system (default 15, 0 = only passive recording) and the pause between two queries (default 20-45 s). Pauses while the bot sleeps or is paused. |
| `GalaxyCache.UseInFastFarm` / `UseInColonize` | Default on. FastFarm takes the inactive-target scan from a fresh cache. Colonize skips positions the cache shows as occupied; free positions are still checked live. |
| `GalaxyCache.Events` / `Telegram` | Default on. Changes between two scans of a system (new colony, planet gone, new moon, status change, move = a player disappears at one place and appears at another within 72 h) go to the log; new colonies and moves also to Telegram / Discord. Your own colonies are ignored. The first scan of a system is only the baseline. |
| `ShadowFleet.Active` | Default off. **Not fully tested - use with care.** **Needs a deuterium reserve on the chosen celestial** - every flight burns fuel, and the fleet carries deuterium for `DeuteriumFlights` flights. The chosen ships of ONE celestial fly one after another to the nearest debris field (harvest mission, at `SpeedPercent`), are recalled after half of the planned time and fly again after a short random pause. Only one celestial is covered, so only its deuterium and one fleet slot are used. |
| `ShadowFleet.Celestial` | `Galaxy`, `System`, `Position`, `Type` (`Planet` or `Moon`) of the one celestial that is secured. |
| `ShadowFleet.Ships` | Ships that fly, as `{ "Recycler": 0, "LargeCargo": 500 }`. A ship is in the fleet when it is listed; the number is the amount, **0 = all of them**. At least one `Recycler` (or `Pathfinder`) is needed for the harvest mission. In the WebUI: tick the ship and enter the amount. |
| `ShadowFleet.SpeedPercent` | 10 to 100, default 10 (slowest, the cheapest fuel). |
| `ShadowFleet.MinBackInMinutes` / `MaxBackInMinutes` | One flight (out and back) lasts a random time between these two values (default 20 to 60). The time limit: the fleet is back home after at most `MaxBackInMinutes`. |
| `ShadowFleet.PauseMinSeconds` / `PauseMaxSeconds` | Random pause at home between two flights (default 30 to 180). |
| `ShadowFleet.DeuteriumFlights` | Deuterium for this many flights goes along as cargo (default 3, limited by cargo space and stock). With 2 or more flights left in stock a low stock only produces a log warning; with exactly one flight left a messenger warning follows; without deuterium the flights wait and a messenger message is sent once. |
| `ShadowFleet.WindowFrom` / `WindowTo` | Optional time window (game time, `HH:mm`, may cross midnight, e.g. `22:00` to `07:00`). Empty = always. A flight is only started if it is back before the window ends. |
| `ShadowFleet.DefenderOff` | Default off. Trap mode, applies only to the shadow fleet of this celestial. On = the Defender leaves the shadow fleet alone (it is not sent away) but still saves the resources and all other ships. When an attack on the celestial is announced, a flying shadow fleet is called home at once if it can be back before impact, so it is there when the attack lands (but was not there while the attacker looked). No new flight starts while the attack is pending. Sleep-time fleetsaves (SleepMode/GhostSleep) still send everything away. |
| `ShadowFleet` (attack guard) | With `DefenderOff` off: no flight starts if an attack on the celestial would land before the fleet is back; a fleet that is already flying keeps flying (recall cancelled) when an attack lands before its return. The fleet then harvests and comes home by itself. The attack list comes from the Defender (its check interval limits how early the shadow fleet learns of an attack). |

## WebUI.Language (settings.json)

`"WebUI": { "Language": "en" }` sets the interface language: `en` (built in, default) or the code of a language file. It is changed with the language selector in the menu bar, which writes this value. Language files are `lang/<code>.json` next to `settings.json` (shipped: `lang/de.json`). One file holds (1) all WebUI texts - menu, pages, labels, messages, (2) the help texts of the Config page and (3) the texts of the browser tiles (ogamed reads the same file from the folder of the `ogamed` binary). Texts without a translation stay English. A new language is a copy of `de.json` with another `name` and translated values; it then shows up in the selector. Changes need no rebuild (reload the page). Logs, Telegram messages and texts that the bot itself writes into its status files stay English. See `config/lang/README.md`.

## WebUI.FormPages (settings.json)

The WebUI page **Config** shows a form view per block (each block also has a JSON tab). Blocks with credentials are never shown as a form. The former Settings page (raw JSON editor) has been removed.
