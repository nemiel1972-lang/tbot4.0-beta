using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TBot.Common.Logging;
using Tbot.Includes;
using TBot.Ogame.Infrastructure.Enums;
using Tbot.Services;
using System.Threading;
using Microsoft.Extensions.Logging;
using Tbot.Helpers;
using TBot.Model;
using TBot.Ogame.Infrastructure.Models;
using TBot.Ogame.Infrastructure;

namespace Tbot.Workers {
	public class HarvestWorker : WorkerBase {
		private readonly IOgameService _ogameService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly ICalculationService _calculationService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;
		public HarvestWorker(ITBotMain parentInstance,
			IOgameService ogameService,
			IFleetScheduler fleetScheduler,
			ICalculationService calculationService,
			ITBotOgamedBridge tbotOgameBridge) :
			base(parentInstance) {
			_fleetScheduler = fleetScheduler;
			_ogameService = ogameService;
			_calculationService = calculationService;
			_tbotOgameBridge = tbotOgameBridge;
		}
		protected override TimeSpan ExecuteTimeout => TimeSpan.FromMinutes(15);

		private List<(int Galaxy, int System)> ReadScanCentersFromExpeditionOrigins() {
			try {
				var ah = _tbotInstance.InstanceSettings.AutoHarvest;
				if (!Tbot.Common.Settings.SettingsService.IsSettingSet(ah, "ScanOnlyExpeditionOrigins") || !(bool) ah.ScanOnlyExpeditionOrigins)
					return null;
				var list = new List<(int Galaxy, int System)>();
				foreach (var configured in _tbotInstance.InstanceSettings.Expeditions.Origin)
					list.Add(((int) configured.Galaxy, (int) configured.System));
				return list.Count > 0 ? list : null;
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"AutoHarvest.ScanOnlyExpeditionOrigins: Expeditions.Origin could not be read, scanning around all planets: {e.Message}");
				return null;
			}
		}

