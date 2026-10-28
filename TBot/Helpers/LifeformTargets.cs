using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using TBot.Common.Logging;
using Tbot.Includes;
using TBot.Ogame.Infrastructure.Enums;
using Tbot.Services;
using TBot.Model;
using TBot.Ogame.Infrastructure.Models;
using TBot.Ogame.Infrastructure;

namespace Tbot.Helpers {
	public static class LifeformTargets {
		private const string DefaultFileName = "lifeform_targets.json";

		private static readonly string[] FallbackKeys = {
			"MaxBasePopulationBuilding", "MaxBaseFoodBuilding", "MaxBaseTechBuilding", "MaxT2Building", "MaxT3Building",
			"MaxBuilding6", "MaxBuilding7", "MaxBuilding8", "MaxBuilding9", "MaxBuilding10", "MaxBuilding11", "MaxBuilding12"
		};

		private static readonly object _lock = new();
		private static JObject _cache;
		private static string _cachePath;
		private static DateTime _cacheWriteUtc = DateTime.MinValue;
		private static DateTime _failedWriteUtc = DateTime.MinValue;
		private static bool _missingFileReported;

		public static bool TryApplyMaxLevels(object lfSettings, Celestial celestial, LFBuildings target, Action<LogLevel, string> log) {
			try {
				string race = RaceKey(celestial.LFtype);
				if (race == null) {
					return false;
				}

				JObject root = Load(ReadTargetsFile(lfSettings), log);
				if (root == null) {
					return false;
				}

				JObject fallback = root["Fallback"] as JObject;
				if (fallback == null) {
					log(LogLevel.Warning, "LifeformTargets: section 'Fallback' is missing in the JSON, using old config values.");
					return false;
				}

				int[] levels = new int[13];
				for (int pos = 1; pos <= 12; pos++) {
					JToken token = fallback[FallbackKeys[pos - 1]];
					if (token == null) {
						log(LogLevel.Warning, $"LifeformTargets: fallback value '{FallbackKeys[pos - 1]}' is missing in the JSON, using old config values.");
						return false;
					}
					levels[pos] = (int) token;
				}

				int[] baseLevels = (int[]) levels.Clone();
				string source = "Fallback";
				int slot = ReadTargetSlot(lfSettings);
				if (slot > 0) {
					JObject raceObj = (root["Races"] as JObject)?[race] as JObject;
					JObject slotObj = (raceObj?["Slots"] as JObject)?[slot.ToString()] as JObject;
					JObject positions = raceObj?["Positions"] as JObject;
					if (slotObj == null || positions == null) {
						log(LogLevel.Warning, $"LifeformTargets: no entry for race {race} / slot {slot} in the JSON, only the fallback values apply.");
					} else {
						foreach (JProperty prop in slotObj.Properties()) {
							if (prop.Name == "Target" || prop.Name.StartsWith("_")) {
								continue;
							}
							JToken posToken = positions[prop.Name];
							if (posToken == null) {
								log(LogLevel.Warning, $"LifeformTargets: building '{prop.Name}' ({race}) has no position in 'Positions', skipped.");
								continue;
							}
							int position = (int) posToken;
							if (position < 1 || position > 12) {
								log(LogLevel.Warning, $"LifeformTargets: invalid position {position} for '{prop.Name}' ({race}), skipped.");
								continue;
							}
							levels[position] = (int) prop.Value;
						}

						int foodBonus = raceObj["FoodBonusLevels"] != null ? (int) raceObj["FoodBonusLevels"] : 0;
						if (foodBonus != 0) {
							levels[2] += foodBonus;
						}
						source = $"Slot {slot} (+{foodBonus} food)";
					}
				}

				string limits = "";
				for (int pos = 1; pos <= 3; pos++) {
					int? cap = ReadCap(lfSettings, pos);
					if (cap.HasValue && levels[pos] > cap.Value) {
						levels[pos] = cap.Value;
						limits += $" Limit P{pos}<={cap.Value}";
					}
				}
				if (limits != "") {
					source += " |" + limits;
				}

				string boost = ApplyResearchBoost(root, race, celestial, levels, baseLevels, ReadCap(lfSettings, 3));
				if (boost != "") {
					source += " |" + boost;
				}

				for (int pos = 1; pos <= 12; pos++) {
					SetByPosition(target, celestial.LFtype, pos, levels[pos]);
				}

				log(LogLevel.Debug, $"LifeformTargets {race} [{source}]: " + string.Join(", ", Enumerable.Range(1, 12).Select(p => $"P{p}={levels[p]}")));
				return true;
			} catch (Exception e) {
				log(LogLevel.Error, $"LifeformTargets: error while applying the target levels ({e.Message}), using old config values.");
				return false;
			}
		}

