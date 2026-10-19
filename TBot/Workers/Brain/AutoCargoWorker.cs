using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tbot.Helpers;
using Tbot.Common.Settings;
using Tbot.Includes;
using Tbot.Services;
using TBot.Common.Logging;
using TBot.Model;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers.Brain {
	public class AutoCargoWorker : WorkerBase {
		private readonly IOgameService _ogameService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly ICalculationService _calculationService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;
		public AutoCargoWorker(ITBotMain parentInstance,
			IOgameService ogameService,
			IFleetScheduler fleetScheduler,
			ICalculationService calculationService,
			ITBotOgamedBridge tbotOgameBridge) :
			base(parentInstance) {
			_ogameService = ogameService;
			_fleetScheduler = fleetScheduler;
			_calculationService = calculationService;
			_tbotOgameBridge = tbotOgameBridge;
		}

		protected override async Task Execute() {
			try {
				DoLog(LogLevel.Information, "Running autocargo...");

				_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
				List<Celestial> newCelestials = _tbotInstance.UserData.celestials.ToList();
				List<Celestial> celestialsToExclude = _calculationService.ParseCelestialsList(_tbotInstance.InstanceSettings.Brain.AutoCargo.Exclude, _tbotInstance.UserData.celestials);

				foreach (Celestial celestial in (bool) _tbotInstance.InstanceSettings.Brain.AutoCargo.RandomOrder ? _tbotInstance.UserData.celestials.Shuffle().ToList() : _tbotInstance.UserData.celestials) {
					if (celestialsToExclude.Has(celestial)) {
						DoLog(LogLevel.Information, $"Skipping {celestial.ToString()}: celestial in exclude list.");
						continue;
					}

					var tempCelestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Fast);

					_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
					if ((bool) _tbotInstance.InstanceSettings.Brain.AutoCargo.SkipIfIncomingTransport && _calculationService.IsThereTransportTowardsCelestial(tempCelestial, _tbotInstance.UserData.fleets)) {
						DoLog(LogLevel.Information, $"Skipping {tempCelestial.ToString()}: there is a transport incoming.");
						continue;
					}

					tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Productions);
					if (tempCelestial.HasProduction()) {
						DoLog(LogLevel.Warning, $"Skipping {tempCelestial.ToString()}: there is already a production ongoing.");
						foreach (Production production in tempCelestial.Productions) {
							Buildables productionType = (Buildables) production.ID;
							DoLog(LogLevel.Information, $"Skipping {tempCelestial.ToString()}: {production.Nbr}x{productionType.ToString()} are already in production.");
						}
						continue;
					}
					tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Constructions);
					if (tempCelestial.Constructions.BuildingID == (int) Buildables.Shipyard || tempCelestial.Constructions.BuildingID == (int) Buildables.NaniteFactory) {
						Buildables buildingInProgress = (Buildables) tempCelestial.Constructions.BuildingID;
						DoLog(LogLevel.Information, $"Skipping {tempCelestial.ToString()}: {buildingInProgress.ToString()} is upgrading.");
						continue;
					}

					tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Ships);
					tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Resources);
					tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.LFBonuses);
					
					var capacity = _calculationService.CalcFleetCapacity(tempCelestial.Ships, _tbotInstance.UserData.serverData, _tbotInstance.UserData.researches.HyperspaceTechnology, tempCelestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
					if (tempCelestial.Coordinate.Type == Celestials.Moon && (bool) _tbotInstance.InstanceSettings.Brain.AutoCargo.ExcludeMoons) {
						DoLog(LogLevel.Information, $"Skipping {tempCelestial.ToString()}: celestial is a moon.");
						continue;
					}
					long neededCargos;
					Buildables preferredCargoShip = Buildables.SmallCargo;
					if (!Enum.TryParse<Buildables>((string) _tbotInstance.InstanceSettings.Brain.AutoCargo.CargoType, true, out preferredCargoShip)) {
						DoLog(LogLevel.Warning, "Unable to parse CargoType. Falling back to default SmallCargo");
						preferredCargoShip = Buildables.SmallCargo;
					}
					if (capacity <= tempCelestial.Resources.TotalResources && (bool) _tbotInstance.InstanceSettings.Brain.AutoCargo.LimitToCapacity) {
						long difference = tempCelestial.Resources.TotalResources - capacity;
						float cargoBonus = tempCelestial.LFBonuses?.GetShipCargoBonus(preferredCargoShip) ?? 0;
						int oneShipCapacity = _calculationService.CalcShipCapacity(preferredCargoShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, cargoBonus, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
						neededCargos = (long) Math.Round((float) difference / (float) oneShipCapacity, MidpointRounding.ToPositiveInfinity);
						DoLog(LogLevel.Information, $"{difference.ToString("N0")} more capacity is needed, {neededCargos} more {preferredCargoShip.ToString()} are needed.");
					} else {
						neededCargos = (long) _tbotInstance.InstanceSettings.Brain.AutoCargo.MaxCargosToKeep - tempCelestial.Ships.GetAmount(preferredCargoShip);
					}
					if (neededCargos > 0) {
						if (neededCargos > (long) _tbotInstance.InstanceSettings.Brain.AutoCargo.MaxCargosToBuild)
							neededCargos = (long) _tbotInstance.InstanceSettings.Brain.AutoCargo.MaxCargosToBuild;

					if (tempCelestial.Ships.GetAmount(preferredCargoShip) + neededCargos > (long) _tbotInstance.InstanceSettings.Brain.AutoCargo.MaxCargosToKeep)
						neededCargos = (long) _tbotInstance.InstanceSettings.Brain.AutoCargo.MaxCargosToKeep - tempCelestial.Ships.GetAmount(preferredCargoShip);
					if (neededCargos < 0)
						neededCargos = 0;

						Resources freierBestand = LedgerTBot.Available(_tbotInstance, tempCelestial, "AutoCargo");
						var cost = _calculationService.CalcPrice(preferredCargoShip, (int) neededCargos);
						if (freierBestand.IsEnoughFor(cost))
							DoLog(LogLevel.Information, $"{tempCelestial.ToString()}: Building {neededCargos}x{preferredCargoShip.ToString()}");
						else {
							var buildableCargos = _calculationService.CalcMaxBuildableNumber(preferredCargoShip, freierBestand);
							DoLog(LogLevel.Information, $"{tempCelestial.ToString()}: Not enough resources to build {neededCargos}x{preferredCargoShip.ToString()}. {buildableCargos.ToString()} will be built instead.");
							neededCargos = buildableCargos;
						}

						double maxHours = 0;
						try { maxHours = (double) _tbotInstance.InstanceSettings.Brain.AutoCargo.MaxProductionHours; } catch (Exception) { }
						if (maxHours > 0 && neededCargos > 0) {
							tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Facilities);
							long unitSeconds = Math.Max(1, _calculationService.CalcProductionTime(preferredCargoShip, 1, _tbotInstance.UserData.serverData, tempCelestial.Facilities));
							long maxUnits = (long) (maxHours * 3600) / unitSeconds;
							if (neededCargos > maxUnits) {
								DoLog(LogLevel.Information, $"{tempCelestial.ToString()}: Production time capped at {maxHours} h: {maxUnits} instead of {neededCargos}x{preferredCargoShip.ToString()}.");
								neededCargos = maxUnits;
							}
						}

						if (neededCargos > 0) {
							try {
								await _ogameService.BuildShips(tempCelestial, preferredCargoShip, neededCargos);
								DoLog(LogLevel.Information, "Production succesfully started.");
							} catch {
								DoLog(LogLevel.Warning, "Unable to start ship production.");
							}
						}

						tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Productions);
						foreach (Production production in tempCelestial.Productions) {
							Buildables productionType = (Buildables) production.ID;
							DoLog(LogLevel.Information, $"{tempCelestial.ToString()}: {production.Nbr}x{productionType.ToString()} are in production.");
						}
					} else {
						DoLog(LogLevel.Information, $"{tempCelestial.ToString()}: No ships will be built.");
					}

					if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.AutoCargo, "SupplyMoonFromPlanet") &&
						(bool) _tbotInstance.InstanceSettings.Brain.AutoCargo.SupplyMoonFromPlanet &&
						tempCelestial.Coordinate.Type == Celestials.Planet) {

						Celestial moon = _tbotInstance.UserData.celestials
							.Unique()
							.Where(c => c.Coordinate.Galaxy == tempCelestial.Coordinate.Galaxy)
							.Where(c => c.Coordinate.System == tempCelestial.Coordinate.System)
							.Where(c => c.Coordinate.Position == tempCelestial.Coordinate.Position)
							.Where(c => c.Coordinate.Type == Celestials.Moon)
							.SingleOrDefault();

						if (moon != null) {
							moon = await _tbotOgameBridge.UpdatePlanet(moon, UpdateTypes.Ships);
							long moonHas = moon.Ships.GetAmount(preferredCargoShip);
							long targetStock = (long) _tbotInstance.InstanceSettings.Brain.AutoCargo.MaxCargosToKeep;
							long moonMissing = targetStock - moonHas;

							if (moonMissing > 0) {
								tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Ships);
								long planetHas = tempCelestial.Ships.GetAmount(preferredCargoShip);
								long planetSpare = planetHas - targetStock;

								if (planetSpare > 0) {
									long toSend = Math.Min(planetSpare, moonMissing);
									Ships shipsToSend = new();
									shipsToSend.Add(preferredCargoShip, toSend);

									DoLog(LogLevel.Information, $"{tempCelestial.ToString()}: Sending {toSend}x{preferredCargoShip.ToString()} to co-located moon {moon.ToString()} (Deploy, Phalanx-safe).");

									try {
										await _fleetScheduler.SendFleet(tempCelestial, shipsToSend, moon.Coordinate, Missions.Deploy, Speeds.HundredPercent);
									} catch (Exception ex) {
										DoLog(LogLevel.Warning, $"Unable to send ships to moon: {ex.Message}");
									}
								}
							}
						}
					}

					newCelestials.Remove(celestial);
					newCelestials.Add(tempCelestial);
				}
				_tbotInstance.UserData.celestials = newCelestials;
			} catch (Exception e) {
				DoLog(LogLevel.Error, $"Unable to complete autocargo: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
			} finally {
					var time = await _tbotOgameBridge.GetDateTime();
					var interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.AutoCargo.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.AutoCargo.CheckIntervalMax);
					if (interval <= 0)
						interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					var newTime = time.AddMilliseconds(interval);
					ChangeWorkerPeriod(interval);
					DoLog(LogLevel.Information, $"Next capacity check at {newTime.ToString()}");
					await _tbotOgameBridge.CheckCelestials();
			}
		}

		public override bool IsWorkerEnabledBySettings() {
			try {
				return ((bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.AutoCargo.Active);
			} catch(Exception) {
				return false;
			}
		}

		public override string GetWorkerName() {
			return "AutoCargo";
		}
		public override Feature GetFeature() {
			return Feature.BrainAutobuildCargo;
		}

		public override LogSender GetLogSender() {
			return LogSender.AutoCargo;
		}
	}
}