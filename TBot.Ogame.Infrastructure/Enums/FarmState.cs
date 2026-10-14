using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Enums {
	public enum FarmState {
		Idle,
		ProbesPending,
		ProbesSent,
		ProbesRequired,
		FailedProbesRequired,
		AttackPending,
		AttackSent,
		NotSuitable
	}
}
