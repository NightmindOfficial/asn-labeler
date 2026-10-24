# Client Changelog

## 2.1.0

- Optionaler Konsolenclient `asnlbl` mit verwaltetem PATH-Eintrag; Auswahl bleibt bei Updates erhalten.
- `asnlbl help`, Kurzflags `-c`, `-s`, `-h`, `-v` und separate Anleitung in docs/CLI.md.
- Einzel- und Seriendruck (1-10), optionaler Einzelschnitt, JSON-Zeilen und eindeutige Exitcodes.
- Gleiche Medienpruefung und Reservierungs-/Druckbestaetigung wie in der Oberflaeche; keine automatischen Wiederholungen nach unklarem Druck.
- CLI verweigert den Start bei geoeffneter App oder zweiter CLI in derselben Windows-Sitzung; Installer erkennt laufende CLI-Auftraege.

## 2.0.0

- Windows-Installer mit benutzerbezogener oder systemweiter Installation.
- GitHub-Updatepruefung und bestaetigte Installation ohne Unterbrechung von Druckauftraegen.
- Gefuehrte b-PAC-Einrichtung und Modelluebersicht; QL-810W/QL-820NWB experimentell nur fuer Testdruck.
- Bestehende Einstellungen bleiben erhalten. Keine separate portable Veroeffentlichung mehr.
- Produktive API-Zugriffe nur aus registrierter Installation; ausdruecklicher Entwicklungsmodus im lokalen Testbuild.
- Vom Server geforderte Mindestversion erkennen, neue Druckauftraege sperren und Updates anbieten. Einstellungen/Diagnose bleiben erreichbar.
- Ruhige Informationsdialoge ohne Kopierbutton; kopierbare Fehlerdiagnosen bleiben erhalten.

API-Version 1 bleibt unveraendert; Server 1.4.3 ist weiterhin kompatibel.
Installer und Anwendung sind vorerst nicht codesigniert.

## 1.4.3

- Sekundaerlabel-Zaehler und sichere Wiederholung von Druckbestaetigungen.
- Unveraenderte Servereinstellungen vermeiden unnoetige Abfragen.
- API-Kompatibilitaetspruefung und Lizenzinformationen.

Kompatibel mit API-Version 1. Sekundaerlabels benoetigen
`secondary-label-tracking-v1`, sicherer Abgleich `safe-sync-v1`.
