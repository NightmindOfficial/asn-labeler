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

## Einmalige GitHub-Einrichtung

1. Entwicklungsbranch pushen und PR gegen `main` oeffnen. Checks laufen lassen.
2. Settings > Actions > General: GitHub Actions und die verwendeten offiziellen
   Actions erlauben. Nur Publish benoetigt `contents: write`. Keine persoenlichen
   Tokens oder Brother-Komponenten als Secrets erforderlich.
3. Settings > Rules > Rulesets: aktiven Branch-Ruleset fuer `main` anlegen.
   Pull Request und Statuscheck **CI gate** verlangen, Branch-Aktualitaet vor dem
   Merge verlangen, Force-Pushes und Loeschen blockieren. Quelle: GitHub Actions.
   Keine regulaere Bypass-Ausnahme; allein keine zweite Genehmigung verlangen.
4. Der Check ist nach dem ersten Lauf auswaehlbar. Ohne Ruleset verhindern Tests
   keinen Merge technisch. GitHub-Runner und Docker-Start erstmals pruefen.
5. Ersten gruenen PR mergen. Der Import veroeffentlicht `client-v1.4.3` und
   `server-v1.4.3`, sofern noch nicht vorhanden. Alte Tags wie `v1.4.3` bleiben
   unangetastet und gehoeren nicht zu den neuen getrennten Release-Reihen.

## Normaler Ablauf

1. `main` aktualisieren, Entwicklungsbranch anlegen, implementieren und lokal testen.
2. VERSION und CHANGELOG nur der geaenderten Komponenten aktualisieren.
3. Committen, pushen und einen PR erstellen. Keine Laufzeitdaten committen.
4. GitHub prueft Metadaten, Service-Tests, Windows-Build/Selbsttests, Paketgrenzen
   und Docker-Start. Beide Komponenten werden weiterhin getestet und gebaut.
5. Nach gruenen Checks und erforderlichen Hardwaretests mergen.
6. Auf `main` wiederholt GitHub die Pruefungen. Nur Komponenten mit ausstehender
   Veroeffentlichung erhalten Release-Pakete und einen eigenen GitHub Release.

Nur Client geaendert: `client-v1.5.0`. Nur Server geaendert: `server-v1.4.4`.
Beide geaendert: zwei Releases aus einem Commit, ohne zweiten PR oder manuelle Tags.

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
Latest-Markierung fuer beide Produkte verwenden. Kuenftige Updater muessen nach
`client-v` bzw. `server-v` filtern und Entwuerfe/Vorabversionen ausschliessen.

## Lokale Builds

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File windows-app/build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File windows-app/tools/fetch-zxing-source.ps1
python package-release.py client
python tools/verify_artifacts.py client artifacts/client/1.4.3

python package-release.py server
python tools/verify_artifacts.py server artifacts/server/1.4.3
```

Pfade an die jeweilige Version anpassen. Server-Pakete benoetigen weder EXE noch
ZXing-Download. Client-Releases enthalten EXE und vollstaendiges Quellcode-ZIP;
Server-Releases das NAS-ZIP inklusive Service-Quellcode. Beide enthalten
`release-manifest.json` und `SHA256SUMS.txt`. Die EXE bleibt unsigniert.
Lokale Manifeste ohne GitHub Actions enthalten keinen bestaetigten CI-Commit.

```powershell
python -m unittest discover -s tools/tests
python -m unittest discover -s windows-app/tests -p test_release_packaging.py
```

## Kompatibilitaet und Updates

Produktversionen sind keine API-Versionen. Server 2.x und Client 1.x koennen
kompatibel sein. Massgeblich sind `apiVersion`, benoetigte `capabilities` und eine
gegebenenfalls ausdruecklich gemeldete `minimumClientVersion` in `/health`.
Aktuell unterstuetzt der Client API 1 und alte additive Health-Antworten ohne
API-Feld; unbekannte APIs werden abgewiesen. Sekundaerlabels und sicherer Abgleich
erfordern passende Faehigkeiten. Produkt-Major-Versionen werden nicht verglichen.
Eine neue inkompatible API braucht eine bewusst implementierte Migration;
eine neue Produkt-VERSION allein aendert das API-Protokoll nicht.

Adminpanel und Service gehoeren zusammen: Veraltete Adminseiten muessen nach
Server-Updates neu geladen werden. Das ist keine Client-Versionssperre.

Installationen aktualisieren sich nicht automatisch. Client schliessen und EXE
ersetzen. NAS: Backup, Druckauftraege beenden, NAS-Paket entpacken, `.env`,
Projektname und Volume behalten; UGREEN-Projekt neu bauen und bereitstellen.
Danach `/health`, Adminpanel und Client pruefen. Noch kein Docker-Image auf GHCR.
Migrationen und Kompatibilitaet in Release Notes beachten; Hardwaretests bleiben lokal.
