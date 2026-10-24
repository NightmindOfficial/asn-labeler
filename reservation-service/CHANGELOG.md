# Server Changelog

## 2.1.0

- Persistente Sicherheitsprüfung gegen die offizielle GitHub-Release-Richtlinie; bekannte unsichere Server sperren neue Reservierungen und Druckstarts, nicht deren Bestätigung.
- Unbekannter Sicherheitsstand oder GitHub-Ausfall erzeugt keine neue Sperre; bereits bekannte Mindeststände bleiben erhalten.
- Mindest-Clientversion außchließlich im Release festgelegt; alte Env-Überschreibung entfällt.
- Getestetes Docker-Image in GHCR, Registry-Compose und ausdrücklich aktivierbarer NAS-Updater mit Druckruhe-Prüfung, Wartungslease und SQLite-Backup.
- Adminfooter fest am unteren Fensterrand, gestaltete Links inklusive nightmind.dev.
- Wiederverbindungsversuche enden nach zwei Minuten; danach ist manuelles Neuladen erforderlich.

## 2.0.0

- Client-Version mindestens 2.0.0 erzwingen; unbekannte/veraltete Clients vor neuen Aufträgen mit HTTP 426 ablehnen.
- Effektive Mindestversion über /health melden. MINIMUM_CLIENT_VERSION kann den im Release festgelegten Mindeststand für Sicherheitsupdates weiter anheben.
- Abschluss und Freigabe eigener bestehender Aufträge bleiben nach einer Richtlinienänderung möglich. Adminzugriff bleibt unabhängig von der Client-Version.
- Client-/API-Versionsheader auch im produktiven WSGI-Adapter auswerten.

API-Version 1 und Datenbankschema 3 bleiben erhalten. Vor Service-Update Client 2.0 installieren.

## 1.4.3

- Sekundärlabels pro Dokument erfassen und in Dokumentlisten anzeigen.
- API-Version und Fähigkeiten über /health melden.
- Lizenztexte und Quellcode im Adminpanel bereitstellen.

API-Version 1; Datenbankschema 3. Bestehende Reservierungen bleiben erhalten.
Vor Migrationen Backups sichern; Downgrades benötigen einen passenden Datenbankstand.
