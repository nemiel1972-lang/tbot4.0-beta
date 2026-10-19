window.CFG_HELP = window.CFG_HELP || {};
window.CFG_HELP.de = {
 "AutoFarm": {
  "SystemRange": {
   "t": "Nur Systeme innerhalb dieser Entfernung von deinen Start-Himmelskörpern scannen, statt des festen Scan-Bereichs. 0 = Scan-Bereich verwenden.",
   "e": "5 = bis zu 5 Systeme in jede Richtung um jeden Start-Himmelskörper."
  },
  "ExcludeMoons": {
   "t": "Monde werden weder ausspioniert noch angegriffen, nur Planeten."
  },
  "DistanceBasedScanOrder": {
   "t": "Die nächsten Systeme zuerst scannen. Nahe Ziele bedeuten kürzere Flüge und weniger Treibstoff."
  },
  "ScanBlockSize": {
   "t": "Nach so vielen Systemen den Scan unterbrechen, mit dem Gefundenen angreifen und dann weitermachen. 0 = aus."
  },
  "TargetsProbedBeforeAttack": {
   "t": "Sobald so viele Ziele ausspioniert sind, mit dem Angriff beginnen, statt erst den Scan zu beenden. 0 = aus."
  },
  "SequentialMode": {
   "t": "Ein Ziel nach dem anderen bearbeiten (Sonde, auf den Bericht warten, angreifen), statt zuerst zu scannen. Langsamer, braucht aber sehr wenige Flottenslots."
  },
  "SequentialModeProbesPerCycle": {
   "t": "Nur im sequenziellen Modus: wie viele Ziele pro Durchlauf ausspioniert werden."
  },
  "StopAfterFullScan": {
   "t": "AutoFarm hört von selbst auf, nachdem es den ganzen Bereich einmal gescannt hat."
  },
  "MinimumResources": {
   "t": "Ein unverteidigtes Ziel wird nur angegriffen, wenn mindestens so viel Beute zu holen ist (laut Spionagebericht).",
   "e": "2.000.000 = Ziele mit weniger Beute werden ignoriert."
  },
  "MinimumResourcesDefended": {
   "t": "Dasselbe Minimum für Ziele mit Schiffen oder Verteidigung. Setze es höher als den Wert darüber, weil diese Kämpfe Schiffe kosten können."
  },
  "MinimumPlayerRank": {
   "t": "Spieler mit einem besseren Rang (kleinere Zahl) als diesem werden übersprungen.",
   "e": "3000 = ein Spieler auf Rang 1500 wird übersprungen, einer auf Rang 5000 wird gefarmt."
  },
  "MaxFlightTime": {
   "t": "Längste erlaubte einfache Flugzeit in Sekunden. Weiter entfernte Ziele werden übersprungen. 0 = keine Grenze.",
   "e": "7200 = 2 Stunden."
  },
  "MinLootFuelRatio": {
   "t": "Die Beute muss mindestens so viel wert sein wie der Treibstoff, multipliziert mit diesem Faktor. Der Bot wählt die schnellste Geschwindigkeit, die das noch erfüllt.",
   "e": "1,5 = bei 100.000 Deuterium Treibstoff müssen mindestens 150.000 Beute da sein."
  },
  "PreferedResource": {
   "t": "Ziele mit dem meisten dieses Rohstoffs kommen zuerst. Leer = keine Vorliebe."
  },
  "AttackDefendedTargets": {
   "t": "Auch Ziele mit Schiffen oder Verteidigung angreifen. Der Bot simuliert den Kampf und schickt eine Eskorte aus Kriegsschiffen mit den Frachtern. Braucht die Eskorte-Einstellungen weiter unten.",
   "e": "Kostet mehr Deuterium und kann Schiffe verlieren."
  },
  "MaxDefensePoints": {
   "t": "Ziele, deren Verteidigung mehr wert ist als dieser Wert, werden übersprungen."
  },
  "MinLootPerDefensePoint": {
   "t": "Verteidigte Ziele werden übersprungen, wenn ihre Beute im Verhältnis zur Verteidigung zu klein ist. Höher = wählerischer."
  },
  "KeepReportFor": {
   "t": "Minuten, die ein Spionagebericht gültig bleibt, bevor das Ziel erneut ausspioniert werden muss."
  },
  "MaxReportAgeMinutesForAttack": {
   "t": "Direkt vor einem Angriff wird ein älterer Bericht verworfen und das Ziel erneut ausspioniert, weil sich die Verteidigung geändert haben kann. 0 = aus."
  },
  "MaxAttackDelaySec": {
   "t": "Längste Zeit in Sekunden, die ein Angriff zurückgehalten werden darf, damit die Recycler direkt dahinter ankommen."
  },
  "DebrisCreditPercent": {
   "t": "Wie viel des erwarteten Trümmerfelds als Gewinn zählt, wenn der Bot entscheidet, ob sich ein Angriff lohnt (nach dem Treibstoff der Recycler).",
   "e": "80 = 80 % der Trümmer werden angerechnet."
  },
  "MinDebrisToHarvest": {
   "t": "Recycler werden nur geschickt, wenn das Trümmerfeld nach ihren Treibstoffkosten größer als dieser Wert ist. 0 = immer, wenn Trümmer da sind."
  },
  "MinSpeedPercentForProfitRetry": {
   "t": "Lohnt sich ein Angriff bei der gewählten Geschwindigkeit nicht, probiert der Bot langsamere Geschwindigkeiten bis zu diesem Wert, weil langsamere Flüge weniger Treibstoff verbrennen."
  },
  "GlobalSpacing": {
   "t": "Nur für mehrere Bot-Instanzen: verteilt sie auf verschiedene Startpunkte im Scan-Bereich. Bei einer Instanz aus lassen."
  },
  "ReportQueryLimitForNonActionable": {
   "t": "Wie viele Berichte von nicht angreifbaren Zielen pro Durchlauf gelesen werden. Niedriger = schnellere Durchläufe."
  },
  "RepeatedLogIntervalMinutes": {
   "t": "Dieselbe Log-Meldung erscheint höchstens so oft. 0 = jedes Mal."
  },
  "NumProbes": {
   "t": "Spionagesonden, die pro Ziel geschickt werden."
  },
  "BuildProbes": {
   "t": "Sonden automatisch bauen, wenn zu wenige zu Hause sind."
  },
  "FillSlots": {
   "t": "Alle erlaubten Flottenslots mit Sonden und Angriffen belegt halten."
  },
  "FillSlotsMaxProbeWaitSec": {
   "t": "Wie lange (in Sekunden) auf Sondenberichte gewartet wird, bevor freie Slots anders genutzt werden."
  },
  "MaxSlots": {
   "t": "Die meisten Flottenslots, die AutoFarm gleichzeitig nutzen darf."
  },
  "SlotsToLeaveFree": {
   "t": "Flottenslots, die AutoFarm nie nutzt, damit Expeditionen und andere Worker noch Platz haben."
  },
  "FleetSpeed": {
   "t": "Feste Geschwindigkeit für Angriffe, wenn keine bessere berechnet wird. 100 % ist am schnellsten und verbrennt am meisten Deuterium."
  },
  "CargoType": {
   "t": "Der Frachtertyp, der die Beute trägt."
  },
  "MinCargosToSend": {
   "t": "Es wird kein Angriff mit weniger Frachtern als diesem Wert gestartet."
  },
  "MinCargosToKeep": {
   "t": "Frachter, die immer zu Hause bleiben."
  },
  "CargoSurplusPercentage": {
   "t": "Zusätzlicher Laderaum über die berechnete Menge hinaus, als Sicherheitsreserve."
  },
  "CargoReservePercent": {
   "t": "Anteil der Frachter, der als Reserve zurückgehalten wird."
  },
  "RecyclerReservePercent": {
   "t": "Anteil der Recycler, der als Reserve zurückgehalten wird."
  },
  "BuildCargos": {
   "t": "Frachter bauen, wenn nicht genug da sind."
  },
  "UseSpeedFleet": {
   "t": "Schnelle Schiffe (Pathfinder) als Frachter nutzen, damit Angriffe früher ankommen. Sie verbrennen mehr Deuterium. Die Deuterium-Sperre schaltet das von selbst ab, wenn das Deuterium knapp ist."
  },
  "SpeedFleetCargoTypes": {
   "t": "Alte Einstellung, die diese Version des Bots nicht mehr liest. Sie hat keine Wirkung und kann aus der Config entfernt werden. Die Schnellflotte besteht immer aus Pathfindern."
  },
  "ReduceDeuterium": {
   "t": "Deuterium sparen: Angriffsflotte und Recycler fliegen so langsam, wie es sinnvoll ist, nie unter der unten eingestellten niedrigsten Geschwindigkeit. Flüge dauern länger und Slots bleiben länger belegt.",
   "e": "Nützlich, wenn Deuterium knapp ist."
  },
  "ReduceDeuteriumMinSpeedPercent": {
   "t": "Die langsamste Geschwindigkeit, die „Deuterium sparen“ nutzen darf. Nie unter 30 %."
  },
  "DeuteriumGuard": {
   "t": "Hat ein Start-Himmelskörper weniger Deuterium als das Minimum darunter, werden Angriffe auf verteidigte Ziele von dort pausiert. Hat keiner genug, wird auch die Schnellflotte abgeschaltet. Die AutoFarm-Seite zeigt dann „Deuterium too low for bigger fleets“."
  },
  "DeuteriumGuardMin": {
   "t": "Die Deuterium-Schwelle der Sperre.",
   "e": "10.000.000 = die Sperre greift unter 10 Millionen Deuterium."
  },
  "DeuteriumGuardOverride": {
   "t": "Die Sperre von Hand überbrücken und auch bei wenig Deuterium weiterfarmen. Die Seite zeigt weiterhin einen Hinweis."
  },
  "TargetCombatTechBonus": {
   "t": "Zusätzliche Waffentechnik-Stufen, die für das Ziel in Kampfsimulationen angenommen werden, als Sicherheitsreserve. Höher = vorsichtiger."
  },
  "EscortShipTypes": {
   "t": "Kriegsschiffe, die als Eskorte mitfliegen dürfen, in der Reihenfolge der Vorliebe. Mit Komma getrennt."
  },
  "EscortFallbackShipTypes": {
   "t": "Wird nur genutzt, wenn die Eskortschiffe darüber nicht ausreichen."
  },
  "EscortShipsStep": {
   "t": "Die Eskorte wird in Schritten von so vielen Schiffen gesucht. Kleiner = genauer, aber langsamer."
  },
  "MaxEscortShips": {
   "t": "Größte Zahl Schiffe eines Typs in einer Eskorte, wenn unten für diesen Typ keine Obergrenze gesetzt ist."
  },
  "MaxEscortFleetPercent": {
   "t": "Höchstens so viel Prozent der Schiffe eines Typs, die zu Hause stehen, dürfen als Eskorte mitfliegen.",
   "e": "100 = keine Grenze nach Anteil; 20 = höchstens ein Fünftel davon."
  },
  "EscortReservePercent": {
   "t": "Sicherheitspuffer: Auf die berechnete Eskorte kommt so viel zusätzlich, wo Schiffe verfügbar sind."
  },
  "MaxEscortLossPercent": {
   "t": "Höchster simulierter Verlust, als Anteil am Wert der ganzen Angriffsflotte. Eskorten, die mehr verlieren würden, werden abgelehnt. 0 = Grenze aus (jeder Verlust wird akzeptiert).",
   "e": "10 = eine Eskorte ablehnen, die mehr als 10 % des Flottenwerts verlieren würde."
  },
  "EscortLossWeight": {
   "t": "Wie stark Schiffsverluste im Vergleich zum Treibstoff zählen, wenn der Bot die günstigste Eskorte wählt. Höher = er bevorzugt eine größere Eskorte, die weniger verliert.",
   "e": "3 = ein verlorenes Schiff zählt dreimal so viel wie derselbe Wert in Treibstoff."
  },
  "MinWinRate": {
   "t": "Der simulierte Kampf muss mindestens so oft gewonnen werden.",
   "e": "0,9 = in 90 % der simulierten Kämpfe."
  },
  "SimulationRuns": {
   "t": "Wie viele Kämpfe pro Prüfung simuliert werden. Mehr = zuverlässiger, aber langsamer."
  },
  "EscortMaxIterations": {
   "t": "Suchaufwand für die beste Eskortengröße: höchste Zahl an Schritten. Höher = besseres Ergebnis, mehr Rechenzeit."
  },
  "EscortBisectionIterations": {
   "t": "Suchaufwand: wie fein die Eskortengröße eingegrenzt wird. Höher = genauer, langsamer."
  },
  "EscortFineTuneMaxSims": {
   "t": "Suchaufwand: Simulationen für die letzte Feinabstimmung der Eskorte."
  },
  "FleetToDebrisPercent": {
   "t": "Erwarteter Anteil der Flottenverluste, der als Trümmerfeld übrig bleibt. Wird für die Planung der Recycler genutzt."
  },
  "MaxCargoLossPercent": {
   "t": "Höchster akzeptierter Verlust an Frachtern in der Simulation, als Anteil an ihrem Wert."
  },
  "AutoHarvestDebrisAfterAttack": {
   "t": "Nach dem Angriff Recycler zum Trümmerfeld schicken."
  },
  "CheckIntervalMin": {
   "t": "Minuten zwischen zwei AutoFarm-Durchläufen. Der Bot wählt eine zufällige Zeit zwischen Minimum und Maximum."
  },
  "CheckIntervalMax": {
   "t": "Minuten zwischen zwei AutoFarm-Durchläufen. Der Bot wählt eine zufällige Zeit zwischen Minimum und Maximum."
  },
  "ScanRange": {
   "t": "Galaxie- und Systembereiche, die nach Zielen gescannt werden."
  },
  "Exclude": {
   "t": "Koordinaten, die nie ausspioniert oder angegriffen werden."
  },
  "Blacklist.ResetAfterHours": {
   "t": "Stunden, bis ein Ziel auf der Sperrliste wieder erlaubt ist."
  },
  "Blacklist.UnprofitableResetAfterHours": {
   "t": "Stunden, bis ein Ziel, das sich nicht gelohnt hat, erneut geprüft wird."
  },
  "Blacklist.PermanentResetAfterHours": {
   "t": "Stunden, bis ein dauerhaft gesperrtes Ziel freigegeben wird. 0 = nie."
  },
  "ServerHost": {
   "t": "Hostname deines Universums, um die Allianzen anderer Spieler zu lesen."
  },
  "AdditionalAllianceIds": {
   "t": "Allianz-IDs, die nie angegriffen werden, zum Beispiel NAP-Partner."
  },
  "EscortCap": {
   "t": "Absolute Obergrenze für diesen Eskortschiff-Typ. Leer = nur die allgemeine Grenze „Max escort ships“ zählt."
  }
 },
 "*": {
  "Active": {
   "t": "Hauptschalter dieser Funktion. Aus = der Bot macht hier nichts."
  },
  "CheckIntervalMin": {
   "t": "Kürzeste Pause in Minuten zwischen zwei Durchläufen dieser Funktion."
  },
  "CheckIntervalMax": {
   "t": "Längste Pause in Minuten zwischen zwei Durchläufen. Der Bot wählt eine zufällige Zeit zwischen Minimum und Maximum, damit er nicht wie eine Uhr arbeitet."
  },
  "Galaxy": {
   "t": "Galaxienummer der Koordinaten."
  },
  "System": {
   "t": "Systemnummer der Koordinaten."
  },
  "Position": {
   "t": "Position (1-15) im System."
  },
  "Type": {
   "t": "Planet oder Mond an diesen Koordinaten."
  },
  "Exclude": {
   "t": "Himmelskörper (Planeten oder Monde), die diese Funktion nie anfasst. Eine Zeile pro Himmelskörper."
  },
  "Transports.Active": {
   "t": "Dieser Funktion erlauben, fehlende Rohstoffe per Transport von deinen anderen Himmelskörpern zu holen."
  },
  "RandomOrder": {
   "t": "Die Himmelskörper in zufälliger Reihenfolge bearbeiten, statt immer denselben zuerst."
  },
  "ExcludeMoons": {
   "t": "Monde überspringen, nur Planeten werden bearbeitet."
  },
  "MaxSlots": {
   "t": "Größte Zahl an Flottenslots, die diese Funktion gleichzeitig nutzen darf."
  },
  "SlotsToLeaveFree": {
   "t": "Flottenslots, die diese Funktion nie nutzt, damit sie für dich frei bleiben."
  },
  "CustomTitle": {
   "t": "Alte Einstellung, die diese Version des Bots nicht mehr liest. Sie hat keine Wirkung und kann aus der Config entfernt werden."
  },
  "SecondaryShip": {
   "t": "Alte Einstellung, die diese Version des Bots nicht mehr liest. Sie hat keine Wirkung und kann aus der Config entfernt werden."
  },
  "MinSecondaryToSend": {
   "t": "Alte Einstellung, die diese Version des Bots nicht mehr liest. Sie hat keine Wirkung und kann aus der Config entfernt werden."
  },
  "SecondaryToPrimaryRatio": {
   "t": "Alte Einstellung, die diese Version des Bots nicht mehr liest. Sie hat keine Wirkung und kann aus der Config entfernt werden."
  },
  "MaxWaitTime": {
   "t": "Alte Einstellung, die diese Version des Bots nicht mehr liest. Sie hat keine Wirkung und kann aus der Config entfernt werden."
  },
  "MaxFailures": {
   "t": "Alte Einstellung, die diese Version des Bots nicht mehr liest. Sie hat keine Wirkung und kann aus der Config entfernt werden."
  },
  "RandomizeDestination": {
   "t": "Alte Einstellung, die diese Version des Bots nicht mehr liest. Sie hat keine Wirkung und kann aus der Config entfernt werden."
  },
  "ResourceReserve.*": {
   "t": "Alte Einstellung, die diese Version des Bots nicht mehr liest. Sie hat keine Wirkung und kann aus der Config entfernt werden."
  },
  "ResourceReserve": {
   "t": "Alte Einstellung, die diese Version des Bots nicht mehr liest. Sie hat keine Wirkung und kann aus der Config entfernt werden."
  }
 },
 "Credentials": {
  "Universe": {
   "t": "Name deines Universums genau so, wie er in der Gameforge-Lobby steht.",
   "e": "Fornax"
  },
  "Email": {
   "t": "E-Mail-Adresse deines Gameforge-Kontos."
  },
  "Password": {
   "t": "Passwort deines Gameforge-Kontos. Es steht in secrets/credentials, nicht in der normalen Config."
  },
  "Language": {
   "t": "Sprachcode des Spielservers.",
   "e": "en oder de"
  },
  "LobbyPioneers": {
   "t": "Nur einschalten, wenn dein Konto über die Pioneers-Lobby einloggt."
  },
  "BasicAuth.*": {
   "t": "Nur nötig, wenn ein Proxy oder Server einen HTTP-Login verlangt. Normalerweise leer."
  },
  "DeviceConf.*": {
   "t": "Browser-Fingerabdruck, den der Bot beim Login dem Spiel zeigt (Gerätename, Browser, Bildschirmgröße und so weiter). Über die Zeit gleich lassen und nicht oft ändern."
  }
 },
 "General": {
  "Host": {
   "t": "Adresse des lokalen ogamed-Connectors, mit dem der Bot spricht. Normalerweise 127.0.0.1."
  },
  "Port": {
   "t": "Port des lokalen ogamed-Connectors. Nur ändern, wenn dieser Port schon belegt ist."
  },
  "Proxy.Enabled": {
   "t": "Den Datenverkehr des Bots über einen Proxy-Server schicken."
  },
  "Proxy.Address": {
   "t": "Adresse des Proxys samt Port.",
   "e": "http://proxy.example.com:8080"
  },
  "Proxy.Type": {
   "t": "Art des Proxys, zum Beispiel socks5 oder http."
  },
  "Proxy.Username": {
   "t": "Login des Proxys, falls er eines braucht."
  },
  "Proxy.Password": {
   "t": "Passwort des Proxys. Steht in secrets/proxy."
  },
  "Proxy.LoginOnly": {
   "t": "An = der Proxy wird nur zum Einloggen genutzt, Spielanfragen gehen direkt. Aus = alles läuft über den Proxy."
  },
  "CaptchaAPIKey": {
   "t": "Schlüssel eines Captcha-Lösedienstes. Optional, nur nötig, wenn das Spiel Captchas verlangt."
  },
  "LoginPause": {
   "t": "Schützt die IP: Nach wiederholten Login-Fehlern (Captcha usw.) macht der Bot eine Pause, statt es immer wieder zu versuchen. Die Pause verdoppelt sich mit jedem weiteren Fehler. Während der Pause bleibt die manuelle Captcha-Seite (/bot/captcha) nutzbar; löst du dort das Captcha, macht der Bot sofort weiter. Der Zustand liegt in einer Datei, ein Neustart überspringt die Pause also nicht."
  },
  "LoginPause.Active": {
   "t": "Die Login-Pause an- oder ausschalten."
  },
  "LoginPause.FailuresBeforePause": {
   "t": "Fehlgeschlagene Logins in Folge bis zur ersten Pause."
  },
  "LoginPause.PauseMinutes": {
   "t": "Länge der ersten Pause in Minuten. Verdoppelt sich mit jedem weiteren Fehler."
  },
  "LoginPause.MaxPauseMinutes": {
   "t": "Längste Pause in Minuten."
  },
  "SlotsToLeaveFree": {
   "t": "Flottenslots, die keine Funktion nutzen darf. Sie bleiben für deine eigenen manuellen Flüge frei.",
   "e": "2 = der Bot nimmt nie die letzten 2 Slots."
  },
  "SlotPriorityLevel.*": {
   "t": "Wer zuerst einen freien Slot bekommt, wenn mehrere Funktionen einen wollen. Eine kleinere Zahl wird zuerst bedient."
  },
  "ResourceReserve.*": {
   "t": "Alte Einstellung, die diese Version des Bots nicht mehr liest. Sie hat keine Wirkung und kann aus der Config entfernt werden."
  }
 },
 "DiscordWebhook": {
  "Url": {
   "t": "Webhook-Adresse deines Discord-Kanals. Behandle sie wie ein Passwort."
  },
  "Username": {
   "t": "Name, der als Absender der Discord-Nachrichten angezeigt wird."
  },
  "Active": {
   "t": "Die Telegram-Nachrichten nach Discord kopieren. Das sendet nur; Befehle laufen weiter über Telegram."
  }
 },
 "TelegramMessenger": {
  "Active": {
   "t": "Telegram-Nachrichten und -Befehle an- oder ausschalten."
  },
  "API": {
   "t": "Token deines Telegram-Bots. Du bekommst ihn bei @BotFather. Steht in secrets/telegram."
  },
  "ChatId": {
   "t": "ID des Chats, mit dem der Bot spricht. Nur dieser Chat darf Befehle senden."
  },
  "Logging": {
   "t": "Gesendete und empfangene Telegram-Nachrichten ins Bot-Log schreiben."
  },
  "TelegramAutoPing.Active": {
   "t": "Regelmäßig eine „lebt noch“-Nachricht senden."
  },
  "TelegramAutoPing.EveryHours": {
   "t": "Stunden zwischen zwei Lebenszeichen.",
   "e": "6"
  }
 },
 "Messenger": {},
 "SleepMode": {
  "GoToSleep": {
   "t": "Tageszeit, zu der der Bot schlafen geht (Serverzeit).",
   "e": "23:00"
  },
  "WakeUp": {
   "t": "Tageszeit, zu der der Bot wieder aufwacht (Serverzeit).",
   "e": "07:00"
  },
  "Mode": {
   "t": "Wähle, wie der Bot schläft. SleepMode stoppt alles. GhostSleep versteckt die Flotte, lässt aber Expeditionen und Defender weiterarbeiten. Nur einer kann aktiv sein."
  },
  "Active": {
   "t": "Festes tägliches Schlaffenster. Solange der Bot schläft, werden keine Flotten geschickt. Nur SleepMode oder GhostSleep darf aktiv sein."
  },
  "PreventIfThereAreFleets": {
   "t": "Das Schlafengehen verzögern, wenn Flotten im Schlaffenster noch zurückkämen."
  },
  "TelegramMessenger.Active": {
   "t": "Eine Telegram-Nachricht senden, wenn der Bot schlafen geht und wenn er aufwacht."
  },
  "AutoFleetSave.Active": {
   "t": "Beim Schlafengehen alle Schiffe wegschicken, damit sie nicht angegriffen werden können, während du nicht zusiehst."
  },
  "AutoFleetSave.OnlyMoons": {
   "t": "Nur die Flotten sichern, die auf Monden stehen."
  },
  "AutoFleetSave.DeutToLeave": {
   "t": "Deuterium, das auf dem Himmelskörper bleibt. Alles darüber fliegt als Treibstoff mit der Flotte mit.",
   "e": "1000000"
  },
  "AutoFleetSave.Recall": {
   "t": "Die Flotte vom Fleetsave zurückrufen, damit sie beim Aufwachen wieder zu Hause ist."
  },
  "AutoFleetSave.DefaultMission": {
   "t": "Mission für den Fleetsave-Flug, normalerweise Stationieren (Deploy)."
  },
  "AutoFleetSave.DeployTarget.*": {
   "t": "Koordinaten, zu denen der Fleetsave fliegt."
  }
 },
 "GhostSleep": {
  "GoToSleep": {
   "t": "Tageszeit, zu der der Bot schlafen geht (Serverzeit).",
   "e": "23:00"
  },
  "WakeUp": {
   "t": "Tageszeit, zu der der Bot wieder aufwacht (Serverzeit).",
   "e": "07:00"
  },
  "Active": {
   "t": "Schlafvariante: Alle Schiffe werden mit einem Geisterflug gesichert, aber Expeditionen und Defender arbeiten die Nacht über weiter."
  },
  "PreSleepExpeditionMinutes": {
   "t": "Alte zeitbasierte Vorphase. Minuten vor dem Schlafen, in denen alle Expeditionsslots aufgefüllt werden. Wird ignoriert, solange „Let expeditions finish before sleep“ an ist. 0 = aus."
  },
  "AnchorMode": {
   "t": "Wie der Defender entscheidet, wann er im Schlaf auf Angriffe prüft. LastFleet = eine Prüfung kurz bevor die letzte Expedition landet. Random = eine zufällige zurückkehrende Expedition, 60 Sekunden bevor sie landet."
  },
  "PriorityFleetsaveCelestials": {
   "t": "Himmelskörper (Mond und Planet), deren Flotten zur Schlafenszeit zuerst gesichert werden."
  },
  "ExpeditionDrain.Active": {
   "t": "Kurz vor der Schlafenszeit startet keine neue Expedition mehr, wenn sie nicht rechtzeitig zurück sein könnte. Zur Schlafenszeit wartet der Bot auf die letzten, schickt eine Welle Expeditionen und schläft dann."
  },
  "ExpeditionDrain.BufferMinutes": {
   "t": "Eine Expedition darf noch starten, wenn sie höchstens so viele Minuten nach der Schlafenszeit zurück ist.",
   "e": "10"
  },
  "ExpeditionDrain.StragglerWaitMinutes": {
   "t": "Zur Schlafenszeit wartet der Bot höchstens so lange auf Expeditionen, die noch draußen sind. Spätere kommen nachts heim.",
   "e": "20"
  },
  "ExpeditionDrain.DefaultDurationMinutes": {
   "t": "Expeditionsdauer, die der Bot annimmt, bis er mindestens 3 echte gemessen hat.",
   "e": "90"
  }
 },
 "DebrisSave": {
  "Celestials": {
   "t": "Himmelskörper, die der Telegram-Befehl /debrissave nutzt. Jeder schickt seine Flotte zum nächsten Trümmerfeld und ruft sie zurück, sodass sie nach der gewählten Zeit wieder da ist."
  }
 },
 "Defender": {
  "Active": {
   "t": "Achtet auf eingehende Angriffe und reagiert."
  },
  "IgnoreProbes": {
   "t": "Nicht auf Spionagesonden reagieren."
  },
  "DefendFromMissiles": {
   "t": "Auch auf Raketenangriffe reagieren."
  },
  "IgnoreWeakAttack": {
   "t": "Angreifer ignorieren, die viel schwächer sind als du."
  },
  "WeakAttackRatio": {
   "t": "Ein Angreifer ist schwach, wenn seine Flottenpunkte kleiner sind als deine geteilt durch diese Zahl.",
   "e": "10 = Angreifer mit weniger als einem Zehntel deiner Flottenpunkte ignorieren."
  },
  "IgnoreAttackIfIHave.Active": {
   "t": "Einen Angriff auf einen Himmelskörper ignorieren, auf dem nichts Rettenswertes liegt."
  },
  "IgnoreAttackIfIHave.MinResourcesToSave": {
   "t": "Der Angriff wird ignoriert, wenn der Himmelskörper weniger Rohstoffe hält als dieser Wert ..."
  },
  "IgnoreAttackIfIHave.MinFleetToSave": {
   "t": "... und weniger Flottenpunkte als dieser Wert."
  },
  "RandomActivity": {
   "t": "An = jede Angriffsprüfung öffnet die Verteidigungsseite eines zufälligen Himmelskörpers. Aus = immer der Heimat-Himmelskörper. Lässt die Aktivität des Bots nur normal aussehen."
  },
  "Home.*": {
   "t": "Dein Heimat-Himmelskörper. Wird für den Aktivitätsaufruf genutzt, wenn die zufällige Aktivität aus ist, und als Standard-Startpunkt von AutoHarvest."
  },
  "Autofleet.Active": {
   "t": "Den angegriffenen Himmelskörper automatisch per Fleetsave sichern."
  },
  "Autofleet.SafetyMarginSeconds": {
   "t": "Die Flotte muss so viele Sekunden vor dem Einschlag weg sein.",
   "e": "120"
  },
  "Autofleet.TelegramMessenger.Active": {
   "t": "Bei jedem Fleetsave eine Telegram-Nachricht senden."
  },
  "Discord.Active": {
   "t": "Angriffsmeldungen an einen Discord-Webhook senden."
  },
  "Discord.WebhookUrl": {
   "t": "Discord-Webhook-Adresse. Behandle sie wie ein Passwort."
  },
  "WhiteList": {
   "t": "Spieler-IDs, die nie als Angreifer behandelt werden, zum Beispiel Freunde oder Allianzmitglieder."
  },
  "SpyAttacker.Active": {
   "t": "Den Angreifer mit Sonden ausspionieren, wenn ein Angriff eingeht."
  },
  "SpyAttacker.Probes": {
   "t": "Zahl der Sonden, die den Angreifer ausspionieren."
  },
  "MessageAttacker.Active": {
   "t": "Dem Angreifer eine Nachricht schicken."
  },
  "MessageAttacker.Messages": {
   "t": "Mögliche Nachrichten, eine pro Zeile. Der Bot sendet eine davon."
  },
  "TelegramMessenger.Active": {
   "t": "Angriffsmeldungen per Telegram senden."
  },
  "Alarm.Active": {
   "t": "Die Meldung mit hoher Alarmstufe markieren, damit sie auffällt."
  }
 },
 "Brain": {
  "AutoMine.ColonyOverrides.*": {
   "t": "Eigene Stufenlimits für genau diesen einen Planeten. Nur Felder, die du setzt, ersetzen die Colony-Werte."
  },
  "Active": {
   "t": "Hauptschalter der Gebäude-, Forschungs- und Transportautomatik. Jeder Teil unten hat zusätzlich einen eigenen Schalter."
  },
  "Transports.Active": {
   "t": "Brain-Funktionen erlauben, Rohstoffe zwischen deinen Planeten zu bewegen."
  },
  "Transports.Origin.*": {
   "t": "Standard-Himmelskörper, von dem Rohstoffe geholt werden."
  },
  "Transports.CargoType": {
   "t": "Bevorzugtes Transportschiff. Pathfinder werden nie für Transporte benutzt."
  },
  "Transports.CargoFallback": {
   "t": "Transportschiff, das genutzt wird, wenn der bevorzugte Typ nicht verfügbar ist."
  },
  "Transports.KeepOnOrigin.*": {
   "t": "Schiffe, die am Ursprung bleiben und nicht für Transporte benutzt werden."
  },
  "Transports.MixFleet": {
   "t": "Als letzten Schritt kleine und große Transporter mischen. Die Flotte fliegt mit der Geschwindigkeit des langsamsten Schiffs."
  },
  "Transports.UseTransportChain": {
   "t": "Die Transporte als Kette planen, einen nach dem anderen."
  },
  "Transports.CompactSlotPlan": {
   "t": "Wenn Slots knapp sind, neu mit weniger, größeren Sendungen planen."
  },
  "Transports.DeutToLeaveOnMoons": {
   "t": "Deuterium, das auf Monden bleibt. Ein Transport verlässt einen Mond nur, wenn Fracht, diese Reserve und der Flugtreibstoff zusammen passen, die Reserve wird also nie als Treibstoff verbraucht."
  },
  "Transports.DeutToLeaveOnPlanets": {
   "t": "Deuterium, das auf einem Planeten bleibt, wenn er Rohstoffe wegschickt. Der Transport startet nur, wenn Fracht, diese Reserve und der Flugtreibstoff zusammen passen. 0 = nichts zurückhalten."
  },
  "Transports.RoundResources": {
   "t": "Die gesendete Menge runden, damit wegen eines winzigen Unterschieds keine Flotte fliegt."
  },
  "Transports.SendToTheMoonIfPossible": {
   "t": "Zum Mond liefern statt zum Planeten, wenn es einen Mond gibt."
  },
  "Transports.MaxSlots": {
   "t": "Flottenslots, die Brain-Transporte gleichzeitig nutzen dürfen."
  },
  "Transports.CheckMoonOrPlanetFirst": {
   "t": "Was bei der Rohstoffsuche zuerst geprüft wird, Mond oder Planet."
  },
  "Transports.DoMultipleTransportIsNotEnoughShipButSamePosition": {
   "t": "Reicht ein Schiffstyp nicht, mehrere Transporte von derselben Position senden."
  },
  "Transports.MultipleOrigins.Active": {
   "t": "Rohstoffe von mehreren Himmelskörpern einsammeln statt nur von einem."
  },
  "Transports.MultipleOrigins.OnlyFromMoons": {
   "t": "Nur von Monden einsammeln."
  },
  "Transports.MultipleOrigins.MinimumResourcesToSend": {
   "t": "Ein Himmelskörper macht nur mit, wenn er mindestens so viel senden kann."
  },
  "Transports.MultipleOrigins.PriorityToProximityOverQuantity": {
   "t": "An = nähere Himmelskörper werden bevorzugt. Aus = der Himmelskörper mit den meisten Rohstoffen wird bevorzugt."
  },
  "Transports.MultipleOrigins.Exclude": {
   "t": "Himmelskörper, die nie als Quelle dienen."
  },
  "AutoMine.Active": {
   "t": "Baut Minen, Lager, Energie und Anlagen auf allen deinen Planeten."
  },
  "AutoMine.HomeWorld.*": {
   "t": "Dein Hauptplanet. Er nutzt die Stufenlimits des obersten Blocks, alle anderen Planeten nutzen den Colony-Block."
  },
  "AutoMine.Max*": {
   "t": "Höchste Stufe, die der Bot auf dem Hauptplaneten baut. 0 = nie bauen."
  },
  "AutoMine.Colony.Max*": {
   "t": "Höchste Stufe, die der Bot auf allen anderen Planeten baut. 0 = nie bauen."
  },
  "AutoMine.ColonyOverrides": {
   "t": "Einzelne Planeten mit eigenen Limits. Zuordnung über exakte Koordinaten; nur Felder, die du setzt, ersetzen die Colony-Werte."
  },
  "AutoMine.RandomOrder": {
   "t": "Die Planeten in zufälliger Reihenfolge bearbeiten."
  },
  "AutoMine.OptimizeForStart": {
   "t": "Die Bauordnung für den Spielstart nutzen (erst Roboterfabrik und Raumschiffwerft)."
  },
  "AutoMine.PrioritizeRobotsAndNanites": {
   "t": "Roboterfabrik und Nanitenfabrik früh bauen."
  },
  "AutoMine.BuildDepositIfFull": {
   "t": "Ein Lager bauen, wenn es voll ist."
  },
  "AutoMine.BuildSolarSatellites": {
   "t": "Solarsatelliten bauen, wenn Energie fehlt."
  },
  "AutoMine.SolarSatelliteBatch": {
   "t": "Solarsatelliten werden in Bündeln dieser Größe gebaut (die fehlende Menge wird aufgerundet) statt einzeln. Standard 20; 0 oder 1 = genau die fehlende Menge."
  },
  "AutoMine.EnergyReservePercent": {
   "t": "Vor einem Minenausbau, der die Energiebilanz ins Minus drücken würde, wird zuerst die Energiequelle (Solarkraftwerk, Fusion oder Satelliten) gebaut, bemessen auf die fehlende Energie plus diesen Prozentsatz als Puffer. Standard 10; 0 = genau die fehlende Menge."
  },
  "AutoMine.BuildCrawlers": {
   "t": "Crawler bauen."
  },
  "AutoMine.DepositHours": {
   "t": "Ein Lager soll so viele Stunden Produktion fassen.",
   "e": "24"
  },
  "AutoMine.MaxDaysOfInvestmentReturn": {
   "t": "Bauten überspringen, die länger als diese Tage brauchen, um sich zu amortisieren. 9999 = kein Limit."
  },
  "AutoMine.DeutToLeaveOnMoons": {
   "t": "Deuterium, das auf Monden bleibt."
  },
  "AutoMine.Exclude": {
   "t": "Planeten und Monde, die AutoMine nie anfasst."
  },
  "AutoMine.FixedCrawlerTarget": {
   "t": "Feste Crawler-Zahl pro Planet. 0 = der Bot rechnet selbst."
  },
  "LifeformAutoMine.Active": {
   "t": "Baut Lebensform-Gebäude nach der Tabelle in lifeform_targets.json."
  },
  "LifeformAutoMine.StartFromCrystalMineLvl": {
   "t": "Ein Planet beginnt mit Lebensform-Gebäuden, sobald seine Kristallmine diese Stufe hat.",
   "e": "20"
  },
  "LifeformAutoMine.TargetSlot": {
   "t": "Zeile (1-18) der Zieltabelle, auf die hingebaut wird. 0 = nur die Ersatzlimits."
  },
  "LifeformAutoMine.MaxBasePopulationBuilding": {
   "t": "Zusätzliche Obergrenze für das erste Bevölkerungsgebäude. Der kleinere Wert aus Tabelle und Limit zählt. Für kleine Konten."
  },
  "LifeformAutoMine.MaxBaseFoodBuilding": {
   "t": "Zusätzliche Obergrenze für das erste Nahrungsgebäude."
  },
  "LifeformAutoMine.MaxBaseTechBuilding": {
   "t": "Zusätzliche Obergrenze für das erste Technologiegebäude."
  },
  "LifeformAutoMine.PreventIfMoreExpensiveThanNextMine": {
   "t": "Kein Lebensform-Gebäude bauen, das mehr kostet als der nächste Minenausbau."
  },
  "LifeformAutoMine.Exclude": {
   "t": "Planeten ohne Lebensform-Gebäude."
  },
  "LifeformAutoResearch.Active": {
   "t": "Erforscht Lebensform-Technologien."
  },
  "LifeformAutoResearch.PriorityTechs": {
   "t": "Lebensform-Techs, die zuerst erforscht werden, in dieser Reihenfolge."
  },
  "LifeformAutoResearch.MaxResearchLevel": {
   "t": "Höchste Stufe, die für jede Lebensform-Tech erforscht wird."
  },
  "LifeformAutoResearch.MaxTechs*": {
   "t": "Eigenes Stufenlimit für diesen Tech-Slot. Erste Ziffer = Rasse, zweite = Slot."
  },
  "LifeformAutoResearch.Exclude": {
   "t": "Planeten, die nie für Lebensform-Forschung genutzt werden."
  },
  "AutoResearch.Active": {
   "t": "Erforscht Technologien automatisch."
  },
  "AutoResearch.Max*": {
   "t": "Höchste Stufe, die erforscht wird. 0 = nie erforschen."
  },
  "AutoResearch.Target.*": {
   "t": "Planet, auf dem die Forschung gestartet wird, meist der mit dem besten Forschungslabor."
  },
  "AutoResearch.OptimizeForStart": {
   "t": "Die Forschungsreihenfolge für den Spielstart nutzen."
  },
  "AutoResearch.EnsureExpoSlots": {
   "t": "Computertechnik und Astrophysik nach Bedarf erforschen, um genug Expeditionsslots zu haben."
  },
  "AutoResearch.ForceResearchWhateverTheLabLevel": {
   "t": "Auch forschen, wenn das Forschungslabor noch niedrig ist."
  },
  "AutoResearch.Prioritize*": {
   "t": "Diese Technologie vor den anderen erforschen."
  },
  "AutoResearch.Exclude": {
   "t": "Planeten, die nie für Forschung genutzt werden."
  },
  "AutoCargo.Active": {
   "t": "Baut Transportschiffe automatisch."
  },
  "AutoCargo.MaxProductionHours": {
   "t": "Obergrenze der Produktionszeit eines Transporter-Auftrags pro Himmelskörper in Stunden. 0 = kein Limit. Was nicht passt, wird in einem späteren Durchlauf gebaut."
  },
  "AutoCargo.SupplyMoonFromPlanet": {
   "t": "Monde bekommen ihre Schiffe vom Planeten an derselben Position."
  },
  "AutoCargo.CargoType": {
   "t": "Schiffstyp, der gebaut wird."
  },
  "AutoCargo.MaxCargosToBuild": {
   "t": "Gesamtzahl an Transportern, die erreicht werden soll."
  },
  "AutoCargo.MaxCargosToKeep": {
   "t": "Wie viele Transporter behalten werden."
  },
  "AutoCargo.LimitToCapacity": {
   "t": "Nur so viele bauen, wie die gelagerten Rohstoffe brauchen."
  },
  "AutoCargo.SkipIfIncomingTransport": {
   "t": "Himmelskörper überspringen, zu dem schon ein Transport unterwegs ist."
  },
  "AutoCargo.Exclude": {
   "t": "Himmelskörper, die keine Transporter bauen."
  },
  "AutoDefence.Active": {
   "t": "Baut Verteidigung automatisch."
  },
  "AutoDefence.MaxProductionHours": {
   "t": "Obergrenze der Produktionszeit eines Verteidigungsauftrags pro Himmelskörper in Stunden. 0 = kein Limit. Was nicht passt, wird in einem späteren Durchlauf gebaut."
  },
  "AutoDefence.DefenceToReach.*": {
   "t": "Anzahl dieser Verteidigungsanlage, die der Bot pro Himmelskörper baut. 0 = keine. Schilde sind an oder aus."
  },
  "AutoDefence.Exclude": {
   "t": "Himmelskörper ohne Verteidigung."
  },
  "AutoRepatriate.Active": {
   "t": "Schickt Rohstoffe von deinen Planeten zu einem Ziel-Himmelskörper."
  },
  "AutoRepatriate.Target": {
   "t": "Ziel-Himmelskörper für die Rohstoffe."
  },
  "AutoRepatriate.TargetAssociateMoon": {
   "t": "Zum Mond an der Zielposition liefern."
  },
  "AutoRepatriate.MinimumResources": {
   "t": "Nur senden, wenn mindestens so viel gesendet werden kann."
  },
  "AutoRepatriate.LeaveDeut.OnlyOnMoons": {
   "t": "Die Deuterium-Reserve nur auf Monden behalten."
  },
  "AutoRepatriate.LeaveDeut.DeutToLeave": {
   "t": "Deuterium, das zurückbleibt."
  },
  "AutoRepatriate.CargoType": {
   "t": "Schiffstyp für die Transporte."
  },
  "AutoRepatriate.SkipIfIncomingTransport": {
   "t": "Himmelskörper überspringen, zu dem schon ein Transport unterwegs ist."
  },
  "AutoRepatriate.Exclude": {
   "t": "Himmelskörper, die nichts senden."
  },
  "AutoRepatriate.Reverse.Active": {
   "t": "Umgekehrte Repatriierung: Rohstoffe aus dem Ziel zurückholen, wenn sie für einen Bau gebraucht werden. Fortgeschritten."
  },
  "AutoRepatriate.Reverse.ReturnFuelFactor": {
   "t": "Faktor für den Treibstoff, der für den Rückflug reserviert wird."
  },
  "AutoRepatriate.Reverse.ReturnAllShips": {
   "t": "Alle Schiffe zurückbringen, nicht nur die benötigten."
  },
  "AutoRepatriate.Reverse.KeepShips": {
   "t": "Schiffe, die am Ziel bleiben."
  },
  "AutoRepatriate.Reverse.PendingTtlMinutes": {
   "t": "Wie lange ein geplanter Rückholtransport gültig bleibt."
  },
  "AutoRepatriate.Reverse.OutboundCapacityFactor": {
   "t": "Anteil der Frachtkapazität, der für den Hinflug genutzt wird."
  },
  "BuyOfferOfTheDay.Active": {
   "t": "Kauft das Tagesangebot des Händlers."
  },
  "KeepBoosters.Active": {
   "t": "Aktiviert einen Booster aus dem Inventar, wenn keiner dieses Typs läuft."
  },
  "KeepBoosters.DryRun": {
   "t": "Schreibt nur ins Log, was aktiviert würde."
  },
  "KeepBoosters.MinMineLevel.*": {
   "t": "Ein Booster dieses Typs wird nur genutzt, wenn die passende Mine auf dem Planeten mindestens diese Stufe hat."
  },
  "KeepBoosters.Reserve": {
   "t": "Booster pro Typ, die im Inventar bleiben."
  },
  "KeepBoosters.LogItems": {
   "t": "Loggt aktive Items pro Planet und das Inventar."
  },
  "KeepBoosters.Tiers": {
   "t": "Erlaubte Stufen, die erste wird zuerst genutzt."
  },
  "AutoFleetJumpGate.Active": {
   "t": "Springt mit der Flotte automatisch durch das Sprungtor."
  },
  "AutoFleetJumpGate.Target": {
   "t": "Mond, zu dem die Flotte springt."
  },
  "AutoFleetJumpGate.MinimumAmountOfShipsToBeLeftOnMoon.*": {
   "t": "Schiffe dieses Typs, die auf dem Mond zurückbleiben."
  },
  "AutoFleetJumpGate.MinimumAmountOfShipPointsToTeleport": {
   "t": "Nur springen, wenn mindestens so viele Flottenpunkte mitgehen."
  }
 },
 "Expeditions": {
  "Active": {
   "t": "Hauptschalter des Expeditions-Workers."
  },
  "Origin": {
   "t": "Himmelskörper, von denen die Expeditionen starten."
  },
  "PrimaryShip": {
   "t": "Automatikmodus: Haupt-Transportschiff pro Expedition. Der Bot sendet so viele, wie für den größtmöglichen Fund nötig sind, oder alle verfügbaren, dazu eine Sonde und das stärkste Kampfschiff."
  },
  "PrimaryToKeep": {
   "t": "Automatikmodus: So viele Hauptschiffe bleiben immer zu Hause.",
   "e": "10"
  },
  "MinPrimaryToSend": {
   "t": "Automatikmodus: Mindestzahl an Hauptschiffen pro Expedition. Sind weniger übrig, wird von diesem Ursprung nichts gesendet. 0 = kein Minimum."
  },
  "ManualShips.Active": {
   "t": "Eine feste Flotte pro Expedition nutzen statt der automatischen."
  },
  "ManualShips.Ships.*": {
   "t": "Anzahl dieses Schiffstyps in jeder Expedition."
  },
  "FuelToCarry": {
   "t": "Deuterium, das jede Expedition mitnimmt."
  },
  "MaxExpeditionsPerOrigin": {
   "t": "Höchstzahl an Expeditionen, die gleichzeitig von einem Ursprung fliegen."
  },
  "SplitExpeditionsBetweenSystems.Active": {
   "t": "Die Expeditionsziele auf mehrere Systeme verteilen."
  },
  "SplitExpeditionsBetweenSystems.Range": {
   "t": "Systeme um den Ursprung, die als Ziel dienen dürfen.",
   "e": "2 = bis zu 2 Systeme in jede Richtung."
  },
  "RandomizeOrder": {
   "t": "Die Ursprünge in zufälliger Reihenfolge bearbeiten."
  },
  "IgnoreSleep": {
   "t": "Auch im SleepMode weiter Expeditionen senden."
  },
  "SleepWave.Active": {
   "t": "Eine zusätzliche Expeditionswelle kurz bevor der Bot schlafen geht."
  },
  "SleepWave.ReturnAt": {
   "t": "Zeit, zu der die Welle zurück sein soll.",
   "e": "WakeUp+15 = 15 Minuten nach dem Aufwachen."
  },
  "SleepWave.SpeedPercent": {
   "t": "Fluggeschwindigkeit der Welle, wenn keine Rückkehrzeit gesetzt ist (10 bis 100 in 10er-Schritten)."
  },
  "WaitForAllExpeditions": {
   "t": "Eine neue Runde erst starten, wenn alle Expeditionen zurück sind."
  },
  "WaitForMajorityOfExpeditions": {
   "t": "Eine neue Runde starten, wenn mehr als die Hälfte der Expeditionen zurück ist."
  },
  "MinWaitNextFleet": {
   "t": "Kürzeste Pause in Sekunden zwischen zwei Flotten einer Runde."
  },
  "MaxWaitNextFleet": {
   "t": "Längste Pause in Sekunden zwischen zwei Flotten einer Runde."
  },
  "MinWaitNextRound": {
   "t": "Kürzeste zusätzliche Pause in Sekunden nach der ersten Rückkehr vor der nächsten Runde."
  },
  "MaxWaitNextRound": {
   "t": "Längste zusätzliche Pause in Sekunden nach der ersten Rückkehr vor der nächsten Runde."
  }
 },
 "AutoHarvest": {
  "Active": {
   "t": "Sammelt Trümmerfelder mit Recyclern ein."
  },
  "HarvestOwnDF": {
   "t": "Trümmerfelder deiner eigenen Planeten und Monde einsammeln."
  },
  "HarvestForeignDF": {
   "t": "Trümmerfelder anderer Spieler einsammeln."
  },
  "HarvestDeepSpace": {
   "t": "Trümmerfelder im Weltraum (Deep Space) einsammeln."
  },
  "MinimumResourcesOwnDF": {
   "t": "Ein eigenes Feld lohnt die Reise erst ab diesem Inhalt."
  },
  "MinimumResourcesForeignDF": {
   "t": "Ein fremdes Feld lohnt die Reise erst ab diesem Inhalt."
  },
  "MinimumResourcesDeepSpace": {
   "t": "Ein Deep-Space-Feld lohnt die Reise erst ab diesem Inhalt."
  },
  "DeepSpaceRange": {
   "t": "Systeme um den Start-Himmelskörper, die nach Deep-Space-Feldern abgesucht werden."
  },
  "MaxSlots": {
   "t": "Flottenslots, die das Einsammeln gleichzeitig nutzen darf."
  }
 },
 "AutoColonize": {
  "Active": {
   "t": "Kolonisiert neue Planeten automatisch."
  },
  "Origin.*": {
   "t": "Himmelskörper, von dem die Kolonieschiffe starten."
  },
  "SlotsToLeaveFree": {
   "t": "Slots, die für die Runden dieser Funktion frei bleiben."
  },
  "Abandon.Active": {
   "t": "Neue Planeten aufgeben, die zu klein sind oder die falsche Temperatur haben. Dein Heimatplanet wird nie aufgegeben."
  },
  "Abandon.MinFields": {
   "t": "Kleinste akzeptable Planetengröße in Feldern."
  },
  "Abandon.MinTemperatureAcceptable": {
   "t": "Niedrigste akzeptable Temperatur."
  },
  "Abandon.MaxTemperatureAcceptable": {
   "t": "Höchste akzeptable Temperatur."
  },
  "IntensiveResearch.Active": {
   "t": "In Schüben kolonisieren."
  },
  "IntensiveResearch.MaxSlots": {
   "t": "Slots, die während eines Schubs genutzt werden."
  },
  "IntensiveResearch.MinWaitNextFleet": {
   "t": "Kürzeste Pause in Sekunden zwischen zwei Koloniflotten in einem Schub."
  },
  "IntensiveResearch.MaxWaitNextFleet": {
   "t": "Längste Pause in Sekunden zwischen zwei Koloniflotten in einem Schub."
  },
  "Targets": {
   "t": "Bereiche, die kolonisiert werden. Jeder hat Galaxie, Start- und Endsystem sowie Position und die größte Zahl an Planeten, die in diesem Bereich gegründet werden."
  },
  "RandomPosition": {
   "t": "Positionen innerhalb der Zielbereiche zufällig wählen."
  }
 },
 "AutoDiscovery": {
  "Active": {
   "t": "Sendet Entdeckungsmissionen."
  },
  "Origin": {
   "t": "Himmelskörper, von denen die Entdeckungsmissionen starten."
  },
  "MaxSlots": {
   "t": "Flottenslots, die Entdeckungen gleichzeitig nutzen dürfen."
  }
 },
 "FastFarm": {
  "Active": {
   "t": "Schaltet den Rohstoff-Farmmodus ein. Er scannt nach inaktiven Spielern, spioniert sie aus und raidet die besten. Defender und Schlafmodus arbeiten weiter."
  },
  "OnlyWorker": {
   "t": "Hält alle anderen Worker (Brain, AutoFarm, Expeditionen, Harvest, ...) aus, solange FastFarm aktiv ist, damit alle Flottenslots zum Farmen frei sind. Aus = die anderen Worker laufen wie konfiguriert und konkurrieren um Slots."
  },
  "KeepExpeditions": {
   "t": "Lässt die Expeditionen weiterlaufen, obwohl die anderen Worker aus sind. Sie belegen ihre eigenen Flottenslots."
  },
  "Origin": {
   "t": "Himmelskörper, von denen Sonden und Transporter starten. Für jedes Ziel wird der nächste Ursprung mit genug Schiffen genommen. Leer = alle deine Planeten."
  },
  "ScanRange": {
   "t": "Galaxie- und Systembereiche, die nach inaktiven Spielern gescannt werden, die nächsten zu deinen Ursprüngen zuerst. Leer = 100 Systeme um jeden Ursprung."
  },
  "CargoType": {
   "t": "Schiffe, die die Beute transportieren. Both = erst kleine Transporter, große nur für das, was die kleinen nicht tragen können.",
   "e": "SmallCargo ist für die meisten Ziele am sparsamsten im Treibstoff."
  },
  "ProbesPerTarget": {
   "t": "Spionagesonden pro Ziel. Mehr Sonden liefern auch gegen hohe Spionagestufen einen vollen Bericht (Flotte und Verteidigung), aber einige können verloren gehen."
  },
  "SlotsToLeaveFree": {
   "t": "Flottenslots, die FastFarm nie nutzt, damit der Defender immer eine Flotte senden kann.",
   "e": "1 = von 37 Slots werden 36 genutzt."
  },
  "FleetSpeedPercent": {
   "t": "Geschwindigkeit der Transportflotten. 100 ist am schnellsten; niedrigere Werte sparen Deuterium, belegen den Slot aber länger."
  },
  "CargoSurplusPercent": {
   "t": "Zusätzlicher Laderaum über die erwartete Beute hinaus, in Prozent. Deckt Rohstoffe ab, die während des Fluges produziert werden."
  },
  "MinFillPercent": {
   "t": "Ein Ziel wird übersprungen, wenn die übrigen Schiffe weniger als diesen Anteil seiner Beute tragen können, damit nicht ein paar Transporter zu einem großen Ziel fliegen."
  },
  "WeightMetal": {
   "t": "Wie viel ein Metall im Beutewert zählt, der die Ziele einordnet."
  },
  "WeightCrystal": {
   "t": "Wie viel ein Kristall im Beutewert zählt."
  },
  "WeightDeuterium": {
   "t": "Wie viel ein Deuterium im Beutewert zählt. Ein höherer Wert stellt deuteriumreiche Ziele nach vorn.",
   "e": "Nur das Verhältnis der drei Gewichte zählt, Standard 3 : 2 : 1 (MSE-Verhältnis); erhöhe das Deuterium-Gewicht, um deuteriumreiche Ziele nach vorn zu stellen."
  },
  "ShrinkDiscardPercent": {
   "t": "Ein Ziel fällt weg, wenn sein frischer Beutewert mindestens so viele Prozent unter dem Wert aus dem vollen Durchlauf liegt (oder unter der Mindestbeute).",
   "e": "10 = alles verwerfen, was 10 % oder mehr verloren hat."
  },
  "MinLoot": {
   "t": "Ziele mit weniger plünderbaren Rohstoffen als diesem Wert (Metall + Kristall + Deuterium, nach der Plünderquote) werden nicht angegriffen."
  },
  "MinDeuterium": {
   "t": "Ziele mit weniger plünderbarem Deuterium als diesem Wert werden nicht angegriffen. 0 = kein Limit."
  },
  "TopTargets": {
   "t": "Höchstzahl an Angriffen pro Runde, die besten Ziele zuerst."
  },
  "DeleteReports": {
   "t": "Löscht jeden Spionagebericht, nachdem er gelesen wurde. Das Postfach hat ein Limit; ist es voll, gehen neue Berichte verloren."
  },
  "ReportMaxAgeMinutes": {
   "t": "Sweep-Modus: Ein frischer Prüfbericht, der älter als das ist, wird nicht für einen Angriff genutzt (z. B. wenn lange kein Transporter frei war); das Ziel wird neu geprüft."
  },
  "MaxSystemsPerScanStep": {
   "t": "Wie viele Systeme der volle Scan einer Runde in einem Schritt besucht."
  }
 },
 "Experimental": {
  "BotPause": {
   "t": "Der Pause-Knopf in der Browser-Kachel (oder /pause in Telegram) stoppt alle Funktionen außer dem Defender für ein paar Minuten, damit nichts Neues startet, während du von Hand spielst. Danach läuft der Bot von selbst weiter. Rückrufe und der Defender laufen weiter. Eine Pause wird abgelehnt, wenn in dieser Zeit ein Rückruf fällig ist."
  },
  "BotPause.Active": {
   "t": "Die Pausenfunktion an- oder ausschalten. Aus: Der Pause-Knopf und /pause werden abgelehnt."
  },
  "BotPause.DurationMinutes": {
   "t": "Wie lange eine Pause dauert, wenn du keine Zeit angibst.",
   "e": "15 = der Bot macht 15 Minuten nach dem Druck auf Pause weiter."
  },
  "BotPause.RecallMarginMinutes": {
   "t": "Sicherheitsabstand zu einem geplanten Rückruf. Die Pause wird abgelehnt, wenn innerhalb der Pausenzeit plus diesem Abstand ein Rückruf fällig ist.",
   "e": "2 = bei 15 Minuten Pause blockiert ein Rückruf, der in 17 Minuten oder früher fällig ist, die Pause."
  },
  "BotPause.MaxDurationMinutes": {
   "t": "Längste Pause, die du mit /pause anfordern kannst."
  },
  "DefenderRecheck": {
   "t": "Kurz vor einem Fleetsave fragt der Defender das Spiel noch einmal nach der Angriffsliste. Wurde der Angriff zurückgerufen, entfällt der Fleetsave; kommt er später an, wird der Fleetsave verschoben. Schlägt die Abfrage fehl oder dauert zu lange, geht der Fleetsave wie geplant raus."
  },
  "DefenderRecheck.Active": {
   "t": "Die zweite Prüfung an- oder ausschalten (Standard aus)."
  },
  "DefenderRecheck.TimeoutSeconds": {
   "t": "Wie lange der Defender auf die Antwort wartet. Danach geht der Fleetsave wie geplant raus.",
   "e": "4 = nach 4 Sekunden ohne Antwort geht der Fleetsave raus."
  },
  "MinePlanRanking": {
   "t": "Wie die Seite Minenplan die nächsten Investitionen (Minen und Forschung) einordnet. Nur Anzeige: AutoMine und AutoResearch bauen weiter nach ihren eigenen Regeln. Du kannst diese Werte auch auf der Seite selbst ändern; die Seite merkt sich ihre Wahl in deinem Browser."
  },
  "MinePlanRanking.Mode": {
   "t": "Payback = kürzeste Amortisation zuerst (wie AutoMine). Cheapest = niedrigste Kosten zuerst. Npv = höchster Nettogewinn über den Horizont (Gewinn pro Tag mal Horizonttage minus Kosten); Schritte, die sich im Horizont nicht amortisieren, entfallen."
  },
  "MinePlanRanking.HorizonDays": {
   "t": "Horizont für die Nettogewinn-Reihung, in Tagen.",
   "e": "90 = ein Schritt ist gut, wenn er innerhalb von 90 Tagen mehr einbringt, als er kostet."
  },
  "MinePlanRanking.MaxPaybackDays": {
   "t": "Längste Amortisation, die noch als sinnvoll gilt (genutzt mit „Nur sinnvolle“)."
  },
  "MinePlanRanking.OnlySensible": {
   "t": "Schritte ausblenden, deren Amortisation länger als das Limit ist."
  },
  "MinePlanRanking.IncludeResearch": {
   "t": "Plasmatechnologie neben den Minenschritten einordnen (sie erhöht den Ertrag aller Minen auf allen Planeten)."
  },
  "FleetsaveSlot": {
   "t": "Hält den letzten freien Flottenslot für den Fleetsave frei. Jede andere Flotte, auch eine erzwungene, wird abgelehnt, wenn nur noch der reservierte Slot übrig ist, damit ein Angriff immer einen Slot für den Fleetsave findet."
  },
  "FleetsaveSlot.Active": {
   "t": "Den reservierten Slot an- oder ausschalten (Standard an)."
  },
  "FleetsaveSlot.Slots": {
   "t": "Wie viele Slots für den Fleetsave frei bleiben.",
   "e": "1 = der letzte freie Slot wird nur vom Fleetsave genutzt."
  },
  "AsteroidHunt": {
   "t": "Item-Event: Einmal pro Spieltag greift ein Schiff den Asteroiden auf Position 17 deines eigenen Systems an, danach wird das Inventar verglichen. Eine Logzeile pro Versuch, keine Messenger-Nachricht."
  },
  "AsteroidHunt.Active": {
   "t": "Den Jäger an- oder ausschalten (Standard aus)."
  },
  "AsteroidHunt.ShipType": {
   "t": "Schiff, das gesendet wird, so geschrieben wie im Spiel (englischer Name). Jedes Schiff geht, z. B. Pathfinder, Cruiser, Reaper, Destroyer; eine Sonde kann keine Items finden. Es wird der Planet mit den meisten dieser Schiffe genutzt. Ein unbekannter Name fällt auf Pathfinder zurück.",
   "e": "Pathfinder"
  },
  "AsteroidHunt.ShipCount": {
   "t": "Wie viele Schiffe gesendet werden (Standard 1)."
  },
  "AsteroidHunt.MaxAttemptsPerDay": {
   "t": "Versuche pro Spieltag (0 = unbegrenzt, höchstens 60). Laut Foren ist die Zahl der Versuche nicht begrenzt, aber pro Tag kann nur ein Item gefunden werden – der Jäger hört nach dem ersten Fund auf."
  },
  "AsteroidHunt.SearchSystems": {
   "t": "Der Asteroid ist nicht in deinem Heimatsystem: So viele Systeme werden der Reihe nach versucht (eigenes System, +1, -1, +2, -2 ...). Ein Ziel, das der Server ablehnt, kostet keinen Versuch.",
   "e": "10 = Systeme 0, +1, -1 ... bis zu 5 entfernt."
  },
  "AsteroidHunt.SaveJson": {
   "t": "Jeden Versuch und Fund nach data/asteroiden_funde_<alias>.json schreiben."
  }
 }
};
