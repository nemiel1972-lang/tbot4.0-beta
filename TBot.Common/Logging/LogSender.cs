using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Common.Logging {
	public enum LogSender {
		Main,
		Tbot,
		OGameD,
		Defender,
		Brain,
		AutoCargo,
		AutoDefence,
		AutoMine,
		AutoRepatriate,
		AutoResearch,
		BuyOfferOfTheDay,
		KeepBoosters,
		LifeformsAutoMine,
		LifeformsAutoResearch,
		AutoFleetJumpGate,
		Expeditions,
		Harvest,
		AsteroidHunt,
		ShadowFleet,
		FleetScheduler,
		SleepMode,
		Colonize,
		AutoFarm,
#if FASTFARM
		FastFarm,
#endif
		AutoDiscovery,
		GalaxyCache,
		Directives,
		Telegram
	}
}
