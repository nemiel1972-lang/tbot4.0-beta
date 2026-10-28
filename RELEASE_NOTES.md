Changes since beta14. Everything marked *(off by default)* has to be switched on in the config.

## New
- **Directives** *(off by default)*: works through the in-game directive tasks for you (build or research to a level, ships and defence, colonise, expedition, spy mission to position 16 and the recall of that fleet, lithium conversion). Tasks it cannot do are only logged. Has a dry-run mode.
- **Rewards page**: overview of the universe reward tasks (Tritium tasks and ranks).
- **Logistics page**: planner and calculator for fleet transports.
- **New look** of the web UI.
- **Defender test** (Defender page, Test section): plays an assumed attack from A on B through your settings and shows what the bot calculates and would do. Nothing is sent or changed.
- **Bot time** (game server time) on the start page, with conversion to your browser time.
- **ConfigDoctor**: read-only plausibility check of your config (wrong coordinates, impossible values, switches that cancel each other). Results show on the start page and in the bot log. The package also contains a standalone `ConfigDoctor` program (`ConfigDoctor --dir <folder>`; exit code 0 = ok, 1 = warnings, 2 = errors).
- **Expeditions - Smart ships** *(off by default)*: each expedition gets 1 espionage probe, 1 pathfinder and 1 combat ship (Reaper first), if available. If the main cargo ships do not cover all planned expeditions of a start planet, the amount is reduced equally for all and the missing capacity is filled with a second cargo type (`SmartMixShip`). Has no effect while `ManualShips` is active.
- **Harvest**: `DeepSpaceRange` and `ForeignDFRange` (look around X systems for expedition debris and for normal debris), `ScanOnlyExpeditionOrigins` *(off by default)*, and without `AutoHarvest.Origin` the bot looks at the expedition bases and the location with the most pathfinders/recyclers (detected once, remembered in `data/`).
- **Config page**: blocks can be collapsed (click the heading; "Collapse all" next to "Show all help"). The state is remembered per block.
- **Config page**: JSON view notices changes immediately; obsolete keys are hidden and removed on save; `clean_config` removes old entries (backup first).
- **Expeditions**: optional Telegram message for new finds with a collecting window; reasons are logged when nothing is sent.
- **Telegram**: log level per worker, identical lines are sent only once per configured time.
- **Hide data**: one setting for logs and all WebUI pages.
- **Mine plan page**: next step per queue; Cheapest mode drives the plan across all queues.
- **Navigation bar**: build commit and build time.

## Fixed
- Harvest reported "abandoned Execute() cycle sent no heartbeat" several times a day on long cycles.
- Mine plan: overflow beyond plasma level 100, "Invalid Date", step list limit.
- Calculation errors: deuterium mine and Lifeform bonus, cargo capacity, Lifeform speed bonus; slot count uses the Admiral.
- AutoResearch read its interval from a wrong path (0 ms wait); Plasma priority is a hard priority; max levels and priority list are respected by the build plan.
- Discovery missions are no longer sent twice when a response is lost.
- Fleet list: the last known state is kept when a refresh fails.
- AutoColonize: `MaxPlanets` per target range also applies to dispatching.
- Solar satellite demand: division by zero guarded.
- Repatriate: deuterium reserve no longer blocks the fuel.
- Server clock offset (summer/winter time) is refreshed more often.
- Updater: restarts the service itself under systemd.

## Upgrading
Run MigrateConfig in the folder of your old config before the first start. Existing settings keep their behaviour; new switches default to off.

---

# Deutsch

Änderungen seit beta14. Alles, was mit *(standardmäßig aus)* gekennzeichnet ist, muss in der Config eingeschaltet werden.

