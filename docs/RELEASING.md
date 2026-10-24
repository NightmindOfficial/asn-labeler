# Updates und Releases

## Unabhaengige Versionen

- Client: `windows-app/VERSION` und `windows-app/CHANGELOG.md`.
- Server inklusive Adminpanel: `reservation-service/VERSION` und `reservation-service/CHANGELOG.md`.
- Format: `X.Y.Z`, ohne fuehrende Nullen; jeder Teil maximal 65534.
- Patch: Fehlerkorrektur. Minor: kompatible Erweiterung. Major: inkompatible
  Aenderung am zugesicherten Vertrag der jeweiligen Komponente.
- Keine Gesamtversion. Das root-CHANGELOG bleibt als historische Zusammenfassung.

Ein Branch und ein Pull Request koennen eine oder beide Komponenten aendern.
Nur betroffene Komponenten erhalten hoehere Versionen und Release Notes unter
genau einer nicht leeren Ueberschrift `## X.Y.Z`.

## Ablauf

1. In GitHub Desktop auf `main` wechseln, Fetch/Pull ausfuehren und von diesem aktuellen Stand einen neuen Entwicklungsbranch anlegen. Einen bereits gemergten Branch nicht fuer die naechste Version weiterverwenden. Implementieren und lokal testen.
2. VERSION und CHANGELOG nur der geaenderten Komponenten aktualisieren.
3. Committen, pushen und einen PR erstellen. Keine Laufzeitdaten committen.
4. GitHub prueft Metadaten, Service-Tests, Windows-Build/Selbsttests, Paketgrenzen und Docker-Start. Beide Komponenten werden weiterhin getestet und gebaut.
5. Nach gruenen Checks und erforderlichen Hardwaretests mergen.
6. Auf `main` wiederholt GitHub die Pruefungen. Nur Komponenten mit ausstehender Veroeffentlichung erhalten Release-Pakete und einen eigenen GitHub Release.
7. Unter Actions den erfolgreichen `main`-Lauf inklusive **Publish release** bestaetigen. Unter Releases die erwarteten Komponentenversionen und Downloads pruefen. Erst danach die gewuenschten Installationen aktualisieren.

PR-Titel und Beschreibung sind frei waehlbar. VERSION und CHANGELOG bestimmen die Veroeffentlichung. Eine reine Aenderung dieser Anleitung braucht keinen Bump.

Nur Client geaendert: `client-v1.5.0`. Nur Server geaendert: `server-v1.4.4`. Beide geaendert: zwei Releases aus einem Commit, ohne zweiten PR oder manuelle Tags.

Die Checks blockieren Versionsrueckschritte, Wiederverwendung bereits getaggter
neuer Versionen und Aenderungen innerhalb einer Komponente ohne Bump. Tests und
Markdown sind ausgenommen. Gemeinsame Lizenzdateien betreffen beide Komponenten.
Reine CI-/Release-Werkzeug- oder Dokumentationsaenderungen duerfen ohne Release
gemergt werden. Bei gemeinsamem Build-/Paketcode entscheidet das Review, ob neue
Auslieferungen und damit Versionsanhebungen erforderlich sind.

## Gate und Veroeffentlichung

**CI gate** wird nur bei Erfolg aller vier Pruefjobs gruen. Der Ruleset erzwingt
dies vor dem Merge. Fehler nach dem Merge verhindern die Veroeffentlichung,
nicht rueckwirkend den Merge.

Pro PR und fuer `main` existiert eine Concurrency-Gruppe. Laufende Runs werden
nicht durch neue Pushes abgebrochen; GitHub kann wartende Zwischenstaende ersetzen.
Der Publisher prueft vor und nach Upload, ob sein Commit noch `main` ist.

Nur gepruefte Artifacts aus demselben Run werden veroeffentlicht. Manifest und
SHA-256 binden sie an Komponente, Version und Commit. Uploads gehen zuerst in einen
Entwurf; erst nach Pruefung wird veroeffentlicht. Fehler lassen den Entwurf offen.
Beim gleichen Commit ersetzt eine Wiederholung nur bekannte Entwurfs-Assets.
Veroeffentlichte Releases werden nie geaendert. Fremde Tags/Entwuerfe werden
nicht automatisch umgeschrieben; bei Konflikt bewusst pruefen oder neue Version nutzen.

Zwei Releases sind keine atomare Transaktion: Gelingt einer und der zweite nicht,
Run wiederholen. Der bereits veroeffentlichte bleibt unberuehrt. Keine globale
Latest-Markierung fuer beide Produkte verwenden. Der Client-Updater filtert nach
`client-v` und schliesst Entwuerfe/Vorabversionen aus. Ein Server-Updater muss
entsprechend nach `server-v` filtern.

## Lokale Builds

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File windows-app/build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File windows-app/tools/fetch-zxing-source.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File windows-app/tools/setup-inno.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File windows-app/build-installer.ps1
python package-release.py client
python tools/verify_artifacts.py client artifacts/client/2.0.0

