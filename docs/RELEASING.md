# Updates und Releases

## Unabhängige Versionen

- Client: `windows-app/VERSION` und `windows-app/CHANGELOG.md`.
- Server inklusive Adminpanel: `reservation-service/VERSION` und `reservation-service/CHANGELOG.md`.
- Format: `X.Y.Z`, ohne führende Nullen; jeder Teil maximal 65534.
- Patch: Fehlerkorrektur. Minor: kompatible Erweiterung. Major: inkompatible
  Änderung am zugesicherten Vertrag der jeweiligen Komponente.
- Keine Gesamtversion. Das root-CHANGELOG bleibt als historische Zusammenfassung.

Ein Branch und ein Pull Request können eine oder beide Komponenten ändern.
Nur betroffene Komponenten erhalten höhere Versionen und Release Notes unter
genau einer nicht leeren Überschrift `## X.Y.Z`.

## Ablauf

1. In GitHub Desktop auf `main` wechseln, Fetch/Pull ausführen und von diesem aktuellen Stand einen neuen Entwicklungsbranch anlegen. Einen bereits gemergten Branch nicht für die nächste Version weiterverwenden. Implementieren und lokal testen.
2. VERSION und CHANGELOG nur der geänderten Komponenten aktualisieren.
3. Committen, pushen und einen PR erstellen. Keine Laufzeitdaten committen.
4. GitHub prüft Metadaten, Service-Tests, Windows-Build/Selbsttests, Paketgrenzen und Docker-Start. Beide Komponenten werden weiterhin getestet und gebaut.
5. Nach grünen Checks und erforderlichen Hardwaretests mergen.
6. Auf `main` wiederholt GitHub die Prüfungen. Nur Komponenten mit ausstehender Veröffentlichung erhalten Release-Pakete und einen eigenen GitHub Release.
7. Unter Actions den erfolgreichen `main`-Lauf inklusive **Publish release** bestätigen. Unter Releases die erwarteten Komponentenversionen und Downloads prüfen. Erst danach die gewünschten Installationen aktualisieren.

PR-Titel und Beschreibung sind frei wählbar. VERSION und CHANGELOG bestimmen die Veröffentlichung. Eine reine Änderung dieser Anleitung braucht keinen Bump.

Nur Client geändert: `client-v1.5.0`. Nur Server geändert: `server-v1.4.4`. Beide geändert: zwei Releases aus einem Commit, ohne zweiten PR oder manuelle Tags.

Die Checks blockieren Versionsrückschritte, Wiederverwendung bereits getaggter
neuer Versionen und Änderungen innerhalb einer Komponente ohne Bump. Tests und
Markdown sind ausgenommen. Gemeinsame Lizenzdateien betreffen beide Komponenten.
Reine CI-/Release-Werkzeug- oder Dokumentationsänderungen dürfen ohne Release
gemergt werden. Bei gemeinsamem Build-/Paketcode entscheidet das Review, ob neue
Auslieferungen und damit Versionsanhebungen erforderlich sind.

## Gate und Veröffentlichung

**CI gate** wird nur bei Erfolg aller vier Prüfjobs grün. Der Ruleset erzwingt
dies vor dem Merge. Fehler nach dem Merge verhindern die Veröffentlichung,
nicht rückwirkend den Merge.

Pro PR und für `main` existiert eine Concurrency-Gruppe. Laufende Runs werden
nicht durch neue Pushes abgebrochen; GitHub kann wartende Zwischenstände ersetzen.
Der Publisher prüft vor und nach Upload, ob sein Commit noch `main` ist.

Nur geprüfte Artifacts aus demselben Run werden veröffentlicht. Manifest und
SHA-256 binden sie an Komponente, Version und Commit. Uploads gehen zuerst in einen
Entwurf; erst nach Prüfung wird veröffentlicht. Fehler lassen den Entwurf offen.
Beim gleichen Commit ersetzt eine Wiederholung nur bekannte Entwurfs-Assets.
Veröffentlichte Releases werden nie geändert. Fremde Tags/Entwürfe werden
nicht automatisch umgeschrieben; bei Konflikt bewusst prüfen oder neue Version nutzen.

Zwei Releases sind keine atomare Transaktion: Gelingt einer und der zweite nicht,
Run wiederholen. Der bereits veröffentlichte bleibt unberührt. Keine globale
Latest-Markierung für beide Produkte verwenden. Der Client-Updater filtert nach
`client-v` und schließt Entwürfe/Vorabversionen aus. Ein Server-Updater muss
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

