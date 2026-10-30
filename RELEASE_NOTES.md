Changes since beta15. Everything marked *(off by default)* has to be switched on in the config.

## New
- **Build planning** menu entry: Autobuilds (Build order) and Mine plan (Build calculation) are tabs under one entry.
- **Expedition moon supply** *(off by default)*: `AutoRepatriate.ExpoSupply` sends missing ships and deuterium from the base to the expedition moons in the next repatriate cycle. `AutoFleetJumpGate.ExpoOverride` *(off by default)* keeps only the planned expeditions plus `Expeditions.BackupExpeditions` full fleets on the expedition moons.
- **Config page**: the Expeditions page also shows the expedition moon options (jump gate, supply, slots research) in one place.
- **ConfigDoctor**: suggestions have a "Known" checkbox that hides a suggestion for good ("Show again" resets it).
- **Rewards page**: switch for the Telegram messages.
- **AutoFarm** `NotSuitableRetryMinutes` (default `720`): targets found unsuitable are probed again earlier than `KeepReportFor`, so daily runs reach them again. `0` = same as `KeepReportFor`.
- **Lifeform targets**: optional `PopulationBonusLevels` per race in `lifeform_targets.json` (fine tuning of the population building, like `FoodBonusLevels`); a `MaxBase...Building` limit of `0` now means no extra limit.
- **Rewards**: the bot also opens the resource market, another galaxy view and the inventory for those daily tasks.
- **ConfigDoctor** warns when the manual expedition fleet carries fewer large cargo than the Lifeform-based need.
- **Browser tiles**: the button column collapses to one symbol in a narrow window; arrow buttons move it by 2 px (Shift 20 px).
- **Expeditions**: below the deuterium limit the expeditions no longer stop; the supply tops the moons up and they keep running. New `Expeditions.StopBelowNeed` *(off by default)* stops them only when deuterium plus cargo for the planned expeditions is missing. Repatriate keeps the deuterium reserve (`MinDeuteriumOnMoon`) on the expedition moons, and the automatic pickup order brings missing deuterium along. `ExpoSupply` also works for planets.
- **WebUI**: "Hide data" switch on the Fleeter page; Fleeter and Galaxy map are opaque.
- **AutoMine**: moons other than the home moon use the lunar caps from the Colony block.

## Fixed
- AutoFarm: the number of cargo ships uses the Lifeform cargo bonus of the sending planet, and cargo capacity is checked again right before sending.
- AutoFarm: a target was treated as defended when a ship or defence entry with 0 was in the report.
- Defender: `WeakAttackRatio` of 0 no longer causes a division by zero.
- Captcha solver reads the picture width instead of a fixed size; two wrong entries in its table.
- The Expeditions page did not load in German; missing German translations added.
- Sleep: the sleep status tile and the GhostSleep schedule loaded a game page every 30-60 s while the bot slept, so the active celestial changed in the game. While asleep the clock no longer needs a game request. No rewards capture and no defender fake activity while the bot sleeps.
- AutoFarm: targets whose espionage report never arrived stayed skipped forever; they are released after 60 minutes.
- Lifeform research boost: the research phase no longer freezes the other buildings; only the research building waits.
- The version number is now also set in plain builds (it was always 4.0.0.0 without a release tag).
- AutoFarm: with `StopAfterFullScan` the worker no longer stops while targets still wait for a probe, a report or an attack.
- AutoFarm: after a long pause, stored targets with an outdated or missing report are reset to probing, stored probe targets with no report and no probe on its way are released right away, and an old scan cursor is discarded (log: "Startup: ...").
- AutoFarm: `ProcessAllReports` (default `true`) can be switched off again; the log lines for reports of targets AutoFarm did not probe itself are clearer.
- Expeditions: the deuterium alarm (Telegram) only comes when the expedition moon cannot cover 2 expedition fleets any more; below the target but above that, the expeditions just keep running and the repatriate round resupplies.
- AutoFarm: two summary lines per run in the log ("AutoFarm start summary" with target states, scan cursor, cache and limits; "AutoFarm run summary" with scanned/skipped systems, skip reasons, probes sent and why the run ended), to find out why a run sends no probes.

## Upgrading
Update as usual. New switches default to off; existing settings keep their behaviour.

---

# Deutsch

Änderungen seit beta15. Alles, was mit *(standardmäßig aus)* gekennzeichnet ist, muss in der Config eingeschaltet werden.

