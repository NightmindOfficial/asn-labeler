# Client Changelog

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
