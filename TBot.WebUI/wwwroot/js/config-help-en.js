window.CFG_HELP = window.CFG_HELP || {};
window.CFG_HELP.en = {
 "AutoFarm": {
  "SystemRange": {
   "t": "Scan only the systems within this distance of your start celestials, instead of the fixed scan range. 0 = use the scan range.",
   "e": "5 = up to 5 systems in each direction of every start celestial."
  },
  "ExcludeMoons": {
   "t": "Moons are neither spied on nor attacked, only planets."
  },
  "DistanceBasedScanOrder": {
   "t": "Scan the nearest systems first. Near targets mean shorter flights and less fuel."
  },
  "ScanBlockSize": {
   "t": "Pause scanning after this many systems, attack with what was found, then continue. 0 = off."
  },
  "TargetsProbedBeforeAttack": {
   "t": "Start attacking as soon as this many targets have been probed, instead of finishing the scan first. 0 = off."
  },
  "SequentialMode": {
   "t": "Work on one target at a time (probe, wait for the report, attack) instead of scanning first. Slower, but needs very few fleet slots."
  },
  "SequentialModeProbesPerCycle": {
   "t": "Only in sequential mode: how many targets are probed per cycle."
  },
  "StopAfterFullScan": {
   "t": "AutoFarm stops by itself after it has scanned the whole range once."
  },
  "MinimumResources": {
   "t": "Only attack an undefended target if there is at least this much loot to take away (as shown by the spy report).",
   "e": "2,000,000 = targets with less loot are ignored."
  },
  "MinimumResourcesDefended": {
   "t": "The same minimum for targets that have ships or defence. Set it higher than the value above, because these fights can cost you ships."
  },
  "MinimumPlayerRank": {
   "t": "Players with a better rank (a smaller number) than this are skipped.",
   "e": "3000 = a player on rank 1500 is skipped, one on rank 5000 is farmed."
  },
  "MaxFlightTime": {
   "t": "Longest allowed one-way flight in seconds. Farther targets are skipped. 0 = no limit.",
   "e": "7200 = 2 hours."
  },
  "MinLootFuelRatio": {
   "t": "The loot must be worth at least this many times the fuel cost. The bot picks the fastest speed that still meets it.",
   "e": "1.5 = for 100,000 deuterium of fuel there must be at least 150,000 of loot."
  },
  "PreferedResource": {
   "t": "Targets with the most of this resource come first. Empty = no preference."
  },
  "AttackDefendedTargets": {
   "t": "Also attack targets that have ships or defence. The bot simulates the fight and sends an escort of warships along with the cargo ships. Needs the escort settings below.",
   "e": "Costs more deuterium and can lose ships."
  },
  "MaxDefensePoints": {
   "t": "Targets whose defence is worth more than this are skipped."
  },
  "MinLootPerDefensePoint": {
   "t": "Defended targets are skipped when their loot is too small compared to their defence. Higher = pickier."
  },
  "KeepReportFor": {
   "t": "Minutes an espionage report counts as valid before the target has to be probed again."
  },
  "MaxReportAgeMinutesForAttack": {
   "t": "Right before an attack, a report older than this is discarded and the target is probed again, because the defence may have changed. 0 = off."
  },
  "MaxAttackDelaySec": {
   "t": "Longest time in seconds an attack may be held back so that the recyclers arrive right behind it."
  },
  "DebrisCreditPercent": {
   "t": "How much of the expected debris field counts as profit when the bot decides whether an attack pays off (after recycler fuel).",
   "e": "80 = 80 % of the debris are counted."
  },
  "MinDebrisToHarvest": {
   "t": "Recyclers are only sent if the debris field is larger than this, after their fuel cost. 0 = whenever there is debris."
  },
  "MinSpeedPercentForProfitRetry": {
   "t": "If an attack does not pay off at the chosen speed, the bot tries slower speeds down to this value, because slower flights burn less fuel."
  },
  "GlobalSpacing": {
   "t": "Only for several bot instances: spreads them over different start points of the scan range. Leave it off with one instance."
  },
  "ReportQueryLimitForNonActionable": {
   "t": "How many reports of targets that cannot be attacked are read per run. Lower = faster runs."
  },
  "RepeatedLogIntervalMinutes": {
   "t": "The same log message appears at most this often. 0 = every time."
  },
  "NumProbes": {
   "t": "Espionage probes sent per target."
  },
  "BuildProbes": {
   "t": "Build probes automatically when too few are at home."
  },
  "FillSlots": {
   "t": "Keep all allowed fleet slots busy with probes and attacks."
  },
  "FillSlotsMaxProbeWaitSec": {
   "t": "How long to wait for probe reports (seconds) before free slots are used otherwise."
  },
  "MaxSlots": {
   "t": "Most fleet slots AutoFarm may use at the same time."
  },
  "SlotsToLeaveFree": {
   "t": "Fleet slots AutoFarm never uses, so that expeditions and other workers still have room."
  },
  "FleetSpeed": {
   "t": "Fixed speed for attacks when no better speed is calculated. 100 % is fastest and burns the most deuterium."
  },
  "CargoType": {
   "t": "The cargo ship type that carries the loot."
  },
  "MinCargosToSend": {
   "t": "No attack is started with fewer cargo ships than this."
  },
  "MinCargosToKeep": {
   "t": "Cargo ships that always stay at home."
  },
  "CargoSurplusPercentage": {
   "t": "Extra cargo space on top of the calculated amount, as a safety margin."
  },
  "CargoReservePercent": {
   "t": "Share of the cargo ships that is held back as a reserve."
  },
  "RecyclerReservePercent": {
   "t": "Share of the recyclers that is held back as a reserve."
  },
  "BuildCargos": {
   "t": "Build cargo ships when there are not enough."
  },
  "UseSpeedFleet": {
   "t": "Use fast ships (Pathfinders) as cargo ships so that attacks arrive sooner. They burn more deuterium. The low-deuterium guard switches this off by itself when deuterium is low."
  },
  "SpeedFleetCargoTypes": {
   "t": "Old setting that this version of the bot no longer reads. It has no effect and can be removed from the config. The speed fleet always uses Pathfinders."
  },
  "ReduceDeuterium": {
   "t": "Save deuterium: the attack fleet and the recyclers fly as slowly as makes sense, never below the lowest speed set below. Flights take longer and slots stay busy longer.",
   "e": "Useful when deuterium is short."
  },
  "ReduceDeuteriumMinSpeedPercent": {
   "t": "The slowest speed that 'Save deuterium' may use. Never below 30 %."
  },
  "DeuteriumGuard": {
   "t": "If a start celestial has less deuterium than the minimum below, attacks on defended targets from it are paused. If no start celestial has enough, the speed fleet is switched off as well. The AutoFarm page then shows 'Deuterium too low for bigger fleets'."
  },
  "DeuteriumGuardMin": {
   "t": "The deuterium threshold of the guard.",
   "e": "10,000,000 = the guard acts below 10 million deuterium."
  },
  "DeuteriumGuardOverride": {
   "t": "Bridge the guard by hand and keep farming even with low deuterium. The page still shows a hint."
  },
  "TargetCombatTechBonus": {
   "t": "Extra combat tech levels assumed for the target in fight simulations, as a safety margin. Higher = more careful."
  },
  "EscortShipTypes": {
   "t": "Warships that may go along as escort, in order of preference. Comma separated."
  },
  "EscortFallbackShipTypes": {
   "t": "Used only when the escort ships above are not enough."
  },
  "EscortShipsStep": {
   "t": "The escort is searched in steps of this many ships. Smaller = more exact but slower."
  },
  "MaxEscortShips": {
   "t": "Largest number of ships of one type in an escort, when no cap is set for that type below."
  },
  "MaxEscortFleetPercent": {
   "t": "At most this share of the ships of one type at home may go along as escort.",
   "e": "100 = no limit by share; 20 = at most a fifth of them."
  },
  "EscortReservePercent": {
   "t": "Safety buffer: this much extra is added to the calculated escort where ships are available."
  },
  "MaxEscortLossPercent": {
   "t": "Highest simulated loss, as a share of the value of the whole attack fleet. Escorts that would lose more are rejected. 0 = limit off (any loss is accepted).",
   "e": "10 = reject an escort that would lose more than 10 % of the fleet value."
  },
  "EscortLossWeight": {
   "t": "How much ship losses count compared to fuel when the bot picks the cheapest escort. Higher = it prefers a bigger escort that loses less.",
   "e": "3 = a lost ship counts three times as much as the same value in fuel."
  },
  "MinWinRate": {
   "t": "The simulated fight must be won at least this often.",
   "e": "0.9 = in 90 % of the simulated fights."
  },
  "SimulationRuns": {
   "t": "How many fights are simulated per check. More = more reliable, slower."
  },
  "EscortMaxIterations": {
   "t": "Search effort for the best escort size: maximum number of steps. Higher = better result, more computing time."
  },
  "EscortBisectionIterations": {
   "t": "Search effort: how finely the escort size is narrowed down. Higher = more exact, slower."
  },
  "EscortFineTuneMaxSims": {
   "t": "Search effort: simulations used for the final fine-tuning of the escort."
  },
  "FleetToDebrisPercent": {
   "t": "Expected share of fleet losses that ends up as a debris field. Used to plan the recyclers."
  },
  "MaxCargoLossPercent": {
   "t": "Highest accepted loss of cargo ships, as a share of their value, in the simulation."
  },
  "AutoHarvestDebrisAfterAttack": {
   "t": "Send recyclers to the debris field after the attack."
  },
  "CheckIntervalMin": {
   "t": "Minutes between two AutoFarm runs. The bot picks a random time between minimum and maximum."
  },
  "CheckIntervalMax": {
   "t": "Minutes between two AutoFarm runs. The bot picks a random time between minimum and maximum."
  },
  "ScanRange": {
   "t": "Galaxy and system ranges that are scanned for targets."
  },
  "Exclude": {
   "t": "Coordinates that are never spied on or attacked."
  },
  "Blacklist.ResetAfterHours": {
   "t": "Hours until a target on the blacklist is allowed again."
  },
  "Blacklist.UnprofitableResetAfterHours": {
   "t": "Hours until a target that was not worth attacking is checked again."
  },
  "Blacklist.PermanentResetAfterHours": {
   "t": "Hours until a permanently blacklisted target is released. 0 = never."
  },
  "ServerHost": {
   "t": "Host name of your universe, used to read the alliances of other players."
  },
  "AdditionalAllianceIds": {
   "t": "Alliance IDs that are never attacked, for example NAP partners."
  },
  "EscortCap": {
   "t": "Absolute upper limit for this escort ship type. Empty = only the general limit \"Max escort ships\" counts."
  }
 },
 "*": {
  "Active": {
   "t": "Master switch of this function. Off = the bot does nothing here."
  },
  "CheckIntervalMin": {
   "t": "Shortest pause in minutes between two runs of this function."
  },
  "CheckIntervalMax": {
   "t": "Longest pause in minutes between two runs. The bot picks a random time between minimum and maximum so it does not act like a clock."
  },
  "Galaxy": {
   "t": "Galaxy number of the coordinates."
  },
  "System": {
   "t": "System number of the coordinates."
  },
  "Position": {
   "t": "Position (1-15) inside the system."
  },
  "Type": {
   "t": "Planet or Moon at these coordinates."
  },
  "Exclude": {
   "t": "Celestials (planets or moons) this function never touches. One row per celestial."
  },
  "Transports.Active": {
   "t": "Allow this function to fetch missing resources from your other celestials by transport."
  },
  "RandomOrder": {
   "t": "Handle the celestials in random order instead of always the same one first."
  },
  "ExcludeMoons": {
   "t": "Skip moons, only planets are handled."
  },
  "MaxSlots": {
   "t": "Largest number of fleet slots this function may use at the same time."
  },
  "SlotsToLeaveFree": {
   "t": "Fleet slots that are never used by this function, so they stay free for you."
  },
  "CustomTitle": {
   "t": "Old setting that this version of the bot no longer reads. It has no effect and can be removed from the config."
  },
  "SecondaryShip": {
   "t": "Old setting that this version of the bot no longer reads. It has no effect and can be removed from the config."
  },
  "MinSecondaryToSend": {
   "t": "Old setting that this version of the bot no longer reads. It has no effect and can be removed from the config."
  },
  "SecondaryToPrimaryRatio": {
   "t": "Old setting that this version of the bot no longer reads. It has no effect and can be removed from the config."
  },
  "MaxWaitTime": {
   "t": "Old setting that this version of the bot no longer reads. It has no effect and can be removed from the config."
  },
  "MaxFailures": {
   "t": "Old setting that this version of the bot no longer reads. It has no effect and can be removed from the config."
  },
  "RandomizeDestination": {
   "t": "Old setting that this version of the bot no longer reads. It has no effect and can be removed from the config."
  },
  "ResourceReserve.*": {
   "t": "Old setting that this version of the bot no longer reads. It has no effect and can be removed from the config."
  },
  "ResourceReserve": {
   "t": "Old setting that this version of the bot no longer reads. It has no effect and can be removed from the config."
  }
 },
 "Credentials": {
  "Universe": {
   "t": "Name of your universe exactly as shown in the Gameforge lobby.",
   "e": "Fornax"
  },
  "Email": {
   "t": "E-mail address of your Gameforge account."
  },
  "Password": {
   "t": "Password of your Gameforge account. It is stored in secrets/credentials, not in the normal config."
  },
  "Language": {
   "t": "Language code of the game server.",
   "e": "en or de"
  },
  "LobbyPioneers": {
   "t": "Switch on only if your account logs in through the Pioneers lobby."
  },
  "BasicAuth.*": {
   "t": "Only needed if a proxy or server asks for an HTTP login. Normally empty."
  },
  "DeviceConf.*": {
   "t": "Browser fingerprint that the bot shows to the game at login (device name, browser, screen size and so on). Keep it the same over time and do not change it often."
  }
 },
 "General": {
  "Host": {
   "t": "Address of the local ogamed connector that the bot talks to. Normally 127.0.0.1."
  },
  "Port": {
   "t": "Port of the local ogamed connector. Change only if that port is already used."
  },
  "Proxy.Enabled": {
   "t": "Send the bot traffic through a proxy server."
  },
  "Proxy.Address": {
   "t": "Address of the proxy including port.",
   "e": "http://proxy.example.com:8080"
  },
  "Proxy.Type": {
   "t": "Kind of proxy, for example socks5 or http."
  },
  "Proxy.Username": {
   "t": "Login of the proxy, if it needs one."
  },
  "Proxy.Password": {
   "t": "Password of the proxy. Stored in secrets/proxy."
  },
  "Proxy.LoginOnly": {
   "t": "On = the proxy is used only for logging in, game requests go direct. Off = everything goes through the proxy."
  },
  "CaptchaAPIKey": {
   "t": "Key of a captcha solving service. Optional, only needed if the game asks for captchas."
  },
  "SlotsToLeaveFree": {
   "t": "Fleet slots no function may use. They stay free for your own manual flights.",
   "e": "2 = the bot never takes the last 2 slots."
  },
  "SlotPriorityLevel.*": {
   "t": "Who gets a free slot first when several functions want one. A lower number is served first."
  },
  "ResourceReserve.*": {
   "t": "Old setting that this version of the bot no longer reads. It has no effect and can be removed from the config."
  }
 },
 "DiscordWebhook": {
  "Url": {
   "t": "Webhook address of your Discord channel. Treat it like a password."
  },
  "Username": {
   "t": "Name shown as sender of the Discord messages."
  },
  "Active": {
   "t": "Copy the Telegram messages to Discord. This only sends; commands still go through Telegram."
  }
 },
 "TelegramMessenger": {
  "Active": {
   "t": "Switch Telegram messages and commands on or off."
  },
  "API": {
   "t": "Token of your Telegram bot. You get it from @BotFather. Stored in secrets/telegram."
  },
  "ChatId": {
   "t": "ID of the chat the bot talks to. Only this chat may send commands."
  },
  "Logging": {
   "t": "Write sent and received Telegram messages into the bot log."
  },
  "TelegramAutoPing.Active": {
   "t": "Send a regular \"still alive\" message."
  },
  "TelegramAutoPing.EveryHours": {
   "t": "Hours between two alive messages.",
   "e": "6"
  }
 },
 "Messenger": {},
 "SleepMode": {
  "GoToSleep": {
   "t": "Time of day when the bot goes to sleep (server time).",
   "e": "23:00"
  },
  "WakeUp": {
   "t": "Time of day when the bot wakes up again (server time).",
   "e": "07:00"
  },
  "Mode": {
   "t": "Choose how the bot sleeps. SleepMode stops everything. GhostSleep hides the fleet but keeps Expeditions and Defender working. Only one can be active."
  },
  "Active": {
   "t": "Fixed daily sleep window. No fleets are sent while the bot sleeps. Only one of SleepMode and GhostSleep may be active."
  },
  "PreventIfThereAreFleets": {
   "t": "Delay going to sleep while fleets would still be returning during the sleep window."
  },
  "TelegramMessenger.Active": {
   "t": "Send a Telegram message when the bot goes to sleep and when it wakes up."
  },
  "AutoFleetSave.Active": {
   "t": "When going to sleep, send all ships away so they cannot be attacked while you are not watching."
  },
  "AutoFleetSave.OnlyMoons": {
   "t": "Save only the fleets that stand on moons."
  },
  "AutoFleetSave.DeutToLeave": {
   "t": "Deuterium that stays on the celestial. Everything above it goes with the fleet as fuel.",
   "e": "1000000"
  },
  "AutoFleetSave.Recall": {
   "t": "Call the saving fleet back so it is home again at wake-up."
  },
  "AutoFleetSave.DefaultMission": {
   "t": "Mission used for the fleetsave flight, usually Deploy."
  },
  "AutoFleetSave.DeployTarget.*": {
   "t": "Coordinates the fleetsave flies to."
  }
 },
 "GhostSleep": {
  "GoToSleep": {
   "t": "Time of day when the bot goes to sleep (server time).",
   "e": "23:00"
  },
  "WakeUp": {
   "t": "Time of day when the bot wakes up again (server time).",
   "e": "07:00"
  },
  "Active": {
   "t": "Sleep variant: all ships are saved by a ghost flight, but Expeditions and Defender keep working during the night."
  },
  "PreSleepExpeditionMinutes": {
   "t": "Old time based pre-phase. Minutes before sleep in which all expedition slots are refilled. Ignored while \"Let expeditions finish before sleep\" is on. 0 = off."
  },
  "AnchorMode": {
   "t": "How the Defender decides when to check for attacks while you sleep. LastFleet = one check shortly before the last expedition lands. Random = a random returning expedition, 60 seconds before it lands."
  },
  "PriorityFleetsaveCelestials": {
   "t": "Celestials (moon and planet) whose fleets are saved first at bedtime."
  },
  "ExpeditionDrain.Active": {
   "t": "Shortly before bedtime no new expedition starts if it could not be back in time. At bedtime the bot waits for the last ones, sends one wave of expeditions and then sleeps."
  },
  "ExpeditionDrain.BufferMinutes": {
   "t": "An expedition may still start if it is back at most this many minutes after bedtime.",
   "e": "10"
  },
  "ExpeditionDrain.StragglerWaitMinutes": {
   "t": "At bedtime the bot waits at most this long for expeditions that are still out. Later ones come home during the night.",
   "e": "20"
  },
  "ExpeditionDrain.DefaultDurationMinutes": {
   "t": "Expedition duration the bot assumes until it has measured at least 3 real ones.",
   "e": "90"
  }
 },
 "DebrisSave": {
  "Celestials": {
   "t": "Celestials used by the Telegram command /debrissave. Each sends its fleet to the nearest debris field and recalls it so it is back after the chosen time."
  }
 },
 "Defender": {
  "Active": {
   "t": "Watches for incoming attacks and reacts."
  },
  "IgnoreProbes": {
   "t": "Do not react to espionage probes."
  },
  "DefendFromMissiles": {
   "t": "React to missile attacks as well."
  },
  "IgnoreWeakAttack": {
   "t": "Ignore attackers that are much weaker than you."
  },
  "WeakAttackRatio": {
   "t": "An attacker is weak if his fleet points are less than yours divided by this number.",
   "e": "10 = ignore attackers with less than a tenth of your fleet points."
  },
  "IgnoreAttackIfIHave.Active": {
   "t": "Ignore an attack on a celestial where there is nothing worth saving."
  },
  "IgnoreAttackIfIHave.MinResourcesToSave": {
   "t": "The attack is ignored if the celestial holds fewer resources than this ..."
  },
  "IgnoreAttackIfIHave.MinFleetToSave": {
   "t": "... and fewer fleet points than this."
  },
  "RandomActivity": {
   "t": "On = every attack check opens the defence page of a random celestial. Off = always the Home celestial. Only makes the bot activity look normal."
  },
  "Home.*": {
   "t": "Your home celestial. Used for the activity call when random activity is off, and as default start point of AutoHarvest."
  },
  "Autofleet.Active": {
   "t": "Automatically fleetsave the attacked celestial."
  },
  "Autofleet.SafetyMarginSeconds": {
   "t": "The fleet must be gone this many seconds before the attack lands.",
   "e": "120"
  },
  "Autofleet.TelegramMessenger.Active": {
   "t": "Send a Telegram message whenever a fleetsave happens."
  },
  "Discord.Active": {
   "t": "Send attack alerts to a Discord webhook."
  },
  "Discord.WebhookUrl": {
   "t": "Discord webhook address. Treat it like a password."
  },
  "WhiteList": {
   "t": "Player IDs that are never treated as attackers, for example friends or alliance mates."
  },
  "SpyAttacker.Active": {
   "t": "Spy the attacker with probes when an attack comes in."
  },
  "SpyAttacker.Probes": {
   "t": "Number of probes sent to spy the attacker."
  },
  "MessageAttacker.Active": {
   "t": "Send the attacker a message."
  },
  "MessageAttacker.Messages": {
   "t": "Possible messages, one per line. The bot sends one of them."
  },
  "TelegramMessenger.Active": {
   "t": "Send attack alerts by Telegram."
  },
  "Alarm.Active": {
   "t": "Mark the alert with a high alarm level so it stands out."
  }
 },
 "Brain": {
  "AutoMine.ColonyOverrides.*": {
   "t": "Own level limit for this single planet. Only fields you set replace the Colony values."
  },
  "Active": {
   "t": "Master switch of the building, research and transport automation. Each part below has its own switch too."
  },
  "Transports.Active": {
   "t": "Allow Brain functions to move resources between your planets."
  },
  "Transports.Origin.*": {
   "t": "Default celestial where resources are collected from."
  },
  "Transports.CargoType": {
   "t": "Preferred cargo ship for transports. Pathfinders are never used for transports."
  },
  "Transports.CargoFallback": {
   "t": "Cargo ship used if the preferred type is not available."
  },
  "Transports.KeepOnOrigin.*": {
   "t": "Ships that stay at the origin and are not used for transports."
  },
  "Transports.MixFleet": {
   "t": "As a last step mix small and large cargos. The fleet flies at the speed of the slowest ship."
  },
  "Transports.UseTransportChain": {
   "t": "Plan the transports as a chain, one after the other."
  },
  "Transports.CompactSlotPlan": {
   "t": "If slots are short, plan again with fewer, bigger shipments."
  },
  "Transports.DeutToLeaveOnMoons": {
   "t": "Deuterium that stays on moons. A transport only leaves a moon if cargo, this reserve and the flight fuel all fit, so the reserve is never used up as fuel."
  },
 "Transports.DeutToLeaveOnPlanets": {
  "t": "Deuterium that stays on a planet when it sends resources away. The transport only leaves if cargo, this reserve and the flight fuel all fit. 0 = keep nothing back."
 },
  "Transports.RoundResources": {
   "t": "Round the sent amount so no fleet is sent for a tiny difference."
  },
  "Transports.SendToTheMoonIfPossible": {
   "t": "Deliver to the moon instead of the planet if there is a moon."
  },
  "Transports.MaxSlots": {
   "t": "Fleet slots Brain transports may use at the same time."
  },
  "Transports.CheckMoonOrPlanetFirst": {
   "t": "Which one is checked first when looking for resources, moon or planet."
  },
  "Transports.DoMultipleTransportIsNotEnoughShipButSamePosition": {
   "t": "If one ship type is not enough, send several transports from the same position."
  },
  "Transports.MultipleOrigins.Active": {
   "t": "Collect resources from several celestials instead of only one."
  },
  "Transports.MultipleOrigins.OnlyFromMoons": {
   "t": "Collect only from moons."
  },
  "Transports.MultipleOrigins.MinimumResourcesToSend": {
   "t": "A celestial only takes part if it can send at least this much."
  },
  "Transports.MultipleOrigins.PriorityToProximityOverQuantity": {
   "t": "On = nearer celestials are preferred. Off = the celestial with most resources is preferred."
  },
  "Transports.MultipleOrigins.Exclude": {
   "t": "Celestials never used as a source."
  },
  "AutoMine.Active": {
   "t": "Builds mines, storages, power and facilities on all your planets."
  },
  "AutoMine.HomeWorld.*": {
   "t": "Your main planet. It uses the level limits of the top block, all other planets use the Colony block."
  },
  "AutoMine.Max*": {
   "t": "Highest level the bot builds on the home world. 0 = never build it."
  },
  "AutoMine.Colony.Max*": {
   "t": "Highest level the bot builds on all other planets. 0 = never build it."
  },
  "AutoMine.ColonyOverrides": {
   "t": "Single planets with their own limits. Matched by exact coordinates; only the fields you set replace the Colony values."
  },
  "AutoMine.RandomOrder": {
   "t": "Handle the planets in random order."
  },
  "AutoMine.OptimizeForStart": {
   "t": "Use the early game build order (robotics and shipyard first)."
  },
  "AutoMine.PrioritizeRobotsAndNanites": {
   "t": "Build robotics and nanite factories early."
  },
  "AutoMine.BuildDepositIfFull": {
   "t": "Build a storage when it is full."
  },
  "AutoMine.BuildSolarSatellites": {
   "t": "Build solar satellites when energy is short."
  },
  "AutoMine.BuildCrawlers": {
   "t": "Build crawlers."
  },
  "AutoMine.DepositHours": {
   "t": "A storage should hold this many hours of production.",
   "e": "24"
  },
  "AutoMine.MaxDaysOfInvestmentReturn": {
   "t": "Skip builds that need longer than this many days to pay back. 9999 = no limit."
  },
  "AutoMine.DeutToLeaveOnMoons": {
   "t": "Deuterium that stays on moons."
  },
  "AutoMine.Exclude": {
   "t": "Planets and moons AutoMine never touches."
  },
  "AutoMine.FixedCrawlerTarget": {
   "t": "Fixed number of crawlers to build per planet. 0 = the bot calculates it."
  },
  "LifeformAutoMine.Active": {
   "t": "Builds lifeform buildings according to the table in lifeform_targets.json."
  },
  "LifeformAutoMine.StartFromCrystalMineLvl": {
   "t": "A planet starts with lifeform buildings once its crystal mine has this level.",
   "e": "20"
  },
  "LifeformAutoMine.TargetSlot": {
   "t": "Row (1-18) of the target table that is built towards. 0 = only the fallback limits."
  },
  "LifeformAutoMine.MaxBasePopulationBuilding": {
   "t": "Extra upper limit for the first population building. The lower value of table and limit counts. For small accounts."
  },
  "LifeformAutoMine.MaxBaseFoodBuilding": {
   "t": "Extra upper limit for the first food building."
  },
  "LifeformAutoMine.MaxBaseTechBuilding": {
   "t": "Extra upper limit for the first technology building."
  },
  "LifeformAutoMine.PreventIfMoreExpensiveThanNextMine": {
   "t": "Do not build a lifeform building that costs more than the next mine upgrade."
  },
  "LifeformAutoMine.Exclude": {
   "t": "Planets that get no lifeform buildings."
  },
  "LifeformAutoResearch.Active": {
   "t": "Researches lifeform technologies."
  },
  "LifeformAutoResearch.PriorityTechs": {
   "t": "Lifeform techs that are researched first, in this order."
  },
  "LifeformAutoResearch.MaxResearchLevel": {
   "t": "Highest level researched for every lifeform tech."
  },
  "LifeformAutoResearch.MaxTechs*": {
   "t": "Own level limit for this tech slot. First digit = race, second = slot."
  },
  "LifeformAutoResearch.Exclude": {
   "t": "Planets that are never used for lifeform research."
  },
  "AutoResearch.Active": {
   "t": "Researches technologies automatically."
  },
  "AutoResearch.Max*": {
   "t": "Highest level to research. 0 = never research it."
  },
  "AutoResearch.Target.*": {
   "t": "Planet where research is started, usually the one with the best research lab."
  },
  "AutoResearch.OptimizeForStart": {
   "t": "Use the early game research order."
  },
  "AutoResearch.EnsureExpoSlots": {
   "t": "Research Computer Technology and Astrophysics as needed to get enough expedition slots."
  },
  "AutoResearch.ForceResearchWhateverTheLabLevel": {
   "t": "Research even if the research lab is still low."
  },
  "AutoResearch.Prioritize*": {
   "t": "Research this technology before the others."
  },
  "AutoResearch.Exclude": {
   "t": "Planets that are never used for research."
  },
  "AutoCargo.Active": {
   "t": "Builds cargo ships automatically."
  },
  "AutoCargo.SupplyMoonFromPlanet": {
   "t": "Moons get their ships from the planet at the same position."
  },
  "AutoCargo.CargoType": {
   "t": "Ship type that is built."
  },
  "AutoCargo.MaxCargosToBuild": {
   "t": "Total number of cargo ships to reach."
  },
  "AutoCargo.MaxCargosToKeep": {
   "t": "How many cargo ships are kept."
  },
  "AutoCargo.LimitToCapacity": {
   "t": "Build only as many as the stored resources need."
  },
  "AutoCargo.SkipIfIncomingTransport": {
   "t": "Skip a celestial that already has a transport on the way."
  },
  "AutoCargo.Exclude": {
   "t": "Celestials that build no cargo ships."
  },
  "AutoDefence.Active": {
   "t": "Builds defence automatically."
  },
  "AutoDefence.DefenceToReach.*": {
   "t": "Number of this defence the bot builds per celestial. 0 = none. Shield domes are on or off."
  },
  "AutoDefence.Exclude": {
   "t": "Celestials that get no defence."
  },
  "AutoRepatriate.Active": {
   "t": "Sends resources from your planets to one target celestial."
  },
  "AutoRepatriate.Target": {
   "t": "Target celestial(s) for the resources."
  },
  "AutoRepatriate.TargetAssociateMoon": {
   "t": "Deliver to the moon at the target position."
  },
  "AutoRepatriate.MinimumResources": {
   "t": "Only send if at least this much can be sent."
  },
  "AutoRepatriate.LeaveDeut.OnlyOnMoons": {
   "t": "Keep the deuterium reserve only on moons."
  },
  "AutoRepatriate.LeaveDeut.DeutToLeave": {
   "t": "Deuterium that stays behind."
  },
  "AutoRepatriate.CargoType": {
   "t": "Ship type used for the transports."
  },
  "AutoRepatriate.SkipIfIncomingTransport": {
   "t": "Skip a celestial that already has a transport on the way."
  },
  "AutoRepatriate.Exclude": {
   "t": "Celestials that send nothing."
  },
  "AutoRepatriate.Reverse.Active": {
   "t": "Reverse repatriation: fetch resources back out of the target when they are needed for a build. Advanced."
  },
  "AutoRepatriate.Reverse.ReturnFuelFactor": {
   "t": "Factor for the fuel reserved for the return flight."
  },
  "AutoRepatriate.Reverse.ReturnAllShips": {
   "t": "Bring all ships back, not only the ones needed."
  },
  "AutoRepatriate.Reverse.KeepShips": {
   "t": "Ships that stay at the target."
  },
  "AutoRepatriate.Reverse.PendingTtlMinutes": {
   "t": "How long a planned reverse transport stays valid."
  },
  "AutoRepatriate.Reverse.OutboundCapacityFactor": {
   "t": "Share of the cargo capacity used for the outbound flight."
  },
  "BuyOfferOfTheDay.Active": {
   "t": "Buys the daily offer of the merchant."
  },
  "AutoFleetJumpGate.Active": {
   "t": "Jumps the fleet through the jump gate automatically."
  },
  "AutoFleetJumpGate.Target": {
   "t": "Moon the fleet jumps to."
  },
  "AutoFleetJumpGate.MinimumAmountOfShipsToBeLeftOnMoon.*": {
   "t": "Ships of this type that stay behind on the moon."
  },
  "AutoFleetJumpGate.MinimumAmountOfShipPointsToTeleport": {
   "t": "Only jump if at least this many fleet points would go."
  }
 },
 "Expeditions": {
  "Active": {
   "t": "Master switch of the expedition worker."
  },
  "Origin": {
   "t": "Celestials the expeditions start from."
  },
  "PrimaryShip": {
   "t": "Automatic mode: main cargo ship per expedition. The bot sends as many as are needed for the largest possible find, or all that are available, plus one probe and the strongest combat ship."
  },
  "PrimaryToKeep": {
   "t": "Automatic mode: this many primary ships always stay at home.",
   "e": "10"
  },
  "MinPrimaryToSend": {
   "t": "Automatic mode: fewest primary ships per expedition. If fewer are left, nothing is sent from this origin. 0 = no minimum."
  },
  "ManualShips.Active": {
   "t": "Use a fixed fleet per expedition instead of the automatic one."
  },
  "ManualShips.Ships.*": {
   "t": "Number of this ship type in every expedition."
  },
  "FuelToCarry": {
   "t": "Deuterium taken along on each expedition."
  },
  "MaxExpeditionsPerOrigin": {
   "t": "Most expeditions that fly from one origin at the same time."
  },
  "SplitExpeditionsBetweenSystems.Active": {
   "t": "Spread the expedition targets over several systems."
  },
  "SplitExpeditionsBetweenSystems.Range": {
   "t": "Systems around the origin that may be used as targets.",
   "e": "2 = up to 2 systems in each direction."
  },
  "RandomizeOrder": {
   "t": "Handle the origins in random order."
  },
  "IgnoreSleep": {
   "t": "Keep sending expeditions during SleepMode."
  },
  "SleepWave.Active": {
   "t": "One extra expedition wave right before the bot goes to sleep."
  },
  "SleepWave.ReturnAt": {
   "t": "Time when the wave should be back.",
   "e": "WakeUp+15 = 15 minutes after waking up."
  },
  "SleepWave.SpeedPercent": {
   "t": "Flight speed of the wave when no return time is set (10 to 100 in steps of 10)."
  },
  "WaitForAllExpeditions": {
   "t": "Start a new round only after all expeditions are back."
  },
  "WaitForMajorityOfExpeditions": {
   "t": "Start a new round when more than half of the expeditions are back."
  },
  "MinWaitNextFleet": {
   "t": "Shortest pause in seconds between two fleets of one round."
  },
  "MaxWaitNextFleet": {
   "t": "Longest pause in seconds between two fleets of one round."
  },
  "MinWaitNextRound": {
   "t": "Shortest extra pause in seconds after the first return before the next round."
  },
  "MaxWaitNextRound": {
   "t": "Longest extra pause in seconds after the first return before the next round."
  }
 },
 "AutoHarvest": {
  "Active": {
   "t": "Collects debris fields with recyclers."
  },
  "HarvestOwnDF": {
   "t": "Collect debris fields of your own planets and moons."
  },
  "HarvestForeignDF": {
   "t": "Collect debris fields of other players."
  },
  "HarvestDeepSpace": {
   "t": "Collect deep space debris fields."
  },
  "MinimumResourcesOwnDF": {
   "t": "A field of your own is only worth a trip with at least this much content."
  },
  "MinimumResourcesForeignDF": {
   "t": "A foreign field is only worth a trip with at least this much content."
  },
  "MinimumResourcesDeepSpace": {
   "t": "A deep space field is only worth a trip with at least this much content."
  },
  "DeepSpaceRange": {
   "t": "Systems around the start celestial that are searched for deep space fields."
  },
  "MaxSlots": {
   "t": "Fleet slots harvesting may use at the same time."
  }
 },
 "AutoColonize": {
  "Active": {
   "t": "Colonises new planets automatically."
  },
  "Origin.*": {
   "t": "Celestial where the colony ships start."
  },
  "SlotsToLeaveFree": {
   "t": "Slots kept free for the rounds of this function."
  },
  "Abandon.Active": {
   "t": "Give up new planets that are too small or have the wrong temperature. Your home planet is never abandoned."
  },
  "Abandon.MinFields": {
   "t": "Smallest acceptable planet size in fields."
  },
  "Abandon.MinTemperatureAcceptable": {
   "t": "Lowest acceptable temperature."
  },
  "Abandon.MaxTemperatureAcceptable": {
   "t": "Highest acceptable temperature."
  },
  "IntensiveResearch.Active": {
   "t": "Colonise in bursts."
  },
  "IntensiveResearch.MaxSlots": {
   "t": "Slots used during a burst."
  },
  "IntensiveResearch.MinWaitNextFleet": {
   "t": "Shortest pause in seconds between two colony fleets in a burst."
  },
  "IntensiveResearch.MaxWaitNextFleet": {
   "t": "Longest pause in seconds between two colony fleets in a burst."
  },
  "Targets": {
   "t": "Ranges to colonise. Each has galaxy, start and end system and position, and the most planets to found in that range."
  },
  "RandomPosition": {
   "t": "Pick positions at random inside the target ranges."
  }
 },
 "AutoDiscovery": {
  "Active": {
   "t": "Sends discovery missions."
  },
  "Origin": {
   "t": "Celestials the discovery missions start from."
  },
  "MaxSlots": {
   "t": "Fleet slots discovery may use at the same time."
  }
 },
 "FastFarm": {
  "Active": {
   "t": "Switches the resource farming mode on. It scans for inactive players, spies them and raids the best ones. The Defender and the sleep mode keep working."
  },
  "OnlyWorker": {
   "t": "Keeps every other worker (Brain, AutoFarm, expeditions, harvest, ...) off while FastFarm is active, so all fleet slots are free for farming. Off = the other workers run as configured and compete for slots."
  },
  "KeepExpeditions": {
   "t": "Lets the expeditions keep running although the other workers are off. They occupy their own fleet slots."
  },
  "Origin": {
   "t": "Celestials the probes and cargo ships start from. For every target the nearest origin that has enough ships is used. Empty = all your planets."
  },
  "ScanRange": {
   "t": "Galaxy and system ranges that are scanned for inactive players, nearest to your origins first. Empty = 100 systems around every origin."
  },
  "CargoType": {
   "t": "Ships used to carry the loot. Both = small cargos first, large cargos only for what the small ones cannot carry.",
   "e": "SmallCargo is the cheapest in fuel for most targets."
  },
  "ProbesPerTarget": {
   "t": "Espionage probes sent to every target. More probes give a full report (fleet and defence) even against high espionage levels, but some may be lost."
  },
  "SlotsToLeaveFree": {
   "t": "Fleet slots that are never used by FastFarm, so the Defender can always send a fleet.",
   "e": "1 = of 37 slots, 36 are used."
  },
  "FleetSpeedPercent": {
   "t": "Speed of the cargo fleets. 100 is the fastest; lower values save deuterium but keep the slot busy longer."
  },
  "CargoSurplusPercent": {
   "t": "Extra cargo space on top of the expected loot, in percent. Covers resources produced while the fleet is flying."
  },
  "MinFillPercent": {
   "t": "A target is skipped when the ships you have left can carry less than this share of its loot, so a few cargos are not sent to a large target."
  },
  "WeightMetal": {
   "t": "How much one metal counts in the loot value that ranks the targets."
  },
  "WeightCrystal": {
   "t": "How much one crystal counts in the loot value. 1.5 matches the usual trade rate."
  },
  "WeightDeuterium": {
   "t": "How much one deuterium counts in the loot value. A higher value puts deuterium-rich targets first.",
   "e": "1 : 1.5 : 3 = metal : crystal : deuterium."
  },
  "MinLoot": {
   "t": "Targets with less lootable resources than this (metal + crystal + deuterium, after the plunder ratio) are not attacked."
  },
  "MinDeuterium": {
   "t": "Targets with less lootable deuterium than this are not attacked. 0 = no limit."
  },
  "TopTargets": {
   "t": "Highest number of attacks sent in one round, best targets first."
  },
  "DeleteReports": {
   "t": "Deletes each espionage report after it was read. The inbox has a limit; when it is full, new reports are lost."
  },
  "ReportMaxAgeMinutes": {
   "t": "Reports older than this are not used for an attack, because the target may have changed. It is spied again instead."
  },
  "RespyAfterAttackMinutes": {
   "t": "A target that was just raided is left alone for this long, because its storage has to refill first."
  },
  "DefendedRecheckHours": {
   "t": "Targets with ships or defence, or that turned active, are spied again only after this time."
  },
  "MaxSystemsPerScanStep": {
   "t": "How many systems the background scan visits in one step while the bot waits for fleets."
  }
 },
};