		private DateTime _lastOriginDetection = DateTime.MinValue;
		private string DetectedOriginFile => System.IO.Path.Combine(AppContext.BaseDirectory, "data", System.IO.Path.GetFileName($"harvest_origin_{_tbotInstance.InstanceAlias}.json"));
		private Coordinate LoadDetectedOrigin() {
			try {
				if (!System.IO.File.Exists(DetectedOriginFile)) return null;
				var j = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(DetectedOriginFile));
				return new Coordinate((int) j["Galaxy"], (int) j["System"], (int) j["Position"], Enum.Parse<Celestials>((string) j["Type"]));
			} catch (Exception) { return null; }
		}
		private void SaveDetectedOrigin(Coordinate c) {
			try {
				System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(DetectedOriginFile));
				Tbot.Common.IO.AtomicFile.WriteAllText(DetectedOriginFile, Newtonsoft.Json.JsonConvert.SerializeObject(new { Galaxy = (int) c.Galaxy, System = (int) c.System, Position = (int) c.Position, Type = c.Type.ToString() }));
			} catch (Exception e) { DoLog(LogLevel.Warning, $"Could not save the detected harvest origin: {e.Message}"); }
		}

		public override bool IsWorkerEnabledBySettings() {
			try {
				return (bool) _tbotInstance.InstanceSettings.AutoHarvest.Active;
			} catch (Exception) {
				return false;
			}
		}
		public override string GetWorkerName() {
			return "Harvest";
		}
		public override Feature GetFeature() {
			return Feature.Harvest;
		}

		public override LogSender GetLogSender() {
			return LogSender.Harvest;
		}

		protected override async Task Execute() {
			bool stop = false;
			bool delay = false;
			try {

				if ((bool) _tbotInstance.InstanceSettings.AutoHarvest.Active) {
					DoLog(LogLevel.Information, "Detecting harvest targets");

					_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();

					List<Celestial> celestialSnapshot = _tbotInstance.UserData.celestials.ToList();

					List<Celestial> harvestOrigins = celestialSnapshot;
					bool detectOriginNeeded = false;
					if (Tbot.Common.Settings.SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoHarvest, "Origin")) {
						var configuredOrigins = _calculationService.ParseCelestialsList(_tbotInstance.InstanceSettings.AutoHarvest.Origin, celestialSnapshot);
						if (configuredOrigins.Count > 0)
							harvestOrigins = configuredOrigins;
					} else {
						harvestOrigins = new List<Celestial>();
						{
							void AddOrigin(Celestial c) { if (c != null && !harvestOrigins.Any(o => o.Coordinate.IsSame(c.Coordinate))) harvestOrigins.Add(c); }
							try { foreach (var o in _calculationService.ParseCelestialsList(_tbotInstance.InstanceSettings.Expeditions.Origin, celestialSnapshot)) AddOrigin(o); } catch (Exception) { }
							var remembered = LoadDetectedOrigin();
							if (remembered != null)
								AddOrigin(celestialSnapshot.FirstOrDefault(c => c.Coordinate.IsSame(remembered)));
							detectOriginNeeded = remembered == null || DateTime.UtcNow - _lastOriginDetection > TimeSpan.FromHours(1);
						}
					}

					List<Celestial> updatedCelestials = new();
					foreach (Celestial c in celestialSnapshot) {
						Heartbeat();
						if (harvestOrigins.Any(o => o.Coordinate.IsSame(c.Coordinate))) {
							Celestial updated = await _tbotOgameBridge.UpdatePlanet(c, UpdateTypes.Ships);
							if (updated.Ships == null) {
								await Task.Delay(TimeSpan.FromSeconds(10));
								updated = await _tbotOgameBridge.UpdatePlanet(updated, UpdateTypes.Ships);
								if (updated.Ships == null)
									DoLog(LogLevel.Warning, $"Harvest: could not read the ships of {updated.Coordinate} (ogamed not reachable?). This origin is treated as empty for this check.");
							}
							updatedCelestials.Add(updated);
						} else {
							updatedCelestials.Add(c);
						}
					}
					if (detectOriginNeeded) {
						bool candidatesEmpty = updatedCelestials.Where(c => harvestOrigins.Any(o => o.Coordinate.IsSame(c.Coordinate)))
							.All(c => (c.Ships?.Pathfinder ?? 0) + (c.Ships?.Recycler ?? 0) == 0);
						if (LoadDetectedOrigin() == null || candidatesEmpty) {
							_lastOriginDetection = DateTime.UtcNow;
							for (int i = 0; i < updatedCelestials.Count; i++) {
								Heartbeat();
								if (!harvestOrigins.Any(o => o.Coordinate.IsSame(updatedCelestials[i].Coordinate)))
									updatedCelestials[i] = await _tbotOgameBridge.UpdatePlanet(updatedCelestials[i], UpdateTypes.Ships);
							}
							long InFlight(Celestial c) => (_tbotInstance.UserData.fleets ?? new List<Fleet>())
								.Where(f => f.Origin != null && f.Origin.IsSame(c.Coordinate) && f.Ships != null)
								.Sum(f => (long) f.Ships.Pathfinder + f.Ships.Recycler);
							long Score(Celestial c) => (c.Ships?.Pathfinder ?? 0) + (c.Ships?.Recycler ?? 0) + InFlight(c);
							var best = updatedCelestials.OrderByDescending(c => Score(c)).FirstOrDefault();
							if (best != null && Score(best) > 0) {
								SaveDetectedOrigin(best.Coordinate);
								if (!harvestOrigins.Any(o => o.Coordinate.IsSame(best.Coordinate)))
									harvestOrigins.Add(best);
								DoLog(LogLevel.Information, $"Harvest origin detected: {best.Coordinate} holds the most harvest ships ({best.Ships?.Pathfinder ?? 0} pathfinders, {best.Ships?.Recycler ?? 0} recyclers at home, {InFlight(best)} in flight). It is remembered and used as an additional start (set AutoHarvest.Origin to override).");
							} else {
								DoLog(LogLevel.Information, "Harvest origin detection: no celestial holds pathfinders or recyclers (at home or in flight). Will look again in about an hour.");
							}
						}
					}
					_tbotInstance.UserData.celestials = updatedCelestials.ToList();

					var targets = new List<HarvestTarget>();
					var targetShipsNeeded = new Dictionary<Coordinate, long>();

					List<Celestial> newCelestials = updatedCelestials.ToList();

					var expoCenters = ReadScanCentersFromExpeditionOrigins();
					bool IsScanCenter(Coordinate c) => expoCenters == null || expoCenters.Any(e => e.Galaxy == c.Galaxy && e.System == c.System);
					var scannedDeepSpace = new HashSet<(int Galaxy, int System)>();
					foreach (Planet planet in updatedCelestials.Where(c => c is Planet).Cast<Planet>()) {
						Heartbeat();
						Planet tempCelestial = await _tbotOgameBridge.UpdatePlanet(planet, UpdateTypes.Fast) as Planet;

						if ((bool) _tbotInstance.InstanceSettings.AutoHarvest.HarvestOwnDF) {
							Coordinate dest = new(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Debris);
							if (!_tbotInstance.UserData.fleets.Any(f => f.Mission == Missions.Harvest && f.Destination != null && f.Destination.IsSame(dest))) {
								tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Debris) as Planet;
								if (tempCelestial.Debris != null && tempCelestial.Debris.Resources.TotalResources >= (long) _tbotInstance.InstanceSettings.AutoHarvest.MinimumResourcesOwnDF) {
									if (!targets.Any(t => t.Destination.IsSame(dest))) {
										targets.Add(new HarvestTarget(dest, tempCelestial.Debris.Resources, isOwnDebris: true));
										targetShipsNeeded[dest] = tempCelestial.Debris.RecyclersNeeded;
									}
								}
							}
						}

						if ((bool) _tbotInstance.InstanceSettings.AutoHarvest.HarvestDeepSpace && IsScanCenter(tempCelestial.Coordinate)) {
							int deepSpaceRange;
							try {
								deepSpaceRange = (int) _tbotInstance.InstanceSettings.AutoHarvest.DeepSpaceRange;
							} catch (Exception) {
								deepSpaceRange = 1;
							}
							for (int i = -deepSpaceRange; i <= deepSpaceRange; i++) {
								Coordinate destination = new Coordinate {
									Galaxy = tempCelestial.Coordinate.Galaxy,
									System = tempCelestial.Coordinate.System + i,
									Position = 16,
									Type = Celestials.DeepSpace
								};
								destination.System = GeneralHelper.WrapSystem(destination.System);

								if (targets.Any(t => t.Destination.IsSame(destination)))
									continue;
								if (_tbotInstance.UserData.fleets.Any(f => f.Mission == Missions.Harvest && f.Destination != null && f.Destination.IsSame(destination)))
									continue;

								if (!scannedDeepSpace.Add((destination.Galaxy, destination.System)))
									continue;
								Heartbeat();
								await Task.Delay(RandomizeHelper.CalcRandomInterval(IntervalType.LessThanASecond));
								ExpeditionDebris expoDebris = (await _ogameService.GetGalaxyInfo(destination)).ExpeditionDebris;
								if (expoDebris != null && expoDebris.Resources.TotalResources >= (long) _tbotInstance.InstanceSettings.AutoHarvest.MinimumResourcesDeepSpace) {
									targets.Add(new HarvestTarget(destination, expoDebris.Resources, isOwnDebris: false));
									targetShipsNeeded[destination] = expoDebris.PathfindersNeeded;
								}
							}
						}

						newCelestials.Remove(planet);
						newCelestials.Add(tempCelestial);
					}
					_tbotInstance.UserData.celestials = newCelestials;

					if (Tbot.Common.Settings.SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoHarvest, "HarvestForeignDF")
						&& (bool) _tbotInstance.InstanceSettings.AutoHarvest.HarvestForeignDF) {
						long minForeignDF = Tbot.Common.Settings.SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoHarvest, "MinimumResourcesForeignDF")
							? (long) _tbotInstance.InstanceSettings.AutoHarvest.MinimumResourcesForeignDF
							: (long) _tbotInstance.InstanceSettings.AutoHarvest.MinimumResourcesOwnDF;

						var scannedSystems = new HashSet<(int Galaxy, int System)>();
						int foreignRange = Tbot.Common.Settings.SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoHarvest, "ForeignDFRange") ? Math.Max(0, (int) _tbotInstance.InstanceSettings.AutoHarvest.ForeignDFRange) : 0;
						foreach (Planet ownPlanet in updatedCelestials.Where(c => c is Planet && IsScanCenter(c.Coordinate)).Cast<Planet>()) {
							for (int off = -foreignRange; off <= foreignRange; off++) {
								int scanSystem = GeneralHelper.WrapSystem(ownPlanet.Coordinate.System + off);
								var sysKey = (ownPlanet.Coordinate.Galaxy, scanSystem);
								if (!scannedSystems.Add(sysKey))
									continue;

								Heartbeat();
								await Task.Delay(RandomizeHelper.CalcRandomInterval(IntervalType.LessThanASecond));
								var galaxyInfo = await _ogameService.GetGalaxyInfo(new Coordinate(ownPlanet.Coordinate.Galaxy, scanSystem, ownPlanet.Coordinate.Position, Celestials.Planet));
								foreach (var foreignPlanet in galaxyInfo.Planets ?? new List<Planet>()) {
									if (foreignPlanet?.Coordinate == null || foreignPlanet.Debris == null)
										continue;
									if (_tbotInstance.UserData.celestials.Any(c => c.Coordinate.Galaxy == foreignPlanet.Coordinate.Galaxy
										&& c.Coordinate.System == foreignPlanet.Coordinate.System
										&& c.Coordinate.Position == foreignPlanet.Coordinate.Position))
										continue;
									if (foreignPlanet.Debris.Resources.TotalResources < minForeignDF)
										continue;
									Coordinate dest = new(foreignPlanet.Coordinate.Galaxy, foreignPlanet.Coordinate.System, foreignPlanet.Coordinate.Position, Celestials.Debris);
									if (targets.Any(t => t.Destination.IsSame(dest)))
										continue;
									if (_tbotInstance.UserData.fleets.Any(f => f.Mission == Missions.Harvest && f.Destination != null && f.Destination.IsSame(dest)))
										continue;
									targets.Add(new HarvestTarget(dest, foreignPlanet.Debris.Resources, isOwnDebris: true));
									targetShipsNeeded[dest] = foreignPlanet.Debris.RecyclersNeeded;
								}
							}
						}
					}

					if (targets.Count == 0)
						DoLog(LogLevel.Information, "Skipping harvest: there are no fields to harvest.");

					var origins = _tbotInstance.UserData.celestials
						.Where(c => harvestOrigins.Any(o => o.Coordinate.IsSame(c.Coordinate)))
						.Select(c => new HarvestOriginState(c))
						.Where(o => o.AvailableRecyclers > 0 || o.AvailablePathfinders > 0)
						.ToList();

					int DistanceFn(HarvestTarget target, HarvestOriginState origin) {
						var oc = origin.Celestial.Coordinate;
						var dc = target.Destination;
						if (oc.Galaxy != dc.Galaxy)
							return 100000 + Math.Abs(oc.Galaxy - dc.Galaxy) * 1000;
						return Math.Abs(oc.System - dc.System);
					}

					long RequiredShipsFn(HarvestTarget target, HarvestOriginState origin) {
						return targetShipsNeeded.TryGetValue(target.Destination, out long needed) ? needed : 0;
					}

					int configuredMaxSlots;
					try {
						configuredMaxSlots = (int) _tbotInstance.InstanceSettings.AutoHarvest.MaxSlots;
					} catch (Exception) {
						configuredMaxSlots = 1;
					}
					int activeHarvestMissions = _tbotInstance.UserData.fleets.Count(f => f.Mission == Missions.Harvest);
					int availableSlots = Math.Max(0, configuredMaxSlots - activeHarvestMissions);

					var assignments = HarvestPlanner.Build(targets, origins, availableSlots, RequiredShipsFn, DistanceFn);

					if (targets.Count > 0 && availableSlots == 0)
						DoLog(LogLevel.Information, $"Skipping harvest: AutoHarvest.MaxSlots ({configuredMaxSlots}) already fully used by in-flight harvest missions.");
					else if (targets.Count > 0 && assignments.Count == 0) {
						DoLog(LogLevel.Information, "Skipping harvest: no origin has enough recyclers/pathfinders for any target.");
						if (ExtraLog.Erzeugen(_tbotInstance)) try {
							string herkunft = origins.Count == 0
								? "none of the harvest origins [" + string.Join(", ", harvestOrigins.Select(h => h.Coordinate.ToString() + " R=" + (_tbotInstance.UserData.celestials.FirstOrDefault(c => c.Coordinate.IsSame(h.Coordinate))?.Ships?.Recycler ?? 0) + " P=" + (_tbotInstance.UserData.celestials.FirstOrDefault(c => c.Coordinate.IsSame(h.Coordinate))?.Ships?.Pathfinder ?? 0))) + "] has idle recyclers or pathfinders (AutoHarvest.Origin" + (Tbot.Common.Settings.SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoHarvest, "Origin") ? " is set" : " is not set, expedition bases and the detected main fleet location are used") + ")"
								: "origins " + string.Join(", ", origins.Select(o => o.Celestial.Coordinate.ToString() + " R=" + o.AvailableRecyclers + " P=" + o.AvailablePathfinders));
							string ziele = string.Join(", ", targets.Take(8).Select(tg => tg.Destination.ToString() + " needs " + (targetShipsNeeded.TryGetValue(tg.Destination, out long need) ? need : 0) + (tg.IsOwnDebris ? " recyclers" : " pathfinders")));
							ExtraLog.Write(_tbotInstance, "Harvest", $"Harvest check: {herkunft}; targets: {ziele}{(targets.Count > 8 ? " ..." : "")}");
						} catch (Exception) { }
					}

					foreach (var assignment in assignments) {
						Heartbeat();
						var fleetId = (int) SendFleetCode.GenericError;
						Coordinate destination = assignment.Target.Destination;

						if (assignment.Ship == Buildables.Pathfinder) {
							DoLog(LogLevel.Information, $"Harvesting debris in {destination.ToString()} from {assignment.Origin.ToString()} with {assignment.ShipsToSend.ToString()} {Buildables.Pathfinder.ToString()}");
							fleetId = await _fleetScheduler.SendFleet(assignment.Origin, new Ships { Pathfinder = assignment.ShipsToSend }, destination, Missions.Harvest, Speeds.HundredPercent);
						} else {
							DoLog(LogLevel.Information, $"Harvesting debris in {destination.ToString()} from {assignment.Origin.ToString()} with {assignment.ShipsToSend.ToString()} {Buildables.Recycler.ToString()}");
							fleetId = await _fleetScheduler.SendFleet(assignment.Origin, new Ships { Recycler = assignment.ShipsToSend }, destination, Missions.Harvest, Speeds.HundredPercent);
						}

						if (fleetId == (int) SendFleetCode.AfterSleepTime) {
							stop = true;
							return;
						}
						if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
							delay = true;
							return;
						}
					}

					_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
					long interval;
					if (_tbotInstance.UserData.fleets.Any(f => f.Mission == Missions.Harvest)) {
						interval = (_tbotInstance.UserData.fleets
							.Where(f => f.Mission == Missions.Harvest)
						.OrderBy(f => f.BackIn)
						.First()
							.BackIn ?? 0) * 1000;
					} else {
						interval = (int) RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.AutoHarvest.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.AutoHarvest.CheckIntervalMax);
					}
					var time = await _tbotOgameBridge.GetDateTime();
					if (interval <= 0)
						interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					DateTime newTime = time.AddMilliseconds(interval);
					ChangeWorkerPeriod(interval);
					DoLog(LogLevel.Information, $"Next check at {newTime.ToString()}");
				}
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"HandleHarvest exception: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
				long interval = (int) RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.AutoHarvest.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.AutoHarvest.CheckIntervalMax);
				var time = await _tbotOgameBridge.GetDateTime();
				if (interval <= 0)
					interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				ChangeWorkerPeriod(interval);
				DoLog(LogLevel.Information, $"Next check at {newTime.ToString()}");
			} finally {
				if (!_tbotInstance.UserData.isSleeping) {
					if (stop) {
						DoLog(LogLevel.Information, $"Stopping feature.");
						await EndExecution();
					}
					if (delay) {
						DoLog(LogLevel.Information, $"Delaying...");
						var time = await _tbotOgameBridge.GetDateTime();
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						long interval = (_tbotInstance.UserData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						var newTime = time.AddMilliseconds(interval);
						ChangeWorkerPeriod(interval);
						DoLog(LogLevel.Information, $"Next check at {newTime.ToString()}");
					}
					await _tbotOgameBridge.CheckCelestials();
				}
			}
		}
	}
}