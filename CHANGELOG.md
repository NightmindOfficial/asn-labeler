# Changelog

## 1.4.3

- Bestätigte Sekundärlabels pro Dokument zählen und in Dokumentlisten anzeigen.
- Druckbestätigungen bei Verbindungsfehlern erneut übermitteln, ohne erneut zu drucken.
- Unveränderte Servereinstellungen lösen beim schließen keinen sofortigen Abgleich aus.
- API-Version und Fähigkeiten beim Verbindungstest berücksichtigen.
- AGPLv3-Lizenztexte und b-PAC-Zusatzerlaubnis im Client; Lizenz- und Quellcodezugriff im Adminpanel.

Die Datenbank wird auf Schema 3 erweitert. Bestehende Reservierungen bleiben
erhalten; vor der Migration wird eine Sicherung erstellt. Ein Downgrade erfordert
einen geeigneten Datenbankstand und die Klärung aller seither gedruckten Labels.
Sekundärlabel-Zähler werden nicht rückwirkend für frühere Drucke ergänzt.

## 1.4.0 - 1.4.2

- Überarbeitete Einstellungen, Sprachumschaltung und Versionsanzeige.
- Eigene Registerkarten für Sekundärlabels und Softwareinformationen.
- Verbindungsverlust und Wiederherstellung im Adminpanel anzeigen.
- Veraltete Admin-Oberflächen nach einem Service-Versionswechsel neu laden.

## 1.3.x

- Einstellbare Aktualisierungsintervalle und persistente Listenfilter.
- Sekundärlabels für bereits in Paperless vorhandene Dokumente.
- Verbesserte Druckereinrichtung und paginierter Änderungsverlauf.

## 1.2.0

- Serverprüfung vor Druckerprüfung; ASN-Anzeige auch ohne angeschlossenen Drucker.
- Unterstützung für Endlosmedien verbessert.
- In Paperless gefundene ASNs mit lokalen Reservierungen abgleichen.

## 1.1.x

- Seriendruck mit 1 bis 10 Labels und wählbarem Einzelschnitt.
- Anzeige von In Druck getrennt von Druck unklar.
- Überarbeitete Druck- und Aktualisierungsbedienelemente.

## 1.0.x

- Windows-Client mit Brother-b-PAC-Anbindung und DataMatrix-Labels.
- PF-B11221-Rollenprofil für kompatible 23x23-mm-Labels.
- Reservation-Service mit Dokumentstatus, Web-Administration und geschützter ASN-Freigabe.
