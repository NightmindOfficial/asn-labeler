# Updates und Releases

## Voraussetzungen

Client und Service werden aus einem gemeinsam geprueften Quellcodestand gebaut.
`.github/workflows/ci.yml` prueft Pull Requests vor dem Merge und `main` nach dem
Merge. Eine noch nicht veroeffentlichte Version wird anschliessend automatisch
als GitHub Release bereitgestellt. Bestehende Installationen aktualisieren sich
dadurch nicht selbst. Ein Docker-Image wird noch nicht auf GHCR veroeffentlicht.

## Einmalige GitHub-Einrichtung

1. Projekt und Workflow auf einem Entwicklungsbranch pushen, Pull Request gegen
   `main` erstellen. Die Checks einmal vollstaendig laufen lassen. Beim ersten
   Import entstehen keine weiteren Voraussetzungen durch fehlende alte Tags.
2. Unter Settings > Actions > General GitHub Actions erlauben. Die verwendeten
   offiziellen Actions muessen erlaubt sein. Der Workflow setzt Leserechte als
   Standard und benoetigt nur im Publish-Job `contents: write`. Keine persoenlichen
   Tokens oder Brother-Komponenten als Secrets hinterlegen. Eine uebergeordnete
   Organisationsrichtlinie darf diese Schreibberechtigung nicht verbieten.
3. Unter Settings > Rules > Rulesets einen aktiven Branch-Ruleset fuer `main`
   einrichten: Pull Request erforderlich, Statuscheck **CI gate** erforderlich,
   Branch muss vor dem Merge aktuell sein, Force-Pushes und Loeschen blockieren.
   Als Check-Quelle GitHub Actions waehlen. Keine Bypass-Ausnahme fuer den normalen
   Arbeitsablauf; bei Einzelentwicklung keine zweite Genehmigung verlangen.
4. Der Check erscheint nach seinem ersten Lauf in der Auswahl. Ohne aktiven
   Ruleset laufen Tests zwar, verhindern aber nicht technisch einen Merge.
5. Ersten gruenen Pull Request mergen. Ist `v1.4.3` noch nicht veroeffentlicht,
   erstellt dieser erste Import nach den `main`-Tests automatisch Release 1.4.3.
   Die Workflows muessen danach im Actions-Tab als erfolgreich bestaetigt werden.

Die Repository-Regeln werden nicht durch die YAML-Datei selbst aktiviert.

## Aenderungen entwickeln

1. Aktuellen Stand holen (z. B. Fetch/Pull in GitHub Desktop).
2. Fuer die Aenderung einen Branch erstellen. Code und zugehoerige Tests anpassen.
3. Client-Selbsttest, Service-Tests und Release-Tests aus der README ausfuehren.
   Bei Druckaenderungen zusaetzlich echte Hardware testen; Simulation ersetzt das nicht.
4. Aenderungen in GitHub Desktop pruefen, committen und pushen; per Pull Request
   nach `main` uebernehmen. Keine `.env`, Logs oder Datenbanken committen.

## Eine Version veroeffentlichen

1. In `reservation-service/VERSION` eine neue Version als `X.Y.Z` eintragen,
   beispielsweise `1.4.4`. Nur diese Datei bestimmt die Client-/Server-/Paketversion.
   Keine fuehrenden Nullen oder Vorabversionssuffixe; jeder Teil maximal 65534.
2. In `CHANGELOG.md` genau einen Abschnitt `## X.Y.Z` mit den Release Notes
   ergaenzen. Migrationen, Kompatibilitaet und Update-Reihenfolge dokumentieren.
3. Branch pushen und Pull Request erstellen. GitHub prueft Metadaten, Service,
   Windows-Build/Selbsttests, Paketgrenzen, Docker-Build und Containerstart.
   **CI gate** wird nur gruen, wenn alle vier Jobs erfolgreich sind.
4. Nach erfolgreichen Checks und gegebenenfalls Hardwaretests mergen. Auf `main`
   laufen dieselben Pruefungen noch einmal. Danach werden Tag `vX.Y.Z`, Release
   Notes und Downloads automatisch erstellt. Keine manuelle Tag-Erstellung noetig.