		private static JObject Load(string fileName, Action<LogLevel, string> log) {
			lock (_lock) {
				string path = ResolvePath(fileName);
				if (path == null) {
					if (!_missingFileReported) {
						log(LogLevel.Warning, $"LifeformTargets: file '{fileName}' not found (searched in {Directory.GetCurrentDirectory()} and {AppContext.BaseDirectory}), using old config values.");
						_missingFileReported = true;
					}
					return null;
				}
				_missingFileReported = false;

				DateTime writeUtc = File.GetLastWriteTimeUtc(path);
				if (_cache != null && path == _cachePath && writeUtc == _cacheWriteUtc) {
					return _cache;
				}
				if (writeUtc == _failedWriteUtc) {
					return _cache;
				}

				try {
					JObject parsed = JObject.Parse(File.ReadAllText(path));
					_cache = parsed;
					_cachePath = path;
					_cacheWriteUtc = writeUtc;
					_failedWriteUtc = DateTime.MinValue;
					log(LogLevel.Information, $"LifeformTargets: '{path}' loaded.");
				} catch (Exception e) {
					_failedWriteUtc = writeUtc;
					log(LogLevel.Error, $"LifeformTargets: '{path}' not readable ({e.Message}). " + (_cache != null ? "Using the last valid version." : "Using the old config values."));
				}
				return _cache;
			}
		}

		private static string ResolvePath(string fileName) {
			if (Path.IsPathRooted(fileName)) {
				return File.Exists(fileName) ? fileName : null;
			}
			string inCwd = Path.Combine(Directory.GetCurrentDirectory(), fileName);
			if (File.Exists(inCwd)) {
				return inCwd;
			}
			string inBase = Path.Combine(AppContext.BaseDirectory, fileName);
			return File.Exists(inBase) ? inBase : null;
		}

		private static int ReadTargetSlot(object settings) {
			try {
				dynamic s = settings;
				object value = s.TargetSlot;
				return value == null ? 0 : Convert.ToInt32(value);
			} catch {
				return 0;
			}
		}

		private static string ReadTargetsFile(object settings) {
			try {
				dynamic s = settings;
				object value = s.TargetsFile;
				string text = value == null ? null : Convert.ToString(value);
				return string.IsNullOrWhiteSpace(text) ? DefaultFileName : text;
			} catch {
				return DefaultFileName;
			}
		}

		private static int? ReadCap(object settings, int position) {
			try {
				dynamic s = settings;
				object value = null;
				switch (position) {
					case 1: value = s.MaxBasePopulationBuilding; break;
					case 2: value = s.MaxBaseFoodBuilding; break;
					case 3: value = s.MaxBaseTechBuilding; break;
				}
				return value == null ? (int?) null : Convert.ToInt32(value);
			} catch {
				return null;
			}
		}

