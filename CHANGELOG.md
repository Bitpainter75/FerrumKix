# Changelog

## Unveroeffentlicht

- MPRIS: Waybar, playerctl, die Benachrichtigungen und die Multimedia-Tasten des Systems erreichen
  FerrumPlay jetzt auch, wenn das Fenster im Hintergrund liegt. Titel, Interpret, Album, Laufzeit
  und Titelbild gehen mit hinaus.
- FerrumPlay laeuft nur noch einmal. Wer eine Datei oder einen Ordner oeffnet, waehrend es laeuft,
  bekommt sie in der laufenden Instanz gespielt. Der Desktop-Eintrag nimmt dafuer jetzt Dateien und
  Ordner an.
- Lautstaerkeangleichung nach ReplayGain: aus, nach Titel, nach Album oder automatisch, dazu eine
  Vorverstaerkung.
- Titel, deren Datei fehlt, werden beim Start erkannt, ausgegraut und uebersprungen. Sie kommen
  zurueck, sobald die Datei wieder da ist; ein Knopf unter der Liste entfernt sie alle auf einmal.
  Laesst sich ein Titel nicht abspielen, geht es mit dem naechsten weiter, statt stehen zu bleiben.

## 0.1.0

Die erste Fassung.

FerrumPlay spielt Musik: MP3, FLAC, OGG, Opus, M4A und die uebrigen gaengigen Formate. Die
Wiedergabeliste ordnet sich nach Alben, das Titelbild und die Angaben zum Stueck stehen links,
der Transport unten auf einem dunklen Deck, die Lautstaerke als Drehregler. Dateien und Ordner
lassen sich ins Fenster ziehen oder beim Aufruf uebergeben (`FerrumPlay ~/Musik/Album`); der erste
Titel wird dann gespielt.

Akzentfarbe, Schriftgroesse, Sprache (Deutsch, Englisch) und die Vergroesserung der Anwendung
lassen sich einstellen. Die Wiedergabeliste und der zuletzt gespielte Titel stehen beim naechsten
Start wieder da.