## Neu
- **Bauplanung** als Menüeintrag: Autobuilds (Bauplan) und Minenplan (Bauberechnung) sind Reiter unter einem Eintrag.
- **Versorgung der Expeditionsmonde** *(standardmäßig aus)*: `AutoRepatriate.ExpoSupply` schickt fehlende Schiffe und Deuterium von der Basis zu den Expeditionsmonden im nächsten Repatriate-Zyklus. `AutoFleetJumpGate.ExpoOverride` *(standardmäßig aus)* lässt auf den Expeditionsmonden nur die geplanten Expeditionen plus `Expeditions.BackupExpeditions` komplette Flotten stehen.
- **Config-Seite**: Die Seite Expeditionen zeigt auch die Expeditionsmond-Optionen (Sprungtor, Versorgung, Slot-Forschung) an einer Stelle.
- **ConfigDoctor**: Hinweise haben ein Häkchen „Bekannt“, das einen Hinweis dauerhaft ausblendet („Wieder anzeigen“ setzt es zurück).
- **Belohnungen-Seite**: Schalter für die Telegram-Meldungen.
- **AutoFarm** `NotSuitableRetryMinutes` (Standard `720`): als ungeeignet erkannte Ziele werden früher erneut ausspioniert als nach `KeepReportFor`, damit tägliche Läufe sie wieder erreichen. `0` = wie `KeepReportFor`.
- **Lebensform-Ziele**: optional `PopulationBonusLevels` je Rasse in `lifeform_targets.json` (Feintuning des Bevölkerungsgebäudes wie `FoodBonusLevels`); ein Limit `MaxBase...Building` von `0` bedeutet jetzt kein Zusatzlimit.
- **Belohnungen**: Der Bot öffnet für die Tagesaufgaben auch den Rohstoffmarkt, eine andere Galaxieansicht und das Inventar.
- **ConfigDoctor** warnt, wenn die manuelle Expeditionsflotte weniger Große Transporter hat als der Lebensform-Bedarf.
- **Browser-Kacheln**: Die Knopfspalte klappt in einem schmalen Fenster zu einem Symbol zusammen; Pfeiltasten verschieben sie um 2 px (Shift 20 px).
- **Expeditionen**: Unter dem Deuterium-Limit stoppen die Expeditionen nicht mehr; die Versorgung füllt die Monde auf und sie laufen weiter. Neu `Expeditions.StopBelowNeed` *(standardmäßig aus)* stoppt sie nur, wenn Deuterium plus Ladung für die geplanten Expeditionen fehlt. Repatriate hält die Deuteriumreserve (`MinDeuteriumOnMoon`) auf den Expeditionsmonden, der automatische Abholungsauftrag bringt fehlendes Deuterium mit. `ExpoSupply` funktioniert auch für Planeten.
- **WebUI**: Schalter „Daten ausblenden“ auf der Fleeter-Seite; Fleeter und Galaxiekarte sind nicht mehr transparent.
- **AutoMine**: Monde außer dem Heimatmond nutzen die Mondgrenzen aus dem Colony-Block.

## Behoben
- AutoFarm: Die Zahl der Transporter nutzt den Lebensform-Laderaumbonus des sendenden Planeten, und der Laderaum wird direkt vor dem Senden nochmals geprüft.
- AutoFarm: Ein Ziel galt als verteidigt, wenn im Bericht ein Schiff oder eine Verteidigung mit 0 stand.
- Defender: `WeakAttackRatio` von 0 verursacht keine Division durch 0 mehr.
- Captcha-Löser liest die Bildbreite statt einer festen Größe; zwei falsche Einträge in seiner Tabelle.
- Die Seite Expeditionen lud auf Deutsch nicht; fehlende deutsche Übersetzungen ergänzt.
- Schlaf: Die Statuskachel und der GhostSleep-Zeitplan luden alle 30-60 s eine Spielseite, während der Bot schlief, deshalb wechselte der aktive Himmelskörper im Spiel. Im Schlaf braucht die Uhrzeit keine Spielanfrage mehr. Keine Rewards-Erfassung und keine Schein-Aktivität des Defenders, solange der Bot schläft.
- AutoFarm: Ziele, deren Spionagebericht nie ankam, blieben für immer übersprungen; sie werden nach 60 Minuten wieder freigegeben.
- Lebensform-Forschungsschub: Die Forschungsphase friert die anderen Gebäude nicht mehr ein; nur das Forschungsgebäude wartet.
- Die Versionsnummer wird jetzt auch bei normalen Builds gesetzt (ohne Release-Tag war sie immer 4.0.0.0).
- AutoFarm: Mit `StopAfterFullScan` stoppt der Worker nicht mehr, solange Ziele noch auf Sonde, Bericht oder Angriff warten.
- AutoFarm: Nach langer Pause werden gespeicherte Ziele mit veraltetem oder fehlendem Bericht zurück auf Sondieren gesetzt, gespeicherte Sonden-Ziele ohne Bericht und ohne unterwegs befindliche Sonde sofort freigegeben und ein alter Scan-Cursor verworfen (Log: „Startup: ...“).
- AutoFarm: `ProcessAllReports` (Standard `true`) lässt sich wieder abschalten; die Logzeilen zu Berichten von Zielen, die AutoFarm nicht selbst ausspioniert hat, sind verständlicher.
- Expeditionen: Der Deuterium-Alarm (Telegram) kommt erst, wenn der Expeditionsmond nicht mehr für 2 Expeditions-Flotten reicht; unter dem Ziel, aber darüber, laufen die Expeditionen einfach weiter und der Repatriate-Zyklus füllt auf.
- AutoFarm: zwei Zusammenfassungszeilen pro Lauf im Log („AutoFarm start summary“ mit Zielstatus, Scan-Cursor, Cache und Grenzen; „AutoFarm run summary“ mit gescannten/übersprungenen Systemen, Überspringgründen, gesendeten Sonden und dem Grund fürs Laufende), um zu finden, warum ein Lauf keine Sonde schickt.

## Aktualisieren
Wie gewohnt aktualisieren. Neue Schalter sind standardmäßig aus; bestehende Einstellungen behalten ihr Verhalten.
