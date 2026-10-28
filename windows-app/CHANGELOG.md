# Client Changelog

## 2.3.0

- CLI `-m` / `--media`: Druckermodell und tatsächlich eingelegtes Medium ohne Serveranfrage oder Druck abfragen; optional mit `-j` als JSON.
- CLI standardmäßig mit lesbaren Fortschrittsmeldungen und Fehlerhinweisen in der App-Sprache. Für bestehende JSON-Skripte jetzt `-j` / `--json` ergänzen; Exitcodes und JSON-Ereignisse bleiben erhalten.
- A4-Stellvertreterkarten mit Dokumentauswahl, Aufbewahrungsort, Dauer, optionalen Entnahme-/Rückgabedaten und Handzeichen; Vorschau und Windows-Druckerauswahl getrennt vom Brother-Labeldruck.
- Nach einem Sekundärlabel optional eine Karte drucken; beim Kartendruck mit erreichbarem Brother standardmäßig ein Sekundärlabel mitdrucken, abwählbar.
- Blankokarten ohne API, Dokumentzuordnung oder Labeldruck. Kartendaten werden nicht auf dem Server gespeichert und verändern keine ASN.
- Karten verwenden das ursprüngliche Vorlagenlayout; der Dokumenttitel stammt unveränderbar aus Paperless. Dauerhafte Entnahmen enthalten keine geplante Rückgabe, Blankokarten öffnen direkt die Druckerauswahl.
- Keine Änderungen an `.env` erforderlich.

## 2.2.0

- Sekundärlabels gezielt über die ASN suchen; Auswahl und Prüfung des Dokuments bleiben vor dem Druck erforderlich.
- Sekundärlabel-Anzahl nur noch als Punkt-/Ring-Marker in der Dokumentauswahl, mit genauer Anzahl im Tooltip.
- Serverversion unter der Adresse anzeigen, auch bei inkompatiblen Serverantworten.
- Changelog offline unter Einstellungen > Über verfügbar; Update-Button über die gesamte Breite.
- Demo-Modus in den Wartungsbereich verschoben.
- Server 2.2.0 bleibt unverändert. Keine Änderungen an `.env` oder ASN-Vergaberegeln.

## 2.1.1

- Unabhängige, RSA-signierte GitHub-Sicherheitsrichtlinie beim Start und bei manueller Updateprüfung; CLI prüft vor dem Drucken. Verifizierte Sperren bleiben offline erhalten und können nicht durch eine ältere Richtlinie oder Serverantwort aufgehoben werden.
- Gesperrte Clients können keine neuen Druckaufträge starten; Bestätigungen laufender Aufträge sowie Update und Diagnose bleiben verfügbar.

- Update-Download: Windows-Herkunftsmarkierung über nativen Dateizugriff schreiben; behebt den Pfadformatfehler von .NET Framework, ohne die Internet-Zonenmarkierung zu entfernen.
- Update-Prüfung bei jedem App-Start, ohne gespeicherte Einstunden-Sperre. Manuelle Prüfung bleibt möglich.
- Für den Wechsel von 2.0.0/2.1.0 mit betroffenem Updater den neuen Setup-Installer einmal direkt aus dem GitHub-Release herunterladen und ausführen. Keine vorherige Deinstallation nötig.

- Hosting-neutrale Beispieladresse im Einstellungs-Selbsttest. Keine Änderung am Druck- oder API-Verhalten.

## 2.1.0

- Oberfläche bereinigt: A4-Platzhalter und doppelte Hinweise entfernt; neue
  Installationen starten ohne vorbelegte Serveradresse. Gespeicherte Adressen bleiben erhalten.

- Optionaler Konsolenclient `asnlbl` mit verwaltetem PATH-Eintrag; Auswahl bleibt bei Updates erhalten.
- `asnlbl help`, Kurzflags `-c`, `-s`, `-h`, `-v` und separate Anleitung in docs/CLI.md.
- Einzel- und Seriendruck (1-10), optionaler Einzelschnitt, JSON-Zeilen und eindeutige Exitcodes.
- Gleiche Medienprüfung und Reservierungs-/Druckbestätigung wie in der Oberfläche; keine automatischen Wiederholungen nach unklarem Druck.
- CLI verweigert den Start bei geöffneter App oder zweiter CLI in derselben Windows-Sitzung; Installer erkennt laufende CLI-Aufträge.

## 2.0.0

- Windows-Installer mit benutzerbezogener oder systemweiter Installation.
- GitHub-Updateprüfung und bestätigte Installation ohne Unterbrechung von Druckaufträgen.
- Geführte b-PAC-Einrichtung und Modellübersicht; QL-810W/QL-820NWB experimentell nur für Testdruck.
- Bestehende Einstellungen bleiben erhalten. Keine separate portable Veröffentlichung mehr.
- Produktive API-Zugriffe nur aus registrierter Installation; ausdrücklicher Entwicklungsmodus im lokalen Testbuild.
- Vom Server geforderte Mindestversion erkennen, neue Druckaufträge sperren und Updates anbieten. Einstellungen/Diagnose bleiben erreichbar.
- Ruhige Informationsdialoge ohne Kopierbutton; kopierbare Fehlerdiagnosen bleiben erhalten.

API-Version 1 bleibt unverändert; Server 1.4.3 ist weiterhin kompatibel.
Installer und Anwendung sind vorerst nicht codesigniert.

## 1.4.3

- Sekundärlabel-Zähler und sichere Wiederholung von Druckbestätigungen.
- Unveränderte Servereinstellungen vermeiden unnötige Abfragen.
- API-Kompatibilitätsprüfung und Lizenzinformationen.

Kompatibel mit API-Version 1. Sekundärlabels benötigen
`secondary-label-tracking-v1`, sicherer Abgleich `safe-sync-v1`.