## Neu
- **Direktiven** *(standardmäßig aus)*: arbeitet die Direktiven-Aufgaben des Spiels für dich ab (bauen oder erforschen bis zu einer Stufe, Schiffe und Verteidigung, kolonisieren, Expedition, Spionagemission zu Position 16 samt Rückruf der Flotte, Lithium-Umwandlung). Was er nicht kann, wird nur geloggt. Mit Probelauf-Modus.
- **Belohnungen-Seite**: Übersicht der Universums-Belohnungen (Tritium-Aufgaben und Ränge).
- **Logistik-Seite**: Planer und Rechner für Flotten-Transporte.
- **Neue Optik** der Web-Oberfläche.
- **Defender-Test** (Defender-Seite, Abschnitt Test): spielt einen angenommenen Angriff von A auf B durch deine Einstellungen durch und zeigt, was der Bot berechnet und tun würde. Es wird nichts gesendet oder geändert.
- **Bot-Zeit** (Serverzeit des Spiels) auf der Startseite, mit Umrechnung auf die Browserzeit.
- **ConfigDoctor**: Plausibilitätsprüfung deiner Config, nur lesend (falsche Koordinaten, unmögliche Werte, Schalter, die sich gegenseitig aufheben). Die Ergebnisse erscheinen auf der Startseite und im Bot-Log. Im Paket liegt außerdem ein eigenständiges Programm `ConfigDoctor` (`ConfigDoctor --dir <Ordner>`; Exit-Code 0 = in Ordnung, 1 = Warnungen, 2 = Fehler).
- **Expeditionen – Smart ships** *(standardmäßig aus)*: Jede Expedition bekommt, falls vorhanden, 1 Spionagesonde, 1 Pfadfinder und 1 Kampfschiff (Reaper zuerst). Reichen die Haupttransporter nicht für alle geplanten Expeditionen eines Startplaneten, wird die Menge für alle gleich gekürzt und die fehlende Ladung mit einem zweiten Transportertyp aufgefüllt (`SmartMixShip`). Wirkungslos, solange `ManualShips` aktiv ist.
- **Harvest**: `DeepSpaceRange` und `ForeignDFRange` (X Systeme um die Start-Himmelskörper für Expeditions- bzw. normale Trümmerfelder), `ScanOnlyExpeditionOrigins` *(standardmäßig aus)*. Ohne `AutoHarvest.Origin` prüft der Bot die Expeditions-Basen und den Standort mit den meisten Pfadfindern/Recyclern (einmal erkannt, in `data/` gemerkt).
- **Config-Seite**: Blöcke lassen sich einklappen (Klick auf die Überschrift; „Collapse all“ neben „Show all help“). Der Zustand wird pro Block gemerkt.
- **Config-Seite**: Die JSON-Ansicht erkennt Änderungen sofort; veraltete Schlüssel werden ausgeblendet und beim Speichern entfernt; `clean_config` entfernt alte Einträge (vorher Sicherung).
- **Expeditionen**: optionale Telegram-Meldung bei neuen Funden mit Sammelfenster; wird nichts gesendet, steht der Grund im Log.
- **Telegram**: Log-Pegel je Worker; gleiche Zeilen werden nur einmal je eingestellter Zeit gesendet.
- **Daten verbergen**: eine Einstellung für Logs und alle WebUI-Seiten.
- **Minenplan-Seite**: nächster Schritt je Warteschlange; der Modus Cheapest steuert den Plan über alle Warteschlangen.
- **Kopfleiste**: Build-Commit und Build-Zeit.

## Behoben
- Harvest meldete bei langen Zyklen mehrmals täglich „abandoned Execute() cycle sent no heartbeat“.
- Minenplan: Überlauf über Plasmatechnik Stufe 100, „Invalid Date“, Begrenzung der Schrittliste.
- Rechenfehler: Deuteriummine und Lebensform-Bonus, Laderaum, Lebensform-Geschwindigkeitsbonus; die Slot-Zahl nutzt den Admiral.
- AutoResearch las sein Intervall von einem falschen Pfad (0 ms Wartezeit); die Plasma-Priorität ist eine harte Priorität; Maximalstufen und Prioritätenliste gelten auch im Bauplan.
- Entdeckungs-Missionen werden nicht mehr doppelt gesendet, wenn eine Antwort verloren geht.
- Flottenliste: Der letzte bekannte Stand bleibt erhalten, wenn eine Aktualisierung fehlschlägt.
- AutoColonize: `MaxPlanets` je Zielbereich gilt auch beim Versand.
- Solarsatelliten-Bedarf: Division durch 0 abgesichert.
- Repatriate: Die Deuterium-Reserve blockiert den Treibstoff nicht mehr.
- Der Zeitversatz des Servers (Sommer-/Winterzeit) wird öfter neu bestimmt.
- Updater: startet unter systemd den Dienst selbst neu.

## Aktualisieren
Vor dem ersten Start MigrateConfig im Ordner deiner alten Config ausführen. Bestehende Einstellungen behalten ihr Verhalten; neue Schalter sind standardmäßig aus.