		private static string ApplyResearchBoost(JObject root, string race, Celestial celestial, int[] levels, int[] baseLevels, int? techCap) {
			try {
				JObject cfg = root["ResearchBoost"] as JObject;
				if (cfg == null || (cfg["Active"] != null && !(bool) cfg["Active"]) || celestial.LFBuildings == null) {
					return "";
				}
				int t2Slot = cfg["T2Slot"] != null ? (int) cfg["T2Slot"] : 12;
				int boostLevel = cfg["BoostLevel"] != null ? (int) cfg["BoostLevel"] : 15;
				JObject raceObj = (root["Races"] as JObject)?[race] as JObject;
				JObject slotObj = (raceObj?["Slots"] as JObject)?[t2Slot.ToString()] as JObject;
				JObject positions = raceObj?["Positions"] as JObject;
				if (slotObj == null || positions == null) {
					return "";
				}
				int[] stage1 = (int[]) baseLevels.Clone();
				bool[] gelistet = new bool[13];
				foreach (JProperty prop in slotObj.Properties()) {
					if (prop.Name == "Target" || prop.Name.StartsWith("_") || positions[prop.Name] == null) {
						continue;
					}
					int position = (int) positions[prop.Name];
					if (position >= 1 && position <= 12) {
						stage1[position] = (int) prop.Value;
						gelistet[position] = true;
					}
				}
				int foodBonus = raceObj["FoodBonusLevels"] != null ? (int) raceObj["FoodBonusLevels"] : 0;
				if (foodBonus != 0) {
					stage1[2] += foodBonus;
				}
				int[] current = new int[13];
				for (int pos = 1; pos <= 12; pos++) {
					current[pos] = GetByPosition(celestial.LFBuildings, celestial.LFtype, pos);
				}
				return ApplyBoostStages(levels, stage1, current, boostLevel, techCap, t2Slot, gelistet);
			} catch (Exception) {
				return "";
			}
		}

		internal static string ApplyBoostStages(int[] levels, int[] stage1, int[] current, int boostLevel, int? techCap, int t2Slot, bool[] gelistet = null) {
			bool t2Done = true;
			for (int pos = 1; pos <= 12; pos++) {
				if (gelistet != null && !gelistet[pos]) {
					continue;
				}
				int want = Math.Min(stage1[pos], levels[pos]);
				if (current[pos] < want) {
					t2Done = false;
					break;
				}
			}
			if (!t2Done) {
				for (int pos = 1; pos <= 12; pos++) {
					levels[pos] = Math.Min(levels[pos], Math.Max(stage1[pos], current[pos]));
				}
				return $" T2 phase (targets limited to slot {t2Slot})";
			}
			int goal = techCap.HasValue ? Math.Min(boostLevel, techCap.Value) : boostLevel;
			if (current[3] >= goal) {
				return "";
			}
			for (int pos = 1; pos <= 12; pos++) {
				if (pos != 3) {
					levels[pos] = Math.Min(levels[pos], current[pos]);
				}
			}
			levels[3] = Math.Max(levels[3], goal);
			return $" Research phase P3 {current[3]}->{goal} (others frozen)";
		}

		private static int GetByPosition(LFBuildings b, LFTypes type, int pos) {
			switch (type) {
				case LFTypes.Humans:
					switch (pos) {
						case 1: return b.ResidentialSector;
						case 2: return b.BiosphereFarm;
						case 3: return b.ResearchCentre;
						case 4: return b.AcademyOfSciences;
						case 5: return b.NeuroCalibrationCentre;
						case 6: return b.HighEnergySmelting;
						case 7: return b.FoodSilo;
						case 8: return b.FusionPoweredProduction;
						case 9: return b.Skyscraper;
						case 10: return b.BiotechLab;
						case 11: return b.Metropolis;
						case 12: return b.PlanetaryShield;
					}
					break;
				case LFTypes.Mechas:
					switch (pos) {
						case 1: return b.AssemblyLine;
						case 2: return b.FusionCellFactory;
						case 3: return b.RoboticsResearchCentre;
						case 4: return b.UpdateNetwork;
						case 5: return b.QuantumComputerCentre;
						case 6: return b.AutomatisedAssemblyCentre;
						case 7: return b.HighPerformanceTransformer;
						case 8: return b.MicrochipAssemblyLine;
						case 9: return b.ProductionAssemblyHall;
						case 10: return b.HighPerformanceSynthesiser;
						case 11: return b.ChipMassProduction;
						case 12: return b.NanoRepairBots;
					}
					break;
				case LFTypes.Rocktal:
					switch (pos) {
						case 1: return b.MeditationEnclave;
						case 2: return b.CrystalFarm;
						case 3: return b.RuneTechnologium;
						case 4: return b.RuneForge;
						case 5: return b.Oriktorium;
						case 6: return b.MagmaForge;
						case 7: return b.DisruptionChamber;
						case 8: return b.Megalith;
						case 9: return b.CrystalRefinery;
						case 10: return b.DeuteriumSynthesiser;
						case 11: return b.MineralResearchCentre;
						case 12: return b.AdvancedRecyclingPlant;
					}
					break;
				case LFTypes.Kaelesh:
					switch (pos) {
						case 1: return b.Sanctuary;
						case 2: return b.AntimatterCondenser;
						case 3: return b.VortexChamber;
						case 4: return b.HallsOfRealisation;
						case 5: return b.ForumOfTranscendence;
						case 6: return b.AntimatterConvector;
						case 7: return b.CloningLaboratory;
						case 8: return b.ChrysalisAccelerator;
						case 9: return b.BioModifier;
						case 10: return b.PsionicModulator;
						case 11: return b.ShipManufacturingHall;
						case 12: return b.SupraRefractor;
					}
					break;
			}
			return 0;
		}

