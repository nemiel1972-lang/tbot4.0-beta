using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Tbot.Services {
	public class SettingsFileWatcher {

		private Action _watchFunc;
		private string _absFpToWatch;
		private string[] _extraGlobs;
		private SemaphoreSlim _changedSem = new SemaphoreSlim(1, 1);
		private PhysicalFileProvider p;
		private IChangeToken changeToken;
		private IDisposable changeCallback;

		// extraGlobs: zusaetzliche Muster relativ zum Ordner der Hauptdatei, z. B. "conf.d/**/*.json" (per Load eingebundene Dateien)
		public SettingsFileWatcher(Action func, string filePathToWatch, string[] extraGlobs = null) {
			_watchFunc = func;
			_absFpToWatch = filePathToWatch;
			_extraGlobs = extraGlobs ?? Array.Empty<string>();

			initWatch();
		}

		private void initWatch() {
			p = new PhysicalFileProvider(Path.GetDirectoryName(_absFpToWatch));
			var tokens = new List<IChangeToken> { p.Watch(Path.GetFileName(_absFpToWatch)) };
			foreach (var glob in _extraGlobs)
				tokens.Add(p.Watch(glob));
			changeToken = tokens.Count == 1 ? tokens[0] : new CompositeChangeToken(tokens);
			changeCallback = changeToken.RegisterChangeCallback(onChanged, default);
		}

		public void deinitWatch() {
			if (changeCallback != null) {
				changeCallback.Dispose();
				changeCallback = null;
			}
		}

		private async void onChanged(object state) {

			await _changedSem.WaitAsync();
			_watchFunc();
			_changedSem.Release();

			initWatch();
		}
	}
}
