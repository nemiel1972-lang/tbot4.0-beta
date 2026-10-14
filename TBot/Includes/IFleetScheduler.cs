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
		Task<int> SendFleet(Celestial origin, Ships ships, Coordinate destination, Missions mission, decimal speed, Resources payload = null, CharacterClass playerClass = CharacterClass.NoClass, bool force = false, bool ignoreLedger = false, long deutToLeave = 0);
		// PATCH Brain transport chain (14.10.2026): ship selection (SC -> LC, mixed fleet, expedition reserve) for the Brain workers.
		// WaehleBrainSchiffe returns null if the origin cannot carry the payload; BrainQuellSchiffe lists the ships usable for planning.
		Ships WaehleBrainSchiffe(Celestial origin, Resources payload);
		Ships BrainQuellSchiffe(Celestial origin);
		int BrainMaxSendungen(int freieSlots);
		// PATCH 37: account-wide serialized discovery dispatch, ported from TBot v3.5.1-beta1's
		// FleetScheduler.SendDiscovery. Wraps the existing IOgameService.SendDiscovery call with a
		// dedicated semaphore so overlapping AutoDiscovery dispatches (and any other worker's discovery
		// sends) cannot race each other. Deliberately NOT wired into SendFleet/DispatchFleet - this only
		// serializes discovery sends against each other, leaving normal fleet sends untouched.
		Task<bool> SendDiscovery(Celestial origin, Coordinate destination);
		Task CancelFleet(Fleet fleet);
		Task<List<Fleet>> UpdateFleets();
		Task ReconcilePendingRecalls();
		Task<bool> DebrisSave(Celestial celestial, Coordinate debris, bool random, long durationSeconds, decimal speed);
		void RetireFleet(object fleet);
		Task<int> HandleMinerTransport(Celestial origin, Celestial destination, Celestial differentDestination, Resources resources, Buildables buildable = Buildables.Null, Buildings maxBuildings = null, Facilities maxFacilities = null, Facilities maxLunarFacilities = null, AutoMinerSettings autoMinerSettings = null);
		Task<int> HandleMinerTransport(Celestial origin, Celestial destination, Celestial differentDestination, Resources resources, LFBuildables buildable = LFBuildables.None, LFBuildings maxLFBuildings = null, bool preventIfMoreExpensiveThanNextMine = false);
		Task Collect(bool noLimit = false);
		Task<RepatriateCode> CollectImpl(bool fromTelegram = false, bool noLimit = false);
		Task CollectDeut(long minAmount = 0);
	}
}