		private static string RaceKey(LFTypes type) {
			switch (type) {
				case LFTypes.Humans: return "Human";
				case LFTypes.Mechas: return "Mecha";
				case LFTypes.Rocktal: return "Rocktal";
				case LFTypes.Kaelesh: return "Kaelesh";
				default: return null;
			}
		}

		private static void SetByPosition(LFBuildings b, LFTypes type, int pos, int v) {
			switch (type) {
				case LFTypes.Humans:
					switch (pos) {
						case 1: b.ResidentialSector = v; break;
						case 2: b.BiosphereFarm = v; break;
						case 3: b.ResearchCentre = v; break;
						case 4: b.AcademyOfSciences = v; break;
						case 5: b.NeuroCalibrationCentre = v; break;
						case 6: b.HighEnergySmelting = v; break;
						case 7: b.FoodSilo = v; break;
						case 8: b.FusionPoweredProduction = v; break;
						case 9: b.Skyscraper = v; break;
						case 10: b.BiotechLab = v; break;
						case 11: b.Metropolis = v; break;
						case 12: b.PlanetaryShield = v; break;
					}
					break;
				case LFTypes.Mechas:
					switch (pos) {
						case 1: b.AssemblyLine = v; break;
						case 2: b.FusionCellFactory = v; break;
						case 3: b.RoboticsResearchCentre = v; break;
						case 4: b.UpdateNetwork = v; break;
						case 5: b.QuantumComputerCentre = v; break;
						case 6: b.AutomatisedAssemblyCentre = v; break;
						case 7: b.HighPerformanceTransformer = v; break;
						case 8: b.MicrochipAssemblyLine = v; break;
						case 9: b.ProductionAssemblyHall = v; break;
						case 10: b.HighPerformanceSynthesiser = v; break;
						case 11: b.ChipMassProduction = v; break;
						case 12: b.NanoRepairBots = v; break;
					}
					break;
				case LFTypes.Rocktal:
					switch (pos) {
						case 1: b.MeditationEnclave = v; break;
						case 2: b.CrystalFarm = v; break;
						case 3: b.RuneTechnologium = v; break;
						case 4: b.RuneForge = v; break;
						case 5: b.Oriktorium = v; break;
						case 6: b.MagmaForge = v; break;
						case 7: b.DisruptionChamber = v; break;
						case 8: b.Megalith = v; break;
						case 9: b.CrystalRefinery = v; break;
						case 10: b.DeuteriumSynthesiser = v; break;
						case 11: b.MineralResearchCentre = v; break;
						case 12: b.AdvancedRecyclingPlant = v; break;
					}
					break;
				case LFTypes.Kaelesh:
					switch (pos) {
						case 1: b.Sanctuary = v; break;
						case 2: b.AntimatterCondenser = v; break;
						case 3: b.VortexChamber = v; break;
						case 4: b.HallsOfRealisation = v; break;
						case 5: b.ForumOfTranscendence = v; break;
						case 6: b.AntimatterConvector = v; break;
						case 7: b.CloningLaboratory = v; break;
						case 8: b.ChrysalisAccelerator = v; break;
						case 9: b.BioModifier = v; break;
						case 10: b.PsionicModulator = v; break;
						case 11: b.ShipManufacturingHall = v; break;
						case 12: b.SupraRefractor = v; break;
					}
					break;
			}
		}
	}
}