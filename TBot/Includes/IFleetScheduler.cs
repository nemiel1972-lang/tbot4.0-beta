using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tbot.Services;
using Tbot.Workers;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Includes {
	public interface IFleetScheduler {
		void SetTBotInstance(ITBotMain tbotInstance);
		void SetTBotOgameBridge(ITBotOgamedBridge tbotOgameBridge);
		Task SpyCrash(Celestial fromCelestial, Coordinate target = null);
		Task AutoFleetSave(Celestial celestial, bool isSleepTimeFleetSave = false, long minDuration = 0, bool WaitFleetsReturn = false, Missions TelegramMission = Missions.None, bool fromTelegram = false, bool saveall = false);
		Task<int> SendFleet(Celestial origin, Ships ships, Coordinate destination, Missions mission, decimal speed, Resources payload = null, CharacterClass playerClass = CharacterClass.NoClass, bool force = false, bool ignoreLedger = false, long deutToLeave = 0, bool allowSpeedReduction = false, bool shrinkDeuteriumPayload = false, bool fleetsave = false);
		Ships WaehleBrainSchiffe(Celestial origin, Resources payload);
		Ships BrainQuellSchiffe(Celestial origin);
		int BrainMaxSendungen(int freieSlots);
		Task<bool> SendDiscovery(Celestial origin, Coordinate destination);
		Task CancelFleet(Fleet fleet);
		Task<List<Fleet>> UpdateFleets();
		Task ReconcilePendingRecalls();
		Task<ShadowFlightResult> ShadowFlight(Celestial celestial, Ships ships, int deutFlights, long totalSeconds, decimal speed);
		void CancelPendingRecall(int fleetId);
		Task<bool> DebrisSave(Celestial celestial, Coordinate debris, bool random, long durationSeconds, decimal speed);
		void RetireFleet(object fleet);
		Task<int> HandleMinerTransport(Celestial origin, Celestial destination, Celestial differentDestination, Resources resources, Buildables buildable = Buildables.Null, Buildings maxBuildings = null, Facilities maxFacilities = null, Facilities maxLunarFacilities = null, AutoMinerSettings autoMinerSettings = null);
		Task<int> HandleMinerTransport(Celestial origin, Celestial destination, Celestial differentDestination, Resources resources, LFBuildables buildable = LFBuildables.None, LFBuildings maxLFBuildings = null, bool preventIfMoreExpensiveThanNextMine = false);
		Task Collect(bool noLimit = false, string typFilter = null, bool skipIncoming = false);
		Task<RepatriateCode> CollectImpl(bool fromTelegram = false, bool noLimit = false, string typFilter = null, bool skipIncoming = false);
		string PreviewCollect(bool noLimit = false, string typFilter = null, bool skipIncoming = false);
		Task CollectDeut(long minAmount = 0);
	}
}
