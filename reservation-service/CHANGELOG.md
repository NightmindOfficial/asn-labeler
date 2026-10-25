# Server Changelog

## 2.2.0

- Öffentlicher Healthcheck auf Betriebs- und Kompatibilitätsangaben reduziert; Update-Diagnosen bleiben ausschließlich im authentifizierten Adminbereich verfügbar.
- Service-Protokoll über den gespeicherten Entwicklermodus in den Einstellungen einblendbar; einzelne Ereignisse lassen sich kopieren.
- Lizenzen, Drittanbieterhinweise und Changelog in lesbaren Dialogen. Quellcode-Download im Lizenzdialog statt im Footer.
- Sekundärlabels durch einen gleich großen orangefarbenen Punkt am Dokumentstatus markiert: ausgefüllt bei einem Exemplar, Innenkontur bei mehreren. Die genaue Anzahl steht im Tooltip.
- Hintergrund während geöffneter Dialoge gegen Scrollen gesperrt, einschließlich Verbindungsabbrüchen.
- Keine Änderungen an `.env`, Datenbankschema oder ASN-Vergabe. Client 2.1.1 bleibt kompatibel.

## 2.1.1

- Mindest-Clientversion 2.1.1: ältere Clients mit defektem Updater müssen einmal manuell aktualisiert werden. Bestehende Druckbestätigungen bleiben möglich.
- Release-unabhängige, signierte Sicherheitsrichtlinie von GitHub für Client- und Server-Mindestversionen. Prüfung beim Start und stündlich; bekannte Sperren bleiben offline und nach Neustart erhalten. Neue Arbeit wird transaktional gesperrt, ohne bestehende ASNs freizugeben.
- Keine zusätzlichen `.env`-Variablen für die Sicherheitsrichtlinie. SQLite erhält automatisch eine zusätzliche Tabelle; bestehende Reservierungen bleiben unverändert.

- Hosting-neutrale Paketnamen und Dokumentation: `asn-reservation-2.1.1.zip`.
- `.env`-Migration beim Austausch der Compose-Dateien: bisherigen Bind-Adressenschlüssel in `HOST_BIND_IP` umbenennen, Wert beibehalten. Standard bleibt `127.0.0.1`. Reine Image-Updates ändern bestehende Compose-Konfigurationen nicht.

## 2.1.0

- Persistente Sicherheitsprüfung gegen die offizielle GitHub-Release-Richtlinie; bekannte unsichere Server sperren neue Reservierungen und Druckstarts, nicht deren Bestätigung.
- Unbekannter Sicherheitsstand oder GitHub-Ausfall erzeugt keine neue Sperre; bereits bekannte Mindeststände bleiben erhalten.
- Mindest-Clientversion außchließlich im Release festgelegt; alte Env-Überschreibung entfällt.
- Getestetes Docker-Image in GHCR, Registry-Compose und ausdrücklich aktivierbarer Host-Updater mit Druckruhe-Prüfung, Wartungslease und SQLite-Backup.
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
