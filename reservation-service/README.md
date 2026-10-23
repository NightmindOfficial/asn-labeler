# Reservation-Service

Der Dienst koordiniert ASN-Nummern fuer eine Paperless-Instanz. SQLite speichert
Reservierungen, Druckzustaende, Sekundaerlabel-Auftraege und den Aenderungsverlauf.
Das Adminpanel ist unter `/admin/` erreichbar. Version und API-Faehigkeiten liefert
`/health`. Der Dienst laedt keine Dokumente zu Paperless hoch.

## Voraussetzungen

Ab Service 2.0 wird Client 2.0 oder neuer benoetigt. Zuerst den Client per Setup
installieren, dann den Service aktualisieren. Die Versionssperre gilt fuer neue
Clientaktionen; bestehende eigene Druckauftraege duerfen noch bestaetigt werden.
Adminpanel und administrative Werkzeuge bleiben mit separatem Admin-Key erreichbar.
`MINIMUM_CLIENT_VERSION` kann fuer Sicherheitsupdates angehoben werden; nach
Aenderung den Dienst neu bereitstellen. Der im Release eingebaute Mindeststand
kann dadurch nicht abgesenkt werden. Es wird nicht pauschal die neueste
GitHub-Version erzwungen, und ein GitHub-Ausfall sperrt keinen zugelassenen Client.

- Docker mit Compose, etwa die Docker-Projektverwaltung eines NAS.
- Eine erreichbare paperless-ngx-Instanz und ein API-Token mit Leserechten auf
  alle Dokumente des verwendeten ASN-Nummernkreises.
- Einen eigenen langen Client-Schluessel und einen davon verschiedenen
  Admin-Schluessel. Beide sind Zugangsdaten und duerfen nicht veroeffentlicht werden.

## Einrichtung

1. Dieses Service-Verzeichnis oder das vollstaendige NAS-Releasepaket in einen
   dauerhaften Projektordner kopieren. `web/`, `licenses/`, `tests/` und alle
   Build-Dateien werden benoetigt. Keine Verzeichnisse auf `/opt/asn` mounten.
2. `.env.example` als `.env` kopieren und die Platzhalter ersetzen:

   | Variable | Bedeutung |
   | --- | --- |
   | `PAPERLESS_URL` | Vom Container aus erreichbare Paperless-Adresse |
   | `PAPERLESS_TOKEN` | Paperless-API-Token |
   | `ASN_PREFIX` | Barcode-Praefix, muss der Paperless-Konfiguration entsprechen |
   | `ASN_PADDING` | Mindestanzahl der Ziffern; Standard 5 |
   | `API_KEYS` | Kommagetrennte Client-Schluessel, mindestens 32 Zeichen je Schluessel |
   | `ADMIN_API_KEYS` | Separate Admin-Schluessel, mindestens 32 Zeichen je Schluessel |
   | `RESERVATION_TTL_SECONDS` | Lebensdauer nie gestarteter Reservierungen; Standard 300, Bereich 30 bis 3600 |
   | `PRINTING_SETTLE_SECONDS` | Wartezeit vor Anzeige eines unbestaetigten Drucks als unklar; Standard 30, mindestens 10 |
   | `MINIMUM_CLIENT_VERSION` | Mindestversion des Clients, Standard 2.0.0; kann fuer Sicherheitsupdates angehoben werden |
   | `SQLITE_PATH` | Datenbank im persistenten Volume, Standard `/data/reservations.sqlite3` |
   | `NAS_BIND_IP` | Hostadresse fuer Port 8765; Standard `127.0.0.1` |

   `localhost` in `PAPERLESS_URL` bezeichnet den Service-Container, nicht das NAS
   oder einen anderen Container. Zufallsschluessel separat erzeugen, beispielsweise
   mit `python -c "import secrets; print(secrets.token_urlsafe(32))"`.
3. In der NAS-Oberflaeche ein Compose-Projekt mit der mitgelieferten Datei anlegen
   und bauen/starten. Alternativ im Service-Ordner:

   ```sh
   docker compose up -d --build
   ```

4. `/health` ueber die konfigurierte Adresse aufrufen. Erwartet werden `ok: true`
   und die installierte Versionsnummer. Anschliessend `/admin/` oeffnen,
   mit einem Admin-Schluessel anmelden und die Paperless-Verbindung pruefen.
5. Im Windows-Client dieselbe Basisadresse und einen Client-Schluessel eintragen.
   Demo-Modus ausschalten. Fuer einen HTTPS-Tunnel keine interne Portnummer anhaengen.

