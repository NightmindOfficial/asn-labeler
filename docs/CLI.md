# CLI: asnlbl

## Installation

Im Windows-Setup die optionale Auswahl **Kommandozeilenwerkzeug (asnlbl)**
aktivieren. Sie ist bei einer neuen Installation standardmäßig ausgeschaltet.
Der Installer fügt nur den Unterordner `cli` zum PATH hinzu: bei einer
Benutzerinstallation zum Benutzer-PATH, bei einer systemweiten Installation
zum System-PATH. Danach ein neues Terminal öffnen; gegebenenfalls Windows
Terminal komplett beenden und neu starten.

Updates übernehmen die bisherige Auswahl. Mit erneutem Setup kann die CLI
nachinstalliert oder abgewählt werden. Bei Abwahl und Deinstallation entfernt
Setup nur den PATH-Eintrag, den es selbst angelegt hat.

Zuerst die grafische App einrichten, Demo-Modus ausschalten und die App schließen.
Die CLI verwendet deren Serveradresse, Credential-Manager-Schlüssel, Drucker
und Rollenprofil. Sie fragt keine Zugangsdaten ab und öffnet kein Fenster.

## Syntax

```text
asnlbl help
asnlbl -m [-j]
asnlbl print [-c ANZAHL] [-s] [-j]
asnlbl -h
asnlbl -v
```

| Flag | Bedeutung |
| --- | --- |
| `-c ANZAHL` | 1 bis 10 Labels, Standard: 1 |
| `-s` | Jedes Label einzeln schneiden; sonst erst am Serienende |
| `-j` / `--json` | Maschinenlesbare JSON-Zeilen statt Klartext |
| `-m` / `--media` | Modell und eingelegtes Medium abfragen, ohne Druck oder Serveranfrage |
| `-h` | Hilfe mit Syntax, Flags, Beispielen und Exitcodes |
| `-v` | Installierte Client-Version |

Die langen Aliase `--count`, `--cut-each`, `--help` und `--version` bleiben
gültig. Flags werden getrennt angegeben, beispielsweise `-c 3 -s`.

```powershell
asnlbl print
asnlbl print -c 3
asnlbl print -c 3 -s
$LASTEXITCODE
```

## Ergebnisse und Fehler

`asnlbl -m` fragt den in der App ausgewählten Brother-Drucker ab. Ohne Auswahl
wird ein eindeutiger Treffer verwendet; mehrere Treffer erfordern eine Auswahl
in den Einstellungen. Die App muss wie beim CLI-Druck geschlossen sein.
Es wird auch im Demo-Modus echte Hardware abgefragt, keine Demo-Rolle.
`asnlbl -m -j` liefert zusätzlich strukturierte Maße in Millimetern;
bei Endlosmaterial ist `lengthMm` null. Ein erfolgreicher Abruf hat Exitcode 0,
auch wenn `ready` false ist. Eine fehlgeschlagene Abfrage hat Exitcode 2.

Standardmäßig erscheinen lesbare Fortschritts- und Abschlussmeldungen auf stdout,
Fehler mit Handlungshinweisen auf stderr. Die Sprache folgt den App-Einstellungen.
`asnlbl print -c 3 -j` liefert stattdessen die bisherigen JSON-Zeilen ohne
zusätzliche Fortschrittstexte. **Bestehende Skripte, die JSON auswerten, müssen
ab Client 2.3.0 `-j` oder `--json` ergänzen.** Exitcodes bleiben unverändert.
Jedes abgeschlossene Label wird separat gemeldet. API-Schlüssel werden nicht
ausgegeben. Eine laufende GUI oder zweite CLI in derselben Windows-Sitzung
verhindert einen weiteren CLI-Druck.

| Exitcode | Bedeutung |
| --- | --- |
| 0 | Erfolgreich |
| 2 | Argumente, Konfiguration oder initiale Verbindung prüfen |
| 3 | App, CLI oder Setup bereits aktiv |
| 4 | Aktueller Druck nicht gestartet; vorherige Labels der Serie beachten |
| 5 | Druckausgang oder API-Bestätigung unklar; in der Dokumentenliste prüfen |

Niemals eine fehlgeschlagene Serie blind wiederholen: Zuvor abgeschlossene
Labels bleiben gedruckt. Unklare ASNs werden nicht automatisch freigegeben.
Ein abgebrochener Sammeldruck kann einen noch ungeschnittenen Streifen hinterlassen.
Nach einem COM-Timeout wartet die CLI gegebenenfalls weiter auf den laufenden
Hintergrundauftrag. Keinen Nachdruck starten, bis der Druckausgang geklärt ist.

Falls `asnlbl` nicht gefunden wird: Auswahl im Setup prüfen und Terminal neu
starten. Die Datei liegt unter `<Installationsordner>\cli\asnlbl.exe` und kann
auch direkt über diesen vollständigen Pfad gestartet werden.
