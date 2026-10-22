# Changelog

## 1.4.3

- Bestaetigte Sekundaerlabels pro Dokument zaehlen und in Dokumentlisten anzeigen.
- Druckbestaetigungen bei Verbindungsfehlern erneut uebermitteln, ohne erneut zu drucken.
- Unveraenderte Servereinstellungen loesen beim Schliessen keinen sofortigen Abgleich aus.
- API-Version und Faehigkeiten beim Verbindungstest beruecksichtigen.
- AGPLv3-Lizenztexte und b-PAC-Zusatzerlaubnis im Client; Lizenz- und Quellcodezugriff im Adminpanel.

Die Datenbank wird auf Schema 3 erweitert. Bestehende Reservierungen bleiben
erhalten; vor der Migration wird eine Sicherung erstellt. Ein Downgrade erfordert
einen geeigneten Datenbankstand und die Klaerung aller seither gedruckten Labels.
Sekundaerlabel-Zaehler werden nicht rueckwirkend fuer fruehere Drucke ergaenzt.

## 1.4.0 - 1.4.2

- Ueberarbeitete Einstellungen, Sprachumschaltung und Versionsanzeige.
- Eigene Registerkarten fuer Sekundaerlabels und Softwareinformationen.
- Verbindungsverlust und Wiederherstellung im Adminpanel anzeigen.
- Veraltete Admin-Oberflaechen nach einem Service-Versionswechsel neu laden.

## 1.3.x

- Einstellbare Aktualisierungsintervalle und persistente Listenfilter.
- Sekundaerlabels fuer bereits in Paperless vorhandene Dokumente.
- Verbesserte Druckereinrichtung und paginierter Aenderungsverlauf.

## 1.2.0

- Serverpruefung vor Druckerpruefung; ASN-Anzeige auch ohne angeschlossenen Drucker.
- Unterstuetzung fuer Endlosmedien verbessert.
- In Paperless gefundene ASNs mit lokalen Reservierungen abgleichen.

## 1.1.x

- Seriendruck mit 1 bis 10 Labels und waehlbarem Einzelschnitt.
- Anzeige von In Druck getrennt von Druck unklar.
- Ueberarbeitete Druck- und Aktualisierungsbedienelemente.

## 1.0.x

- Windows-Client mit Brother-b-PAC-Anbindung und DataMatrix-Labels.
- PF-B11221-Rollenprofil fuer kompatible 23x23-mm-Labels.
- Reservation-Service mit Dokumentstatus, Web-Administration und geschuetzter ASN-Freigabe.
