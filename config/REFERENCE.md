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

**SpeedFleet.** `UseSpeedFleet: true` makes AutoFarm use the first valid ship type of `SpeedFleetCargoTypes`
(default `Pathfinder`) as its cargo ship instead of `CargoType`. Pathfinders are fast and carry cargo, which helps with
far targets, but you need to have researched and built them. Without Pathfinders the setting does nothing useful
(or logs a warning and falls back to `CargoType`), so a new account should keep it `false`.

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
| `CaptchaAPIKey` | API key of the captcha solving service (optional). |
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

### Brain.AutoCargo

Builds cargo ships.

| Field | Meaning |
|---|---|
| `Active` | Switch. |
| `CargoType` | Ship type to build. |
| `MaxCargosToBuild`, `MaxCargosToKeep` | Total target count / how many to keep. |
| `LimitToCapacity` | Only build as many as the stored resources need. |
| `SupplyMoonFromPlanet` | Moons get ships from the planet at the same position. |
| `ExcludeMoons`, `RandomOrder`, `SkipIfIncomingTransport`, `Exclude` | Skip moons; random order; skip a celestial that already has a transport coming; excluded celestials. |

### Brain.AutoDefence

| Field | Meaning |
|---|---|
| `Active` | Switch. |
| `DefenceToReach.*` | Target count per defence type (launchers, lasers, cannons, turret, missiles). Shield domes are `true/false`. 0 = do not build. |
| `ExcludeMoons`, `RandomOrder`, `Exclude` | Skip moons; random order; excluded celestials. |

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
| `Reverse.*` | Reverse repatriation (fetch resources back out for a build): `Active`, `ReturnFuelFactor`, `ReturnAllShips`, `KeepShips`, `PendingTtlMinutes`, `OutboundCapacityFactor`. Advanced. |
| `ExcludeMoons`, `RandomOrder`, `SkipIfIncomingTransport`, `Exclude` | As above. |

### Brain.BuyOfferOfTheDay and Brain.AutoFleetJumpGate

| Field | Meaning |
|---|---|
| `BuyOfferOfTheDay.Active` | Buys the daily merchant offer. |
| `AutoFleetJumpGate.Active` | Switch. |
| `AutoFleetJumpGate.Target` | Moon the fleet jumps to. |
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
| `UseSpeedFleet`, `SpeedFleetCargoTypes` | Use fast ships (default Pathfinder) as cargo; new accounts: `false`. |
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
| `MaxEscortLossPercent` | Maximum accepted escort loss (0 = unlimited). |
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

## WebUI.FormPages (settings.json)

The WebUI page **Config** shows a form view per block (each block also has a JSON tab). Blocks with credentials are never shown as a form. The former Settings page (raw JSON editor) has been removed.