Pfade an die jeweilige Version anpassen. Server-Pakete benötigen weder EXE noch
ZXing-Download. Client-Releases ab 2.0 enthalten Setup-EXE und vollständiges Quellcode-ZIP;
Server-Releases das Deployment-ZIP inklusive Service-Quellcode. Beide enthalten
`release-manifest.json` und `SHA256SUMS.txt`. Die EXE bleibt unsigniert.
Lokale Manifeste ohne GitHub Actions enthalten keinen bestätigten CI-Commit.
GitHub prüft zudem Erstinstallation, erneute Installation, Deinstallation und
die Sperre bei laufender App, jeweils im Benutzer- und Systemmodus.
Der Installer-Test darf lokal nur bewusst mit `-AllowLocalInstallationTest`
gestartet werden; er verweigert vorhandene registrierte Installationen.

```powershell
python -m unittest discover -s tools/tests
python -m unittest discover -s windows-app/tests -p test_release_packaging.py
```

## Kompatibilität und Updates

Produktversionen sind keine API-Versionen. Maßgeblich sind `apiVersion`, benötigte `capabilities` und eine
gegebenenfalls ausdrücklich gemeldete `minimumClientVersion` in `/health`.
Aktuell unterstützt der Client API 1 und alte additive Health-Antworten ohne
API-Feld; unbekannte APIs werden abgewiesen. Sekundärlabels und sicherer Abgleich
erfordern passende Fähigkeiten. Produkt-Major-Versionen werden nicht verglichen.
Eine neue inkompatible API braucht eine bewusst implementierte Migration;
eine neue Produkt-VERSION allein ändert das API-Protokoll nicht.

Ab Server 2.0 wird zusätzlich mindestens Client 2.0 verlangt. Für einen
Sicherheitsrelease `CLIENT_VERSION_FLOOR` im Service anheben. Seit 2.1 ist
dies die einzige Vorgabe; `MINIMUM_CLIENT_VERSION` in `.env` wird ignoriert.
Immer zuerst den benötigten Client
veröffentlichen/installieren, danach die Sperre aktivieren.
Client-API-Anfragen ohne gültige oder mit zu alter `X-ASN-Client-Version`
erhalten HTTP 426. Bestätigungen bereits gestarteter eigener Aufträge und
Freigaben nie gestarteter eigener Reservierungen bleiben möglich. API 1 und
Datenbankschema 3 bleiben gleich. Admin-Schlüssel sind davon ausgenommen,
solange keine Client-Version angegeben wird; nie in Clients hinterlegen.

Client 2.0 erlaubt produktive API-Zugriffe nur aus seiner registrierten
Installation. Der lokale Testbuild kann mit `--development` gestartet werden;
die ausgelieferte EXE enthält diese Ausnahme nicht. Das ist kein kryptographischer
Installationsnachweis: Ein veränderter Client kann Versionsangaben nachahmen.

## Server-Images und Sicherheitsrichtlinie ab 2.1

`reservation-service/release-policy.json` muss dieselbe Version wie die
Server-VERSION enthalten. `minimumSafeServerVersion` nur bei begründetem
Sicherheitsbedarf anheben; ein normaler Release erfordert keine Sperre älterer
Versionen. Die Datei wird als `server-update-policy.json` mitveröffentlicht.
Server ab 2.1 fragen diese offizielle Richtlinie stündlich ab und speichern
den höchsten jemals bekannten Mindeststand dauerhaft. Eine fehlerhafte Anhebung
kann deshalb nicht durch Senken der nächsten Richtlinie rückgängig werden.

CI speichert nach dem Docker-Smoke-Test genau das getestete linux/amd64-Image.
Der Releasejob publiziert es als `ghcr.io/nightmindofficial/asn-reservation:X.Y.Z`;
nach dem veröffentlichten GitHub-Release folgt der bewegliche Tag `stable`.
Versions-Tags werden nicht mit anderen Inhalten überschrieben.
Beim ersten Release muss das GHCR-Paket in GitHub unter Package settings auf
**Public** gestellt werden, damit Installationen es ohne Zugangsdaten laden.
Kein lokaler Test veröffentlicht Images oder Releases.
Zugriffsschutz erfolgt weiterhin über getrennte API-Schlüssel, später Gerätefreigaben.

