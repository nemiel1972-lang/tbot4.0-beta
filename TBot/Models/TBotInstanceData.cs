using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Tbot.Services;

namespace Tbot.Models {
	sealed class TbotInstanceData : IEquatable<TbotInstanceData> {
		public ITBotMain _botMain;
		public IServiceScope ServiceScope;
		public string _botSettingsPath;
		public string _alias;

		public TbotInstanceData(ITBotMain botMain, IServiceScope serviceScope, string botSettingsPath, string alias) {
			_botMain = botMain;
			_botSettingsPath = botSettingsPath;
			_alias = alias;
			ServiceScope = serviceScope;
		}

		public bool Equals(TbotInstanceData other) {
			// Equality must check of settings path is the same, since user may change Alias and settings internal data
			if (other == null)
				return false;

			return (_botSettingsPath == other._botSettingsPath);
		}

		public async Task Deinitialize() {
			await _botMain.DisposeAsync();

			// BUGFIX (25.09.2026): ServiceScope.Dispose() (synchron) wirft bei jedem echten
			// Prozess-Stop (systemctl stop/restart, SIGTERM) eine ungefangene
			// InvalidOperationException ("'Tbot.Services.TBotMain' type only implements
			// IAsyncDisposable. Use DisposeAsync to dispose the container."), weil der DI-Scope
			// TBotMain als scoped Service selbst mitverwaltet und beim Aufloesen ALLER im Scope
			// registrierten Disposables feststellt, dass TBotMain nur IAsyncDisposable
			// implementiert - das manuelle await _botMain.DisposeAsync() oben aendert daran
			// nichts, der Container versucht trotzdem noch einmal, TBotMain synchron zu
			// entsorgen. Ergebnis: Unhandled Exception -> Prozess-Abort (ABRT/core-dump) statt
			// sauberem Shutdown, bei JEDEM Neustart, nicht nur unter Netzwerkstoerung. Fix: den
			// Scope asynchron entsorgen, wenn die konkrete Implementierung das unterstuetzt
			// (ServiceProviderEngineScope tut das seit .NET Core 3.0) - IServiceScope selbst
			// deklariert kein DisposeAsync, deshalb der Laufzeit-Check statt eines direkten Casts.
			if (ServiceScope is IAsyncDisposable asyncDisposableScope) {
				await asyncDisposableScope.DisposeAsync();
			} else {
				ServiceScope.Dispose();
			}
		}
	}
}