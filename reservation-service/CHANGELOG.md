# Server Changelog

## 2.3.1

- Bei unverändertem Reservierungsstatus entstehen keine zusätzlichen Ereignisse. Alte gleiche Statusübergänge werden einmalig bereinigt; echte Statuswechsel bleiben erhalten. Es gibt keinen Anzeigefilter und keine zusätzliche Statustabelle.
- Verschwindet ein Dokument, bleiben normal gedruckte Labels und importierte ASNs mit Sekundärlabel als „Scan ausstehend“ erhalten. Ihr Reservierungsstatus bleibt unverändert.
- Rein importierte ASNs ohne Drucknachweis werden gesperrt oder, ausschließlich direkt oberhalb des aktuellen Paperless-Höchststands und ohne höhere Schutzgrenzen, automatisch wiederverwendbar. Der gespeicherte Höchststand wird nicht abgesenkt.
- Sekundärlabel-Indikatoren bleiben nach dem Verschwinden des Dokuments erhalten; der Änderungsverlauf zeigt lesbare deutsche und englische Bezeichnungen.
- `.env` und Datenbankschema 3 bleiben unverändert. Beim regulären Serverstart wird vor der einmaligen Verlaufsbereinigung eine Sicherung erstellt. Client 2.3.0 bleibt kompatibel. Keine Änderungen am Header oder am Abfrageintervall.

## 2.3.0

- Gleichzeitige Erstzugriffe auf SQLite warten beim Aktivieren von WAL begrenzt auf eine belegte Datenbank, statt sporadisch mit „database is locked“ abzubrechen.

- Die Dokumentübersicht erkennt alle in Paperless vergebenen ASNs, auch ohne vorherigen normalen Labeldruck. Fehlende lokale Labeleinträge werden beim Abrufen automatisch ergänzt; bestehende Sekundärlabels werden mitgezählt.
- Auch nachträglich importierte Dokumente unterhalb der bisherigen höchsten ASN werden erkannt. Nummernlücken ohne tatsächliches Paperless-Dokument bleiben unberührt; laufende Druckaufträge behalten Identität, Besitzer und Zustand.
- Reservierungen unterscheiden „Gedruckt“ (bestätigter normaler Labeldruck) von „Importiert“ (nur in Paperless gefunden). Sekundärlabels verändern diese Herkunft nicht.
- Zuvor in Paperless erkannte, später nicht mehr gefundene Dokumente werden sicherheitshalber gesperrt. Bei erneutem Auftauchen wird abhängig von der erhaltenen Druckhistorie wieder „Gedruckt“ oder „Importiert“ angezeigt. Verbindungsfehler gelten nicht als Löschung; laufende Druckaufträge und manuelle Freigaberegeln bleiben unverändert.
- `.env`: unverändert, keine neuen Variablen oder geänderten Defaults. Kein Schemawechsel und keine manuelle Datenmigration erforderlich; Client 2.3.0 bleibt kompatibel.

## 2.2.1

- Manuelle Updateprüfung in den Admin-Einstellungen mit Ladezustand und Ergebnis; parallele Prüfungen und Wiederholungen innerhalb von 60 Sekunden werden zusammengefasst.
- Compose-Dateien heißen jetzt `docker-compose.yaml` und `docker-compose.registry.yaml`; bestehende Projekt-/Volume-Namen beibehalten und Dateiverweise anpassen.
- Registry-Compose verwendet eine feste `stable`-Imageadresse. Der optionale Host-Updater bindet den geprüften Digest über eine temporäre Override-Datei und prüft die Version über den Health-Response-Header.
- `.env`: keine neuen Pflichtvariablen. Eine bisher selbst gesetzte `ASN_SERVER_IMAGE` wird nicht mehr ausgewertet; eigene Image-Pins direkt in Compose bzw. einer Override-Datei pflegen.

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