Das benannte Datenvolume `asn-reservation-data` bleibt bei Container-Neustarts
erhalten. Projektname und Volume sind Teil der Installation und duerfen beim
Update nicht versehentlich gewechselt werden.

## Netzwerk und Zugriff

Die Voreinstellung bindet Port 8765 nur lokal am Host. Fuer direkten LAN-Zugriff
`NAS_BIND_IP` auf die konkrete LAN-IP des NAS setzen; der direkte Port bietet
selbst kein TLS. Fuer oeffentlichen Zugriff einen HTTPS-Reverse-Proxy verwenden.

Ein Cloudflare-Tunnel kann intern an `http://asn-reservation:8765` weiterleiten,
wenn sein Container am Standardnetzwerk dieses Compose-Projekts angeschlossen
ist. Die Service-Compose benoetigt dafuer kein externes Netzwerk. Im Client steht
dann beispielsweise `https://asn.example.com`, ohne `:8765`.

Client- und Admin-Endpunkte verwenden Bearer-Schluessel. Fehlende Admin-Schluessel
deaktivieren Admin-Funktionen. Die oeffentlichen Pfade `/health`, `/admin/` und
die statischen Panel-/Lizenz-/Quellcode-Dateien benoetigen keinen Schluessel;
sie enthalten keine Dokumentdaten. Lizenz und Service-Quellcode sind ueber den
Footer erreichbar.

## Administration und Updates

- Dokumente und Reservierungen werden getrennt angezeigt, jeweils nach ASN
  absteigend. Die Dokumentliste umfasst die vom Dienst verwalteten Reservierungen,
  nicht alle unabhaengig davon in Paperless angelegten Dokumente.
- Aktualisierungsintervall und Wartungsaktionen stehen in den Einstellungen.
  Der Aenderungsverlauf zeigt die neuesten Eintraege zuerst und laedt weitere
  bei Bedarf. Sekundaerlabels werden als bestaetigte Kopien gezaehlt.
- Vor Updates ein Backup herunterladen, aktive Druckauftraege beenden und die
  Release Notes lesen. Paket im bestehenden Projektordner ersetzen; `.env` und
  Datenvolume behalten. Das Projekt neu bauen/bereitstellen, nicht nur neu starten.
- Danach `/health`, Adminpanel und Client pruefen. Ein altes Panel nach einem
  Versionswechsel neu laden. Bei Schemaaenderungen ist ein altes Image allein
  kein geeigneter Rollback; eine Backup-Wiederherstellung muss alle inzwischen
  physisch gedruckten Labels beruecksichtigen.

Ein Abgleich gibt gedruckte oder unklare ASNs nicht pauschal frei. Der gespeicherte
Paperless-Hoechststand sinkt auch nach Loeschung eines Paperless-Dokuments nicht
automatisch. Korrekturen und Freigaben erfordern die vorgesehenen bestaetigten
Admin-Aktionen und koennen durch geschuetzte Nummern verhindert werden.

Niemals `docker compose down -v` zur Fehlerbehebung verwenden. Eine alte Datenbank
oder ein neues leeres Volume kann bereits gedruckte Labels vergessen und dadurch
Doppelvergaben verursachen. Backups nicht parallel in einer zweiten unabhaengigen
Produktionsinstanz betreiben.

## Diagnose

```sh
docker compose logs --tail=100 asn-reservation
```

Logs enthalten Request-ID, Route, HTTP-Status und Zustandswechsel. Zugangsdaten
nicht in Fehlerberichte kopieren. Bei Paperless-Verbindungsfehlern URL, Token,
Dokumentrechte und Erreichbarkeit aus dem Container pruefen.

Ein Fehler mit `/app/app.py` deutet auf einen alten Startbefehl hin: Das aktuelle
Image startet `/opt/asn/app.py`. NAS-Command-Overrides und alte Images pruefen.
Bei Schreibfehlern auf `/data` Volume und Rechte kontrollieren; nicht pauschal
Root-Betrieb oder `chmod 777` aktivieren.

## Entwicklung und Quellcode

Python 3.12 und die gepinnte Waitress-Abhaengigkeit installieren. Vom Service-Ordner:

```sh
python -m pip install --require-hashes --only-binary=:all: -r requirements.txt
python package_source.py --output source.zip
python -m unittest discover -s tests
```

Bei lokalem Betrieb nach jeder Quellcodeaenderung das Archiv erneut erzeugen.
Der Docker-Build erledigt dies automatisch. Es werden nur festgelegte Quelldateien,
Build-Eingaben, Tests, oeffentliche Dokumentation und Lizenzhinweise aufgenommen,
keine `.env` oder Datenbank. Lizenztexte stehen in `licenses/`.