Adminpanel und Service gehören zusammen: Veraltete Adminseiten müssen nach
Server-Updates neu geladen werden. Das ist keine Client-Versionssperre.

Ab Client 2.0 einmalig Setup installieren. Danach sucht der Client beim Start
nach Updates (ab 2.1.1 bei jedem Start, ohne gespeicherte Einstunden-Sperre);
manuell unter Einstellungen > Über.
Download und Installation erfordern Bestätigung. Während Druckaufträgen wird
kein Update angeboten. Setup startet nach dem Beenden der App und behält den
Installationsmodus sowie Einstellungen/Zugangsdaten. Die Prüfsumme sichert die
Dateiintegrität, ersetzt aber keine Codesignatur. Server-Updates sind standardmäßig
manuell; ab 2.1 ist das externe Update-Skript ausdrücklich aktivierbar.
Backup anlegen, Druckaufträge beenden und Image bzw. Deployment-Paket aktualisieren.
Eigene `.env`, Projektname und Datenvolume behalten; danach `/health`, Adminpanel
und Client prüfen. Details stehen in der Service-Anleitung.
Migrationen und Kompatibilität in Release Notes beachten; Hardwaretests bleiben lokal.

## Konfiguration bei jedem Update

### Sicherheitsrichtlinie ab 2.1.1

`security/policy.json` ist ein signierter Umschlag (RSA/SHA-256) mit Schema,
aufsteigender Revision, Client-/Server-Mindestversion und Begründung. Er wird
unabhängig vom Release über den offiziellen `main`-Branch abgerufen.
Die privaten Signaturschlüssel gehören niemals ins Repository oder in CI-Artefakte.
Den lokalen Schlüssel separat sicher sichern: Ohne ihn lassen sich keine neuen
Richtlinien für bereits installierte Clients und Server signieren.

Neue Richtlinie lokal erstellen, Diff prüfen und separat veröffentlichen:

```powershell
./tools/sign-security-policy.ps1 -Revision 2 -MinimumClient 2.1.1 -MinimumServer 2.0.0 -Reason "Begründung"
```

Revisionen müssen steigen, Mindestversionen dürfen nicht sinken. Nur tatsächlich
betroffene Komponenten sperren. Client und Server prüfen vor der Aktivierung,
ob die verlangten stabilen Releases (beim Client samt Installer) existieren.
Ein unbekannter Stand bleibt erlaubt; eine verifizierte Sperre bleibt auch
offline erhalten. Das ist ein Schutz für unveränderte Installationen, kein DRM:
Wer den Quellcode verändert, kann die Prüfung entfernen.

Für die Einführung zuerst Client 2.1.1 veröffentlichen und installieren, dann
Server 2.1.1. Der neue Server lehnt ältere Clients bereits durch seine eingebaute
Untergrenze ab. Server 2.1.0 und ältere Clients können die neue zentrale
Prüfung nicht rückwirkend erhalten. Bei einem reinen Richtlinien-Update ist
kein Versions-Bump nötig; die erforderlichen korrigierten Releases müssen vorher
bereitstehen. Server/Client werden weiterhin unabhängig versioniert.

Nach erfolgreichem Client-Release ergänzt die Pipeline betroffene alte
Client-Releases um einen Updatehinweis und einen Link zu 2.1.1. Tags,
Quellarchive und Installer bleiben erhalten; es werden keine Assets gelöscht.
Das eigene Source-ZIP bleibt notwendig für die zusätzlich enthaltenen Quellen.

Jeder Release und jede Übergabe benennt ausdrücklich, ob sich `.env` ändert.
Bei Änderungen: betroffene Variablen, alte/neue Defaults, erforderliche Schritte
und Abwärtskompatibilität nennen. Auch geänderte Bedeutung unveränderter
Variablennamen zählt. Ohne Änderung: `.env: unverändert`.

**Service 2.1.0:** `MINIMUM_CLIENT_VERSION` entfällt aus `.env.example` und wird
nicht mehr ausgewertet. Ein vorhandener Eintrag kann entfernt werden; die
Mindestversion ist jetzt im Server-Release eingebettet. Wer bisher einen höheren
eigenen Mindeststand gesetzt hat, verliert diese Überschreibung und muss die
Release-Vorgabe vor dem Update prüfen. Sonstige Werte und Secrets beibehalten.
