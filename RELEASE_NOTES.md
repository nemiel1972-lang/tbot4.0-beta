Changes since beta15. Everything marked *(off by default)* has to be switched on in the config.

## New
- **Build planning** menu entry: Autobuilds (Build order) and Mine plan (Build calculation) are tabs under one entry.
- **Expedition moon supply** *(off by default)*: `AutoRepatriate.ExpoSupply` sends missing ships and deuterium from the base to the expedition moons in the next repatriate cycle. `AutoFleetJumpGate.ExpoOverride` *(off by default)* keeps only the planned expeditions plus `Expeditions.BackupExpeditions` full fleets on the expedition moons.
- **Config page**: the Expeditions page also shows the expedition moon options (jump gate, supply, slots research) in one place.
- **ConfigDoctor**: suggestions have a "Known" checkbox that hides a suggestion for good ("Show again" resets it).
- **Rewards page**: switch for the Telegram messages.

## Fixed
- AutoFarm: the number of cargo ships uses the Lifeform cargo bonus of the sending planet, and cargo capacity is checked again right before sending.
- AutoFarm: a target was treated as defended when a ship or defence entry with 0 was in the report.
- Defender: `WeakAttackRatio` of 0 no longer causes a division by zero.
- Captcha solver reads the picture width instead of a fixed size; two wrong entries in its table.
- The Expeditions page did not load in German; missing German translations added.

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

## Behoben
- AutoFarm: Die Zahl der Transporter nutzt den Lebensform-Laderaumbonus des sendenden Planeten, und der Laderaum wird direkt vor dem Senden nochmals geprüft.
- AutoFarm: Ein Ziel galt als verteidigt, wenn im Bericht ein Schiff oder eine Verteidigung mit 0 stand.
- Defender: `WeakAttackRatio` von 0 verursacht keine Division durch 0 mehr.
- Captcha-Löser liest die Bildbreite statt einer festen Größe; zwei falsche Einträge in seiner Tabelle.
- Die Seite Expeditionen lud auf Deutsch nicht; fehlende deutsche Übersetzungen ergänzt.

## Aktualisieren
Wie gewohnt aktualisieren. Neue Schalter sind standardmäßig aus; bestehende Einstellungen behalten ihr Verhalten.
