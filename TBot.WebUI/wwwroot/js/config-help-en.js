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
  "ShowUpdateNotes": {
   "t": "After a version change the bot shows one short note about what is new, in the log and on Telegram/Discord. Off = no note. Default on."
  },
  "AnonymizeLogs": {
   "t": "On: player name, universe and e-mail address are replaced in all log lines by [Player], [Universe] and [Email] - useful before posting logs (read at start, so a restart is needed). It is also the default of the Hide data switch on every web UI page: names and coordinates are hidden there. The switch on a page overrides this in that browser only. Off (default): the real names are shown."
  },
  "CaptchaAPIKey": {
   "t": "Key of a captcha solving service. Optional, only needed if the game asks for captchas."
  },
  "LoginPause": {
   "t": "Protects the IP: after repeated failed logins (captcha etc.) the bot makes a pause instead of trying again and again. The pause doubles with every further failure. During the pause the manual captcha page (/bot/captcha) keeps working; if you solve the captcha there, the bot continues at once. The state is kept in a file, so a restart does not skip the pause."
  },
  "LoginPause.Active": {
   "t": "Switch the login pause on or off."
  },
  "LoginPause.FailuresBeforePause": {
   "t": "Failed logins in a row before the first pause."
  },
  "LoginPause.PauseMinutes": {
   "t": "Length of the first pause in minutes. Doubles with every further failure."
  },
  "LoginPause.MaxPauseMinutes": {
   "t": "Longest pause in minutes."
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
   "t": "Send log lines of the bot to Telegram. LogLevel and LogLevels decide which. Off: only alerts and command answers."
  },
  "LogLevel": {
   "t": "Lowest level of log lines that go to Telegram (Error, Warning, Information, Debug). Default Warning. Applies to every worker without its own entry in LogLevels. /setloglevel changes it only until the next restart.",
   "e": "Information"
  },
  "LogLevels": {
   "t": "Own level per worker (name as in the log, for example Expeditions or AutoMine). Default = use the default level, Off = nothing from this worker, otherwise Error, Warning, Information or Debug.",
   "e": "{ \"Expeditions\": \"Information\", \"AutoFarm\": \"Off\" }"
  },
  "RepeatMinutes": {
   "t": "The same log line of a worker goes to Telegram at most once in this many minutes (numbers such as times are ignored when comparing). 0 = off, every line is sent. Applies to every worker without its own entry in LogRepeatMinutes. The log file is not affected.",
   "e": "60"
  },
  "LogRepeatMinutes": {
   "t": "Own repeat time per worker in minutes (name as in the log). 0 = never hold back lines of this worker, empty = use RepeatMinutes.",
   "e": "{ \"Expeditions\": 120, \"AutoFarm\": 0 }"
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
  },
  "Alarm.SoundFile": {
   "t": "Full path to a WAV file that is played on an attack (the PC running the bot). Empty, missing or unplayable: the old beep."
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
  "Transports.MaxBundledBuildings": {
   "t": "How many ADDITIONAL buildings one delivery may bring along besides the target of the worker. 0 (default) = only the one target, which matches the resource reservation."
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
  "AutoMine.SolarSatelliteBatch": {
   "t": "Solar satellites are built in bundles of this size (the missing amount is rounded up) instead of one by one. Default 20; 0 or 1 = exactly the missing amount."
  },
  "AutoMine.EnergyReservePercent": {
   "t": "Before a mine upgrade that would push the energy balance into deficit, the energy source (solar plant, fusion or satellites) is built first, sized for the missing energy plus this percentage as a buffer. Default 10; 0 = exactly the missing amount."
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
  "AutoCargo.MaxProductionHours": {
   "t": "Upper limit for the production time of one cargo order per celestial, in hours. 0 = no limit. What does not fit is built in a later run."
  },
  "AutoCargo.SupplyMoonFromPlanet": {
   "t": "Moons get their ships from the planet at the same position."
  },
  "AutoCargo.CargoType": {
   "t": "Ship type that is built."
  },
  "AutoCargo.MaxCargosToBuild": {
   "t": "The most cargo ships ordered in one run. The rest follows in the next run.",
   "e": "200 = at most 200 ships per run, even if the target amount is higher."
  },
  "AutoCargo.MaxCargosToKeep": {
   "t": "The target: the celestial is filled up with cargo ships until it has this many. 0 means nothing is built.",
   "e": "1000 = build until 1000 ships are on the celestial."
  },
  "AutoCargo.LimitToCapacity": {
   "t": "Build only as many as the stored resources need."
  },
  "AutoCargo.SkipIfIncomingTransport": {
   "t": "Skip a celestial that already has a transport on the way."
  },
  "AutoCargo.Ships": {
   "t": "More ship types that are built after the cargo ships: Recycler, Pathfinder (Discoverer class), Reaper (General class), Crawler (capped by the game's maximum) and others. One order per celestial and run, the first entry whose target is not reached yet. Cargo ships come first; moon supply and /switch still use CargoType only.",
   "e": "Type Recycler, Target 50, Build per run 20 = fill up to 50 recyclers, 20 per run."
  },
  "AutoCargo.Exclude": {
   "t": "Celestials that build no cargo ships."
  },
  "AutoDefence.Active": {
   "t": "Builds defence automatically."
  },
  "AutoDefence.MaxProductionHours": {
   "t": "Upper limit for the production time of one defence order per celestial, in hours. 0 = no limit. What does not fit is built in a later run."
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
  "AutoRepatriate.ReserveTransportSlots": {
   "t": "The reverse fetch stays this many slots below Brain Transports Max slots, so it never takes slots from the mines, lifeform and research workers. 0 (default) = no reserve."
  },  "AutoRepatriate.ExpoSupply.Active": {
   "t": "Supplies the expedition moons: if a moon lacks ships or deuterium for its planned expeditions plus the backup (Expeditions BackupExpeditions), the shortfall is noted and sent from the base (first Target) in the next repatriate cycle by deploy. Ships on expeditions and deliveries already on their way count as present. Off by default.",
   "e": "Deuterium target per moon: MinDeuteriumOnMoon, or expeditions x FuelToCarry if that is 0."
  },

  "ResourceLedger.Active": {
   "t": "The bookkeeping that reserves resources for builds so workers do not fight over the same stock or order the same delivery twice. On by default."
  },
  "ResourceLedger.PriorityByCost": {
   "t": "On (default): per celestial the cheapest build wish gets the stock first, and a build that is not yet fully funded releases its held stock to cheaper ones, so small builds do not starve behind a big one. Off: first come, first served."
  },
  "AutoRepatriate.Reverse.Active": {
   "t": "Reverse repatriation: fetch resources back out of the target when they are needed for a build. Advanced."
  },
  "AutoRepatriate.Reverse.ReturnFuelFactor": {
   "t": "Deuterium taken along for the return flight = fuel of the return flight x this factor (1.1 = 10 % more).",
   "e": "The return flight takes exactly the ships that flew out."
  },
  "AutoRepatriate.Reverse.PendingTtlMinutes": {
   "t": "How long a planned reverse transport stays valid."
  },
  "BuyOfferOfTheDay.Active": {
   "t": "Buys the daily offer of the merchant."
  },
  "KeepBoosters.Active": {
   "t": "Activates a booster from the inventory when none of that type is running."
  },
  "KeepBoosters.DryRun": {
   "t": "Only writes to the log what would be activated."
  },
  "KeepBoosters.MinMineLevel.*": {
   "t": "A booster of this type is only used when the matching mine on the planet has at least this level."
  },
  "KeepBoosters.EventBoosters.Active": {
   "t": "Event resource boosters in the inventory expire. When one has little time left it is activated on the planet with the highest production. Normal boosters are not affected. Needs a readable expiry time (see the log line 'Event booster in inventory')."
  },
  "KeepBoosters.EventBoosters.ActivateWithinHours": {
   "t": "Activate an event booster when its remaining time in the inventory is below this many hours."
  },
  "KeepBoosters.Reserve": {
   "t": "Boosters of each type that stay in the inventory."
  },
  "KeepBoosters.LogItems": {
   "t": "Logs active items per planet and the inventory, at most every 12 hours."
  },
  "KeepBoosters.Tiers": {
   "t": "Allowed tiers, the first one is used first."
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
  "AutoFleetJumpGate.Exclude": {
   "t": "Moons that never jump."
  },  "AutoFleetJumpGate.ExpoOverride": {
   "t": "On: on the expedition start moons only the ships for the planned expeditions plus the backup (Expeditions BackupExpeditions) stay. Everything else, expedition finds included, jumps out. This replaces the general minimum on those moons. Off by default."
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
  "SmartShips": {
   "t": "New fleet selection in auto mode (only if ManualShips is off): per expedition 1 probe, 1 pathfinder and 1 combat ship (Reaper first), each if available. If the main ships do not cover all expeditions still planned from this origin, the amount is reduced equally for all and the missing cargo is filled up by the mix ship."
  },
  "SmartMixShip": {
   "t": "Mix ship that fills up missing cargo with SmartShips (SmallCargo or LargeCargo; Null = do not mix)."
  },
  "PrimaryShip": {
   "t": "Automatic mode: main cargo ship per expedition. The bot sends as many as are needed for the largest possible find, or all that are available, plus one probe, the strongest combat ship and one pathfinder if available (otherwise just the combat ship)."
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
  "MinDeuteriumOnMoon": {
   "t": "A moon needs at least this much deuterium to start expeditions (0 = no check). Below it the moon is skipped and you get a message."
  },  "BackupExpeditions": {
   "t": "How many complete expedition fleets are kept as a backup on every expedition start moon, on top of the planned expeditions (used by the JumpGate expo override and the expedition moon supply). Covers the total loss of a fleet. Default 1.",
   "e": "1 = one extra full fleet stays on the moon."
  },

  "TelegramMessenger.Active": {
   "t": "Send a Telegram message when new expedition reports arrive, with the summed finds (resources, dark matter, ships). Several reports found at the same time come as one message. The first read after the first start only learns the existing reports and sends nothing."
  },
  "TelegramMessenger.OnlyFinds": {
   "t": "On: no message when all new expeditions came back empty. Off: you also get a message when nothing was found."
  },
  "TelegramMessenger.WindowMinutes": {
   "t": "0 = a message right after the bot reads new reports (every 20 minutes at most). With a number, new reports are collected and one summed message is sent once that many minutes have passed since the first collected report. Reports collected but not yet sent are lost when the bot restarts.",
   "e": "60 = at most one expedition message per hour."
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
   "t": "Systems to the left and right of each of your planets that are searched for deep space (expedition) fields."
  },
  "ScanOnlyExpeditionOrigins": {
   "t": "On: the deep space and foreign field search runs only around the expedition start celestials (Expeditions.Origin) instead of all your planets. Without expedition origins all planets are used. Own fields at your planets are always checked."
  },
  "ForeignDFRange": {
   "t": "Systems to the left and right of each of your planets that are searched for foreign debris fields (0 = only the planet's own system). The larger, the more galaxy calls per cycle."
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
  },
  "StopAtArtifacts": {
   "t": "Stops sending discoveries once this many artifacts are collected (the \"Collected artifacts: 1840 / 3600\" counter on the lifeform pages). 0 = no limit. Discoveries resume when the count drops below again. If the counter cannot be read, discoveries keep running (warning in the log)."
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
  "BuildCheckMinutes": {
   "t": "While FastFarm is active, AutoMine, AutoResearch and the lifeform workers are not switched off completely: they run whenever transports with resources are on their way to your celestials (so the resources do not lie around), and otherwise once every this many minutes to see what can be built. They only spend resources that are already on the celestial and never order new transports during FastFarm. 0 = they stay off as before (default 60).",
   "e": "60 = a short construction check every hour."
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
   "t": "How much one crystal counts in the loot value."
  },
  "WeightDeuterium": {
   "t": "How much one deuterium counts in the loot value. A higher value puts deuterium-rich targets first.",
   "e": "Only the ratio of the three weights matters, default 3 : 2 : 1 (MSE ratio); raise the deuterium weight to put deuterium-rich targets first."
  },
  "ShrinkDiscardPercent": {
   "t": "%s",
   "e": "10 = drop everything that lost 10 %% or more."
  },
  "ResetOnActivate": {
   "t": "On bot start and when FastFarm is switched on again after a pause, the old state (targets, blocks, sweep position) is discarded so nothing from the day before lingers. Statistics are kept. A backup of the old file is saved as .bak.",
   "e": "Only FastFarm uses this data; the normal AutoFarm is not affected."
  },
  "ResetMinHours": {
   "t": "Lock for the reset: at most one reset every this many hours, so restarts or switching FastFarm on and off do not wipe everything each time.",
   "e": "6 = a reset at most every 6 hours; 0 = no lock."
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
  "NoReportHours": {
   "t": "A target that gave no espionage report three times in a row (the probes were probably destroyed) is not probed again for this many hours. 0 = off. The FastFarm reset clears this list."
  },
  "DeleteReports": {
   "t": "Deletes each espionage report after it was read. The inbox has a limit; when it is full, new reports are lost."
  },
  "ReportMaxAgeMinutes": {
   "t": "Sweep mode: a fresh check report older than this is not used for an attack (e.g. no free cargo for a while); the target is checked again."
  },
  "MaxSystemsPerScanStep": {
   "t": "How many systems the full scan of a round visits in one step."
  }
 },
 "Experimental": {
  "BotPause": {
   "t": "The Pause button in the browser tile (or /pause in Telegram) stops all functions except the Defender for a few minutes, so nothing new starts while you play by hand. After the time the bot goes on by itself. Recalls and the Defender keep running. A pause is refused if a recall is due in that time."
  },
  "BotPause.Active": {
   "t": "Switch the pause function on or off. Off: the Pause button and /pause are refused."
  },
  "BotPause.DurationMinutes": {
   "t": "How long a pause lasts if you do not give a time.",
   "e": "15 = the bot continues 15 minutes after you pressed Pause."
  },
  "BotPause.RecallMarginMinutes": {
   "t": "Safety distance to a planned recall. The pause is refused if a recall is due within the pause time plus this margin.",
   "e": "2 = with a 15 minute pause, a recall due in 17 minutes or less blocks the pause."
  },
  "BotPause.MaxDurationMinutes": {
   "t": "Longest pause you can ask for with /pause."
  },
  "BotPause.StopDefender": {
   "t": "Stop the Defender during the pause as well (default off). Then nothing watches for attacks, so a pause is refused if a known attack arrives within the limit below plus the pause length."
  },
  "BotPause.Mode": {
   "t": "Gate (default): during the pause the workers keep running but skip their cycle, and go on with their usual plan shortly after the pause, spread over about a minute. Stop: the old hard pause, every worker is stopped and started again afterwards."
  },
  "BotPause.AttackMarginMinutes": {
   "t": "A pause is refused if an attack arrives within this many minutes (default 15). Spy, attack and expedition missions are also blocked while the bot is paused."
  },
  "DefenderRecheck": {
   "t": "Shortly before a fleetsave the Defender asks the game for the attack list once more. If the attack was recalled, the fleetsave is skipped; if it arrives later, the fleetsave is moved. If the question fails or takes too long, the fleetsave is sent as planned."
  },
  "DefenderRecheck.Active": {
   "t": "Switch the second check on or off (default off)."
  },
  "CollectLogs": {
   "t": "Collects logs for development: while active, every log line (from debug up) and all lines of the extra debug log are written into ONE file in the folder devlogs (TBot-collect-<start>.log, max. 300 MB). The collection stops by itself after the hours set (default 12), also across restarts; switch it off and on again to collect again."
  },
  "CollectLogs.Active": {
   "t": "Collection on or off (default off)."
  },
  "CollectLogs.Hours": {
   "t": "How long to collect, in hours (default 12)."
  },
  "CollectLogs.Anonymize": {
   "t": "Replace name, universe, mail, celestials, coordinates (x:x:position), IP and tokens (default on). Off = raw data, do not share."
  },
  "ExtraDebugLog": {
   "t": "Writes extra diagnosis lines to their own file devlogs/TBot-debug-<date>.log (anonymized: names, mail, IP and tokens are replaced; coordinates become x:x:position, amounts stay) (max. 50 MB per day). The normal log stays small. On by default; old files (over 7 days) are deleted automatically. Switch it off if you do not need it."
  },
  "ExtraDebugLog.Active": {
   "t": "Switch the extra log on or off (default on)."
  },
  "ExtraDebugLog.Repatriate": {
   "t": "Repatriate / reverse repatriate: candidates, payload after ledger, send attempts and results."
  },
  "ExtraDebugLog.BotPause": {
   "t": "Pause start/end, rejections and blocked missions."
  },
  "ExtraDebugLog.DefenderRecheck": {
   "t": "Attack list before the fleetsave: times, decision."
  },
  "ExtraDebugLog.FleetsaveSlot": {
   "t": "Every slot decision of SendFleet with the numbers."
  },
  "ExtraDebugLog.MinePlanRanking": {
   "t": "Ranking mode and the top steps."
  },
  "ExtraDebugLog.AsteroidHunt": {
   "t": "All asteroid hunt messages."
  },
  "ExtraDebugLog.ShadowFleet": {
   "t": "All shadow fleet messages."
  },
  "ExtraDebugLog.Ledger": {
   "t": "Resource ledger: which claims hold how much of a celestial and which of them count for repatriate and collect."
  },
  "RepatriateLedgerHold": {
   "t": "How strongly waiting build claims of the resource ledger block repatriate and collect."
  },
  "RepatriateLedgerHold.Ignore": {
   "t": "On: repatriate, reverse repatriate and /collect ignore the resource ledger completely and move all stock, even what build claims hold. The workers (AutoMine and others) may then find their saved stock gone. Default off."
  },
  "RepatriateLedgerHold.MinPercent": {
   "t": "A waiting claim only protects its held stock from repatriate and collect if it already holds at least this percent of its total need (metal+crystal+deuterium). Claims with flights or fully covered always protect. 0 = every held amount is protected (old behavior).",
   "e": "30 = a metal mine claim that holds 1% of 995 M no longer blocks the celestial."
  },
  "MinePlanRanking": {
   "t": "How the page Minenplan ranks the next investments (mines and research). Display only: AutoMine and AutoResearch keep building by their own rules. You can also change these values on the page itself; the page remembers its own choice in your browser."
  },
  "MinePlanRanking.Mode": {
   "t": "Payback = shortest payback first (like AutoMine). Cheapest = lowest cost first. Npv = highest net gain over the horizon (gain per day times horizon days minus cost); steps that do not pay back within the horizon are left out."
  },
  "MinePlanRanking.HorizonDays": {
   "t": "Horizon for the net gain ranking, in days.",
   "e": "90 = a step counts as good if it earns more than it costs within 90 days."
  },
  "MinePlanRanking.UsePlanForAutoMine": {
   "t": "AutoMine only builds a mine when its planet is among the first steps of the build plan (best payback first across all planets). Other planets wait. Off = every planet builds on its own."
  },
  "MinePlanRanking.PlanLookahead": {
   "t": "How many steps of the build plan count as due. Larger = more planets build at the same time."
  },
  "MinePlanRanking.DailyBudgetDeut": {
   "t": "Daily budget for the build plan in deuterium equivalent (e.g. expedition income). 0 = enter it on the page."
  },
  "MinePlanRanking.MaxPaybackDays": {
   "t": "Longest payback that still counts as sensible (used with Only sensible)."
  },
  "MinePlanRanking.OnlySensible": {
   "t": "Hide steps whose payback is longer than the limit."
  },
  "MinePlanRanking.IncludeResearch": {
   "t": "Rank plasma technology next to the mine steps (it raises the output of all mines on all planets)."
  },
  "FleetsaveSlot": {
   "t": "Keeps the last free fleet slot for the fleetsave. Every other fleet, even a forced one, is refused when only the reserved slot is left, so an attack always finds a slot for the fleetsave."
  },
  "FleetsaveSlot.Active": {
   "t": "Switch the reserved slot on or off (default on)."
  },
  "FleetsaveSlot.Slots": {
   "t": "How many slots stay free for the fleetsave.",
   "e": "1 = the last free slot is only used by the fleetsave."
  },
  "ExtraDebugLog.GalaxyCache": {
   "t": "Cache hits, background scans and detected events of the galaxy cache."
  },
  "ExtraDebugLog.Requests": {
   "t": "Request counters every 10 minutes: requests to the game per worker and from the bot and the browser."
  },
  "ExtraDebugLog.BrowserProxy": {
   "t": "Error 100 retries and celestial handling of the browser proxy (when you play through the bot)."
  },
  "GalaxyCache": {
   "t": "Shared store of all galaxy scans with a time stamp. FastFarm and Colonize ask it before they query the server. A slow background scan keeps the systems around your planets fresh and reports new colonies and moves of other players. Data file: data/galaxycache_<alias>.json."
  },
  "GalaxyCache.Active": {
   "t": "Switch the cache on or off (default on). Off: every worker queries live again."
  },
  "GalaxyCache.MaxAgeHours": {
   "t": "How old an entry may be so that workers use it instead of a live query (default 12).",
   "e": "12"
  },
  "GalaxyCache.ScanAll": {
   "t": "The background scan walks all galaxies and systems slowly, starting with the surroundings of your own systems, so the cache gives a complete picture (default on). Off = only the systems around your planets (see Systems around each planet)."
  },
  "GalaxyCache.HoursPerGalaxy": {
   "t": "Full scan: time budget per galaxy. The pause between two systems is calculated from it (1 hour = about 7 seconds per system, 9 galaxies = about 9 hours). 0 = use the pauses DelayMinSeconds / DelayMaxSeconds. Default 1."
  },
  "GalaxyCache.FullScanIntervalHours": {
   "t": "Full scan: every system is scanned again after this many hours. 24 = one full scan per day, starting at your home world and moving outwards. Default 24."
  },
  "GalaxyCache.UseHighscore": {
   "t": "Reads the highscore once a day into a player database: status (inactive i, long inactive I, banned, vacation), rank, alliance and home world. Systems with inactive players are scanned first and kept fresh. The rank and status also correct old cache entries. Default on."
  },
  "GalaxyCache.UseApi": {
   "t": "Reads the player list (status, alliance, rank) from the public OGame API: players.xml and highscore.xml, one request each, no load in the game. The host is taken from AutoFarm.AllianceWhitelist.ServerHost or derived from the universe number and language. The API status can lag behind (a returned player may stay marked inactive for a while), so the galaxy scans decide the status; the API only supplies the player list and the rank. Off = read the highscore pages instead. Default on."
  },
  "GalaxyCache.HighscoreIntervalHours": {
   "t": "How often the highscore is read (default 24 hours). A failed read is retried after an hour."
  },
  "GalaxyCache.FastFarmMaxAgeHours": {
   "t": "FastFarm takes its galaxy scan from the cache while the entry is no older than this (default 36 hours). Espionage before an attack is still done live."
  },
  "GalaxyCache.AutoFarmMaxAgeHours": {
   "t": "AutoFarm takes its scan from the cache while the entry is no older than this (default 36 hours). The target is still re-checked live before an attack."
  },
  "GalaxyCache.EmptyMaxAgeHours": {
   "t": "Systems without any planet are scanned again only after this many hours (default 72)."
  },
  "GalaxyCache.RangeSystems": {
   "t": "Used only when the whole-universe scan (ScanAll) is OFF: the background scan walks this many systems left and right of every own system (default 15, i.e. 31 systems around each planet). No effect while ScanAll is on.",
   "e": "15 = 31 systems around each planet."
  },
  "GalaxyCache.DelayMinSeconds": {
   "t": "Shortest pause between two background queries (default 4 seconds)."
  },
  "GalaxyCache.DelayMaxSeconds": {
   "t": "Longest pause between two background queries (default 8 seconds)."
  },
  "GalaxyCache.UseInFastFarm": {
   "t": "FastFarm takes its galaxy scan from the cache while the entry is fresh (default on). Before an attack the fresh spy report still decides."
  },
  "GalaxyCache.UseInColonize": {
   "t": "Colonize skips positions that the fresh cache shows as occupied. Positions shown as free are always checked live (default on)."
  },
  "GalaxyCache.UseInAutoFarm": {
   "t": "AutoFarm takes its scan for inactive targets from the cache while the entry is fresh (default on). Before an attack the target is still re-checked live."
  },
  "GalaxyCache.Events": {
   "t": "Write detected changes to the log: new colony, planet gone, new moon, status change, move (default on)."
  },
  "GalaxyCache.Telegram": {
   "t": "Send new colonies and moves of other players by Telegram / Discord (default on). Other events only appear in the log."
  },
  "AsteroidHunt": {
   "t": "Item event: once per game day one ship attacks the asteroid on position 17 of your own system and the inventory is compared afterwards. One log line per attempt, no messenger message."
  },
  "AsteroidHunt.Active": {
   "t": "Switch the hunter on or off (default off)."
  },
  "AsteroidHunt.ShipType": {
   "t": "Ship that is sent, typed as in the game (English name). Any ship works, e.g. Pathfinder, Cruiser, Reaper, Destroyer; a probe cannot find items. The planet with the most of this ship is used. An unknown name falls back to Pathfinder.",
   "e": "Pathfinder"
  },
  "AsteroidHunt.ShipCount": {
   "t": "How many ships are sent (default 1)."
  },
  "AsteroidHunt.MaxAttemptsPerDay": {
   "t": "Attempts per game day (0 = unlimited, at most 60). According to the forums the number of attempts is not limited, but only one item per day can be found - the hunter stops after the first find."
  },
  "AsteroidHunt.SearchSystems": {
   "t": "The asteroid is not in your home system: this many systems are tried in turn (own system, +1, -1, +2, -2 ...). A target the server refuses costs no attempt.",
   "e": "10 = systems 0, +1, -1 ... up to 5 away."
  },
  "AsteroidHunt.SaveJson": {
   "t": "Write every attempt and find to data/asteroiden_funde_<alias>.json."
  },
  "ShadowFleet": {
   "t": "NOT FULLY TESTED - use with care and report problems. What it does: the chosen ships of ONE celestial fly in a loop on a recycle (harvest) mission at slow speed to the nearest debris field (up to 10 systems away). After half of the flight time the fleet is recalled and flies home, then after a short random pause the next flight starts. So the ships are almost never at home: a player who spies the celestial finds no fleet, and the fleet never stands still where an attack could hit it. It uses one fleet slot and the deuterium of that celestial. Safety: no flight starts when an attack would land before the fleet is back; a flying fleet is called home if it can still make it before the impact. Needs a deuterium reserve on that celestial."
  },
  "ShadowFleet.Active": {
   "t": "Switch the shadow fleet on or off (default off). Keep deuterium on the celestial: every flight burns fuel."
  },
  "ShadowFleet.Celestial": {
   "t": "The one celestial that is secured (planet or moon). Only its deuterium and one fleet slot are used."
  },
  "ShadowFleet.Ships": {
   "t": "Tick the ships that fly and enter the amount (0 = all). At least one Recycler (or Pathfinder) is needed for the harvest mission."
  },
  "ShadowFleet.SpeedPercent": {
   "t": "Flight speed, 10 to 100 percent. 10 is the slowest and the cheapest in fuel."
  },
  "ShadowFleet.MinBackInMinutes": {
   "t": "Shortest time for one flight (out and back).",
   "e": "20"
  },
  "ShadowFleet.MaxBackInMinutes": {
   "t": "Longest time for one flight (out and back). The fleet is back home at the latest after this time - the time limit.",
   "e": "60"
  },
  "ShadowFleet.PauseMinSeconds": {
   "t": "Shortest pause at home between two flights."
  },
  "ShadowFleet.PauseMaxSeconds": {
   "t": "Longest pause at home between two flights."
  },
  "ShadowFleet.DeuteriumFlights": {
   "t": "Deuterium for this many flights goes along as cargo (limited by cargo space and stock). Low stock with 2 or more flights left: log warning only. Exactly one flight left: messenger warning. None: the flights wait.",
   "e": "3"
  },
  "ShadowFleet.WindowFrom": {
   "t": "Start of the time window (game time, HH:mm). Empty = always. May cross midnight, e.g. 22:00 to 07:00.",
   "e": "22:00"
  },
  "ShadowFleet.WindowTo": {
   "t": "End of the time window (HH:mm). A flight is only started if it is back before the window ends.",
   "e": "07:00"
  },
  "ShadowFleet.DefenderOff": {
   "t": "Trap mode for the shadow fleet only. On = the Defender does not send the shadow fleet away but still saves resources and all other ships. When an attack is announced, a flying shadow fleet is called home so that it is there at impact. Sleep-time fleetsaves still send everything away."
  },
  "DefenderRecheck.TimeoutSeconds": {
   "t": "How long the Defender waits for the answer. After that the fleetsave is sent as planned.",
   "e": "4 = after 4 seconds without an answer the fleetsave goes out."
  }
 }
};