python package-release.py server
python tools/verify_artifacts.py server artifacts/server/2.0.0
```

Lokaler interaktiver Client-Test ohne Installation:

```powershell
./windows-app/dist/asn-labeler-test.exe --development
```

Pfade an die jeweilige Version anpassen. Server-Pakete benoetigen weder EXE noch
ZXing-Download. Client-Releases ab 2.0 enthalten Setup-EXE und vollstaendiges Quellcode-ZIP;
Server-Releases das NAS-ZIP inklusive Service-Quellcode. Beide enthalten
`release-manifest.json` und `SHA256SUMS.txt`. Die EXE bleibt unsigniert.
Lokale Manifeste ohne GitHub Actions enthalten keinen bestaetigten CI-Commit.
GitHub prueft zudem Erstinstallation, erneute Installation, Deinstallation und
die Sperre bei laufender App, jeweils im Benutzer- und Systemmodus.
Der Installer-Test darf lokal nur bewusst mit `-AllowLocalInstallationTest`
gestartet werden; er verweigert vorhandene registrierte Installationen.

```powershell
python -m unittest discover -s tools/tests
python -m unittest discover -s windows-app/tests -p test_release_packaging.py
```

## Kompatibilitaet und Updates

Produktversionen sind keine API-Versionen. Massgeblich sind `apiVersion`, benoetigte `capabilities` und eine
gegebenenfalls ausdruecklich gemeldete `minimumClientVersion` in `/health`.
Aktuell unterstuetzt der Client API 1 und alte additive Health-Antworten ohne
API-Feld; unbekannte APIs werden abgewiesen. Sekundaerlabels und sicherer Abgleich
erfordern passende Faehigkeiten. Produkt-Major-Versionen werden nicht verglichen.
Eine neue inkompatible API braucht eine bewusst implementierte Migration;
eine neue Produkt-VERSION allein aendert das API-Protokoll nicht.

Ab Server 2.0 wird zusaetzlich mindestens Client 2.0 verlangt. Fuer einen
Sicherheitsrelease `CLIENT_VERSION_FLOOR` im Service anheben. Seit 2.1 ist
dies die einzige Vorgabe; `MINIMUM_CLIENT_VERSION` in `.env` wird ignoriert.
Immer zuerst den benoetigten Client
veroeffentlichen/installieren, danach die Sperre aktivieren.
Client-API-Anfragen ohne gueltige oder mit zu alter `X-ASN-Client-Version`
erhalten HTTP 426. Bestaetigungen bereits gestarteter eigener Auftraege und
Freigaben nie gestarteter eigener Reservierungen bleiben moeglich. API 1 und
Datenbankschema 3 bleiben gleich. Admin-Schluessel sind davon ausgenommen,
solange keine Client-Version angegeben wird; nie in Clients hinterlegen.

Client 2.0 erlaubt produktive API-Zugriffe nur aus seiner registrierten
Installation. Der lokale Testbuild kann mit `--development` gestartet werden;
die ausgelieferte EXE enthaelt diese Ausnahme nicht. Das ist kein kryptographischer
Installationsnachweis: Ein veraenderter Client kann Versionsangaben nachahmen.

## Server-Images und Sicherheitsrichtlinie ab 2.1

`reservation-service/release-policy.json` muss dieselbe Version wie die
Server-VERSION enthalten. `minimumSafeServerVersion` nur bei begruendetem
Sicherheitsbedarf anheben; ein normaler Release erfordert keine Sperre aelterer
Versionen. Die Datei wird als `server-update-policy.json` mitveroeffentlicht.
Server ab 2.1 fragen diese offizielle Richtlinie stuendlich ab und speichern
den hoechsten jemals bekannten Mindeststand dauerhaft. Eine fehlerhafte Anhebung
kann deshalb nicht durch Senken der naechsten Richtlinie rueckgaengig werden.

CI speichert nach dem Docker-Smoke-Test genau das getestete linux/amd64-Image.
Der Releasejob publiziert es als `ghcr.io/nightmindofficial/asn-reservation:X.Y.Z`;
nach dem veroeffentlichten GitHub-Release folgt der bewegliche Tag `stable`.
Versions-Tags werden nicht mit anderen Inhalten ueberschrieben.
Beim ersten Release muss das GHCR-Paket in GitHub unter Package settings auf
**Public** gestellt werden, damit NAS-Installationen es ohne Zugangsdaten laden.
Kein lokaler Test veroeffentlicht Images oder Releases.
Zugriffsschutz erfolgt weiterhin ueber getrennte API-Schluessel, spaeter Geraetefreigaben.

Adminpanel und Service gehoeren zusammen: Veraltete Adminseiten muessen nach
Server-Updates neu geladen werden. Das ist keine Client-Versionssperre.

Ab Client 2.0 einmalig Setup installieren. Danach sucht der Client beim Start
nach Updates (maximal einmal pro Stunde); manuell unter Einstellungen > Ueber.
Download und Installation erfordern Bestaetigung. Waehrend Druckauftraegen wird
kein Update angeboten. Setup startet nach dem Beenden der App und behaelt den
Installationsmodus sowie Einstellungen/Zugangsdaten. Die Pruefsumme sichert die
Dateiintegritaet, ersetzt aber keine Codesignatur. Es gibt keine automatische
Server-Aktualisierung. NAS: Backup, Druckauftraege beenden, NAS-Paket entpacken, `.env`,
Projektname und Volume behalten; UGREEN-Projekt neu bauen und bereitstellen.
Danach `/health`, Adminpanel und Client pruefen. Noch kein Docker-Image auf GHCR.
Migrationen und Kompatibilitaet in Release Notes beachten; Hardwaretests bleiben lokal.
