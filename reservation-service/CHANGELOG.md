# Server Changelog

## 2.0.0

- Client-Version mindestens 2.0.0 erzwingen; unbekannte/veraltete Clients vor neuen Auftraegen mit HTTP 426 ablehnen.
- Effektive Mindestversion ueber /health melden. MINIMUM_CLIENT_VERSION kann den im Release festgelegten Mindeststand fuer Sicherheitsupdates weiter anheben.
- Abschluss und Freigabe eigener bestehender Auftraege bleiben nach einer Richtlinienaenderung moeglich. Adminzugriff bleibt unabhaengig von der Client-Version.
- Client-/API-Versionsheader auch im produktiven WSGI-Adapter auswerten.

API-Version 1 und Datenbankschema 3 bleiben erhalten. Vor Service-Update Client 2.0 installieren.

## 1.4.3

- Sekundaerlabels pro Dokument erfassen und in Dokumentlisten anzeigen.
- API-Version und Faehigkeiten ueber /health melden.
- Lizenztexte und Quellcode im Adminpanel bereitstellen.

API-Version 1; Datenbankschema 3. Bestehende Reservierungen bleiben erhalten.
Vor Migrationen Backups sichern; Downgrades benoetigen einen passenden Datenbankstand.
