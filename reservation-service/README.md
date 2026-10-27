# Reservation-Service

## Schnellstart mit fertigem Docker-Image

Kein Git-Clone, lokaler Build oder Download des ZIP-Releasepakets erforderlich.
Docker lädt das Image direkt aus GHCR. Für diesen Installationsweg brauchst du
zwei kleine Dateien im selben dauerhaften Projektordner:

- [docker-compose.registry.yml herunterladen](https://raw.githubusercontent.com/NightmindOfficial/asn-labeler/server-v2.2.0/reservation-service/docker-compose.registry.yml)
- [.env.example herunterladen](https://raw.githubusercontent.com/NightmindOfficial/asn-labeler/server-v2.2.0/reservation-service/.env.example), anschließend in `.env` umbenennen.

Die Vorlagen stammen aus Server 2.2.0; der darin verwendete Image-Tag `stable`
folgt neueren stabilen Server-Releases. Vor Updates deren Änderungshinweise prüfen.

1. In `.env` Paperless-Adresse, Paperless-Token sowie eigene, unterschiedliche
   Client- und Admin-Schlüssel eintragen. Siehe Variablentabelle unter Einrichtung.
2. In der Docker-Projektverwaltung die Registry-Compose-Datei verwenden und
   sicherstellen, dass `.env` im Projektordner liegt. Alternativ dort ausführen:

   ```sh
   docker compose -f docker-compose.registry.yml up -d
   ```

3. Zugriff über einen HTTPS-Reverse-Proxy/Tunnel einrichten. Der veröffentlichte
   Port ist standardmäßig nur am Docker-Host erreichbar. Für direkten LAN-Zugriff
   `HOST_BIND_IP` auf dessen LAN-IP setzen; dies aktiviert selbst kein HTTPS.
4. `/health` prüfen, dann `/admin/` öffnen und mit dem Admin-Schlüssel anmelden.

Für manuelle Updates zuerst Druckaufträge beenden, Clients schließen und ein
Backup erstellen. Dann die Update-Funktion der Docker-Verwaltung nutzen oder:

```sh
docker compose -f docker-compose.registry.yml pull
docker compose -f docker-compose.registry.yml up -d
```

Projektname und Datenvolume beibehalten, niemals `down -v` ausführen. Diese
manuellen Befehle übernehmen nicht die Druckruhe- und Backup-Prüfungen unseres
optionalen Host-Updaters. Das Starten per Compose aktiviert keine automatische
Installation zukünftiger Updates.

### Warum ASN_SERVER_IMAGE?

`${ASN_SERVER_IMAGE:-ghcr.io/nightmindofficial/asn-reservation:stable}` verwendet
`stable`, solange keine nichtleere Überschreibung gesetzt ist. Der optionale
Host-Updater setzt diese Variable vorübergehend auf den zuvor geprüften
Image-Digest, damit er genau dieses Image startet.

Wer ausschließlich über seine Docker-Oberfläche manuell aktualisiert, kann
stattdessen eine feste Zeile verwenden:

```yaml
image: ghcr.io/nightmindofficial/asn-reservation:stable
```

Diese vereinfachte Variante nicht mit `update-server.sh` kombinieren: Das Skript
benötigt die Variable für die Digest-Bindung. Eine zusätzliche Registrierung von
GHCR als Suchquelle ist für Docker Compose nicht nötig, da die Registry bereits
im Image-Namen steht.

## Updates ab 2.1

Ab 2.1.1 gilt mindestens Client 2.1.1. Den Installer zuerst manuell aktualisieren,
danach den Service. Eine zusätzliche signierte Sicherheitsrichtlinie unter
`security/policy.json` im offiziellen GitHub-Repository kann Mindestversionen
unabhängig von einem neuen Release erhöhen. Der Service prüft beim Start und
stündlich. Alte Server vor 2.1.1 kennen diese Richtlinie noch nicht.
Erforderliche Releases müssen veröffentlicht sein, bevor eine neue zentrale
Richtlinie aktiviert wird. Gespeicherte Sperren werden bei Ausfällen nicht gelöscht.
Die eingebettete Mindest-Clientversion bleibt eine zusätzliche Untergrenze.

standardmäßig gibt es nur Hinweise im Adminpanel. Stündlich wird ohne
API-Schlüssel oder Dokumentdaten die offizielle GitHub-Release-Richtlinie gelesen.
Nur nachweislich unsichere Versionen sperren neue Reservierungen, Druckstarts und
Sekundärlabel-Starts. Bestätigungen und sichere Freigaben bleiben möglich.
Ein unbekannter Stand oder GitHub-Ausfall erzeugt keine Sperre; eine bereits
bekannte Sperre bleibt jedoch auch nach Neustart und Netzausfall erhalten.

Für fertige Docker-Images die `docker-compose.registry.yml` im **bestehenden**
Compose-Projekt verwenden. Projektname, `.env`, Datenvolume und Netz bleiben gleich.
Der Standardtag `stable` folgt stabilen Server-Releases. Beim ersten GHCR-Release
muss der Herausgeber das Paket einmal auf Public stellen. Plattform: linux/amd64.

Optionale automatische Installation auf dem Docker-Host (nicht im Service-Container):

```sh
# Nur pruefen und Image herunterladen, noch nicht installieren:
sh update-server.sh DEIN_BISHERIGER_COMPOSE_PROJEKTNAME
# Ausdruecklich installieren:
sh update-server.sh DEIN_BISHERIGER_COMPOSE_PROJEKTNAME --apply
```

Voraussetzungen: Docker Compose v2 und `flock` auf dem Host, Service mindestens
2.1 installiert. In einem Aufgabenplaner (z. B. cron) kann der zweite Befehl regelmäßig laufen;
ohne diese eigene Aktivierung gibt es keine automatische Installation.
Den echten Projektnamen aus der Docker-UI übernehmen, keinen neuen erfinden.
Das Skript verweigert unbekannte Projekte und Downgrades, pinnt den geladenen
Image-Digest und setzt vor dem Austausch eine 30-minütige Wartungssperre.
Unbestätigte Drucke (auch unklare) oder Sekundärlabels verschieben das Update.
Vor dem Austausch liegt ein SQLite-Backup unter `/data/backups`. Backups nach
Bedarf archivieren; sie werden nicht automatisch gelöscht. Der Service bekommt
keinen Docker-Socket. Bei Fehlern Logs prüfen, niemals automatisch eine alte
Datenbank zurückspielen: Zwischenzeitliche Labels könnten sonst doppelt werden.

Bei einem unbestätigten Sekundärlabel, das nicht mehr aufgelöst werden kann,
Clients und Drucker manuell stoppen und das Update über Docker Compose durchführen.
Dabei die Datenbank behalten. Das automatische Skript umgeht diese Sperre nicht.

Der Dienst koordiniert ASN-Nummern für eine Paperless-Instanz. SQLite speichert
Reservierungen, Druckzustände, Sekundärlabel-Aufträge und den Änderungsverlauf.
Das Adminpanel ist unter `/admin/` erreichbar. Version und API-Fähigkeiten liefert
`/health`. Der Dienst lädt keine Dokumente zu Paperless hoch.

## Voraussetzungen

Ab Service 2.0 wird Client 2.0 oder neuer benötigt. Zuerst den Client per Setup
installieren, dann den Service aktualisieren. Die Versionssperre gilt für neue
Clientaktionen; bestehende eigene Druckaufträge dürfen noch bestätigt werden.
Adminpanel und administrative Werkzeuge bleiben mit separatem Admin-Key erreichbar.
Die Mindest-Clientversion ist ab 2.1 außchließlich im Server-Release festgelegt.
Eine alte `MINIMUM_CLIENT_VERSION`-Variable wird ignoriert. Es wird nicht pauschal
die neueste GitHub-Version erzwungen.

- Docker mit Compose auf einem geeigneten Host, lokal oder in der Cloud.
- Eine erreichbare paperless-ngx-Instanz und ein API-Token mit Leserechten auf
  alle Dokumente des verwendeten ASN-Nummernkreises.
- Einen eigenen langen Client-Schlüssel und einen davon verschiedenen
  Admin-Schlüssel. Beide sind Zugangsdaten und dürfen nicht veröffentlicht werden.

## Einrichtung mit lokalem Build

Dieser alternative Weg benötigt die Service-Quellen. Für die normale Installation
mit fertigem Image genügt der Schnellstart oben.

1. Dieses Service-Verzeichnis oder das vollständige Server-Releasepaket in einen
   dauerhaften Projektordner kopieren. `web/`, `licenses/`, `tests/` und alle
   Build-Dateien werden benötigt. Keine Verzeichnisse auf `/opt/asn` mounten.
2. `.env.example` als `.env` kopieren und die Platzhalter ersetzen:

   | Variable | Bedeutung |
   | --- | --- |
   | `PAPERLESS_URL` | Vom Container aus erreichbare Paperless-Adresse |
   | `PAPERLESS_TOKEN` | Paperless-API-Token |
   | `ASN_PREFIX` | Barcode-Präfix, muss der Paperless-Konfiguration entsprechen |
   | `ASN_PADDING` | Mindestanzahl der Ziffern; Standard 5 |
   | `API_KEYS` | Kommagetrennte Client-Schlüssel, mindestens 32 Zeichen je Schlüssel |
   | `ADMIN_API_KEYS` | Separate Admin-Schlüssel, mindestens 32 Zeichen je Schlüssel |
   | `RESERVATION_TTL_SECONDS` | Lebensdauer nie gestarteter Reservierungen; Standard 300, Bereich 30 bis 3600 |
   | `PRINTING_SETTLE_SECONDS` | Wartezeit vor Anzeige eines unbestätigten Drucks als unklar; Standard 30, mindestens 10 |
   | `SQLITE_PATH` | Datenbank im persistenten Volume, Standard `/data/reservations.sqlite3` |
   | `HOST_BIND_IP` | Hostadresse für Port 8765; Standard `127.0.0.1` |

   `localhost` in `PAPERLESS_URL` bezeichnet den Service-Container, nicht den Host
   oder einen anderen Container. Zufallsschlüssel separat erzeugen, beispielsweise
   mit `python -c "import secrets; print(secrets.token_urlsafe(32))"`.
3. In der Docker-Projektverwaltung ein Compose-Projekt mit der mitgelieferten Datei anlegen
   und bauen/starten. Alternativ im Service-Ordner:

   ```sh
   docker compose up -d --build
   ```

4. `/health` über die konfigurierte Adresse aufrufen. Erwartet werden `ok: true`
   und die installierte Versionsnummer. Anschließend `/admin/` öffnen,
   mit einem Admin-Schlüssel anmelden und die Paperless-Verbindung prüfen.
5. Im Windows-Client dieselbe Basisadresse und einen Client-Schlüssel eintragen.
   Demo-Modus ausschalten. Für einen HTTPS-Tunnel keine interne Portnummer anhängen.

Das benannte Datenvolume `asn-reservation-data` bleibt bei Container-Neustarts
erhalten. Projektname und Volume sind Teil der Installation und dürfen beim
Update nicht versehentlich gewechselt werden.

## Netzwerk und Zugriff

Das Deployment-Paket heißt ab 2.1.1 `asn-reservation-<Version>.zip`.
Beim Übernehmen der Compose-Dateien aus 2.1.1 den bisherigen Bind-Adressenschlüssel
in `.env` in `HOST_BIND_IP` umbenennen und seinen Wert beibehalten. Ohne diesen
Schlüssel gilt `127.0.0.1`; direkter LAN-Zugriff ist dann nicht verfügbar.
Ein reines Image-Update verändert bestehende Compose-Dateien nicht.

Die Voreinstellung bindet Port 8765 nur lokal am Host. Für direkten LAN-Zugriff
`HOST_BIND_IP` auf die konkrete LAN-IP des Hosts setzen; der direkte Port bietet
selbst kein TLS. Für öffentlichen Zugriff einen HTTPS-Reverse-Proxy verwenden.

Ein Cloudflare-Tunnel kann intern an `http://asn-reservation:8765` weiterleiten,
wenn sein Container am Standardnetzwerk dieses Compose-Projekts angeschlossen
ist. Die Service-Compose benötigt dafür kein externes Netzwerk. Im Client steht
dann beispielsweise `https://asn.example.com`, ohne `:8765`.

Client- und Admin-Endpunkte verwenden Bearer-Schlüssel. Fehlende Admin-Schlüssel
deaktivieren Admin-Funktionen. Die öffentlichen Pfade `/health`, `/admin/` und
die statischen Panel-/Lizenz-/Quellcode-Dateien benötigen keinen Schlüssel;
sie enthalten keine Dokumentdaten. `/health` liefert nur Erreichbarkeit,
Version, Zeit und den Kompatibilitätsvertrag für Clients. Ausführliche
Update- und Sicherheitsdiagnosen erfordern einen Admin-Schlüssel.
Lizenzen, Drittanbieterhinweise und Changelog öffnen sich über den Footer in
Dialogen. Der Service-Quellcode bleibt im Lizenzdialog herunterladbar.

## Administration und Updates

- Dokumente und Reservierungen werden getrennt angezeigt, jeweils nach ASN
  absteigend. Die Dokumentliste umfasst die vom Dienst verwalteten Reservierungen,
  nicht alle unabhängig davon in Paperless angelegten Dokumente.
- Aktualisierungsintervall und Wartungsaktionen stehen in den Einstellungen.
  Der dortige Schalter **Entwicklermodus** blendet das Service-Protokoll ein.
  Die Einstellung wird im Browser gespeichert; standardmäßig bleibt das
  Protokoll ausgeblendet. Einzelne Ereignisse lassen sich über ihr Kopier-Icon
  für die Fehleranalyse übernehmen. Dafür ist HTTPS oder localhost erforderlich.
  Der Änderungsverlauf zeigt die neuesten Einträge zuerst und lädt weitere
  bei Bedarf. Sekundärlabels werden als bestätigte Kopien gezählt.
- Vor Updates ein Backup herunterladen, aktive Druckaufträge beenden und die
  Release Notes lesen. Paket im bestehenden Projektordner ersetzen; `.env` und
  Datenvolume behalten. Das Projekt neu bauen/bereitstellen, nicht nur neu starten.
- Danach `/health`, Adminpanel und Client prüfen. Ein altes Panel nach einem
  Versionswechsel neu laden. Bei Schemaänderungen ist ein altes Image allein
  kein geeigneter Rollback; eine Backup-Wiederherstellung muss alle inzwischen
  physisch gedruckten Labels berücksichtigen.

Ein Abgleich gibt gedruckte oder unklare ASNs nicht pauschal frei. Der gespeicherte
Paperless-Höchststand sinkt auch nach Löschung eines Paperless-Dokuments nicht
automatisch. Korrekturen und Freigaben erfordern die vorgesehenen bestätigten
Admin-Aktionen und können durch geschützte Nummern verhindert werden.

Niemals `docker compose down -v` zur Fehlerbehebung verwenden. Eine alte Datenbank
oder ein neues leeres Volume kann bereits gedruckte Labels vergessen und dadurch
Doppelvergaben verursachen. Backups nicht parallel in einer zweiten unabhängigen
Produktionsinstanz betreiben.

## Diagnose

```sh
docker compose logs --tail=100 asn-reservation
```

Logs enthalten Request-ID, Route, HTTP-Status und Zustandswechsel. Zugangsdaten
nicht in Fehlerberichte kopieren. Bei Paperless-Verbindungsfehlern URL, Token,
Dokumentrechte und Erreichbarkeit aus dem Container prüfen.

Ein Fehler mit `/app/app.py` deutet auf einen alten Startbefehl hin: Das aktuelle
Image startet `/opt/asn/app.py`. Command-Overrides und alte Images prüfen.
Bei Schreibfehlern auf `/data` Volume und Rechte kontrollieren; nicht pauschal
Root-Betrieb oder `chmod 777` aktivieren.

## Entwicklung und Quellcode

Python 3.12 und die gepinnte Waitress-Abhängigkeit installieren. Vom Service-Ordner:

```sh
python -m pip install --require-hashes --only-binary=:all: -r requirements.txt
python package_source.py --output source.zip
python -m unittest discover -s tests
```

Bei lokalem Betrieb nach jeder Qüllcodeänderung das Archiv erneut erzeugen.
Der Docker-Build erledigt dies automatisch. Es werden nur festgelegte Quelldateien,
Build-Eingaben, Tests, öffentliche Dokumentation und Lizenzhinweise aufgenommen,
keine `.env` oder Datenbank. Lizenztexte stehen in `licenses/`.