Ein Merge ohne neue Versionsnummer fuehrt bei einer bereits veroeffentlichten
Version zu keinem neuen Release. Alte Releases werden niemals ueberschrieben.
Pull Requests erhalten keine Release-Schreibrechte und veroeffentlichen nichts.

## Pakete und lokale Vorschau

Ein Release enthaelt die EXE, NAS- und vollstaendiges Quellcode-ZIP sowie
`release-manifest.json` und `SHA256SUMS.txt`. Das Manifest bindet die Downloads
an Version und geprueften Git-Commit. Die Windows-EXE ist weiterhin unsigniert;
Pruefsummen ersetzen keine Codesignatur.

Fuer einen lokalen Paketbuild nach dem normalen Windows-Build:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File windows-app/tools/fetch-zxing-source.ps1
   python package-release.py
   ```

Die festgelegte Pruefsumme wird kontrolliert. Das Archiv bleibt Git-ignoriert.
Pakete liegen unter `artifacts/X.Y.Z/`; die EXE auch unter `windows-app/dist/`.
Ohne GitHub Actions enthaelt das Manifest keinen bestaetigten CI-Commit und ist
als lokaler Build zu behandeln. `python tools/verify_artifacts.py artifacts/X.Y.Z`
prueft die lokalen Archive. Der Publisher laesst nur einen geprueften `main`-Lauf zu.

## Fehler und Wiederholung

- Fehlende/fehlerhafte Tests blockieren **CI gate**. Bei Fehlern nach dem Merge
  gibt es keinen neuen Release; der Merge selbst ist dann bereits erfolgt.
- Ein fehlgeschlagener Upload bleibt als Entwurf unsichtbar fuer normale
  Downloadnutzer. Im Actions-Tab den Lauf wiederholen. Beim gleichen Commit
  werden nur dessen Entwurfs-Uploads ersetzt, nie veroeffentlichte Assets.
- Ein Tag oder Entwurf fuer einen anderen Commit wird nicht automatisch
  umgeschrieben. Konflikt pruefen und vorzugsweise eine neue Version vergeben.
  Einen fehlgeschlagenen eigenen Entwurf nur bewusst entfernen, nicht einen
  bereits veroeffentlichten Release ersetzen.
- Wenn `main` inzwischen weitergelaufen ist, veroeffentlicht ein alter Lauf nicht
  nachtraeglich einen ueberholten Stand. In Actions kann **Run workflow** auf `main`
  alle Pruefungen erneut ausloesen, sobald der Workflow auf `main` liegt.
- Windows-Layouttests laufen ohne angeschlossenen Brother. Falls die Runner-
  Umgebung abweicht, den Test untersuchen statt ihn stillschweigend zu ueberspringen.

## Installationen aktualisieren (heute)

- Client: App schliessen, neue EXE beziehen und bisherige ersetzen. Benutzer-
  einstellungen bleiben im Benutzerprofil; trotzdem Release Notes beachten.
- NAS: Datenbankbackup erstellen, laufende Druckauftraege beenden, neues NAS-Paket
  ins bestehende Projekt entpacken. `.env`, Projektname und Datenvolume behalten.
  In UGREEN das bestehende Projekt neu bauen und bereitstellen. Nicht nur den
  alten Container neu starten. `/health`, Adminpanel und Client danach pruefen.
- Ein gemeinsamer Release bedeutet nicht automatisch, dass alle aelteren Clients
  kompatibel sind. API-Version/Capabilities und Release Notes sind massgeblich.

## Inhalt der Pakete

Oeffentliche Dokumentation wird ueber `PUBLIC_DOCUMENTS` in `package-release.py`
festgelegt. Neue Dokumente muessen dort bewusst aufgenommen werden. Der Service
verwendet eine eigene Dateiliste in `reservation-service/package_source.py`.
Lokale Notizen, Zugangsdaten, Laufzeitdaten und Build-Verzeichnisse gehoeren nicht
in Quellcodepakete. `.gitignore` ersetzt diese Paketpruefung nicht.

Die Paketgrenzen koennen separat getestet werden:

```powershell
python -m unittest discover -s windows-app/tests -p test_release_packaging.py
python -m unittest discover -s tools/tests
```

Referenzen:
- https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository
- https://docs.github.com/en/actions/tutorials/publish-packages/publish-docker-images
