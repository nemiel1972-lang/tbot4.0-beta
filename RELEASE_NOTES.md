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

## Aktualisieren
Wie gewohnt aktualisieren. Neue Schalter sind standardmäßig aus; bestehende Einstellungen behalten ihr Verhalten.
