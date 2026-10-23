# ASN Labeler

Windows-Client und selbst hostbarer Reservation-Service fuer ASN-Labels in
paperless-ngx. [Clientversion](windows-app/VERSION) und
[Serverversion](reservation-service/VERSION) werden unabhaengig gepflegt.
Release Notes: [Client](windows-app/CHANGELOG.md), [Server](reservation-service/CHANGELOG.md).

- DataMatrix-Labels fuer Brother QL-800 ueber den separat installierten b-PAC-Client.
- Stanz- und Endlosmedien; eigenes Profil fuer PF-B11221-kompatible Rollen.
- Zentrale ASN-Vergabe, Dokumentenstatus und Web-Administration.
- Deutscher und englischer Client, Sekundaerlabels und Seriendruck.

## Installation

GitHub Actions prueft Pull Requests und veroeffentlicht neue Versionen nach einem
erfolgreichen Merge nach `main`. Der installierte Client sucht beim Start nach
Updates und installiert sie nur nach Bestaetigung. Die Compose-Datei baut das Server-Image lokal; alternativ koennen die
Quelltexte mit den unten beschriebenen Werkzeugen gebaut werden.

1. Service: [Einrichtung und Updates](reservation-service/README.md).
   Das NAS-Paket muss vollstaendig entpackt werden, einschliesslich `web/`,
   `licenses/`, `tests/` und Build-Skripten. Eigene `.env` und Datenvolume behalten.
2. Client: `asn-labeler-2.0.0-setup.exe` aus dem Client-Release starten und fuer
   den aktuellen Benutzer oder alle Benutzer installieren. Brother-Treiber und
   passendes x64-b-PAC separat installieren; die App verlinkt die offizielle Einrichtung.
   Serveradresse und Client-API-Key in den Einstellungen setzen.
3. Paperless: ASN-Barcode-Erkennung aktivieren und DataMatrix-Unterstuetzung
   pruefen, siehe [Bedienung und Dokumentstatus](docs/USAGE.md). Praefix muss zum Service passen.

Es werden keine Brother-Installer oder Treiber mitgeliefert. Die App benoetigt
Windows x64 mit .NET Framework und b-PAC fuer den echten Druck; kein Python.
Updates behalten Einstellungen und gespeicherte Zugangsdaten. Unter
Einstellungen > Ueber kann auch manuell nach Updates gesucht werden.
Setup ist derzeit unsigniert; Windows kann eine SmartScreen-Warnung anzeigen.
QL-810W und QL-820NWB sind nur fuer experimentelle USB-Testdrucke und Diagnose
freigeschaltet, nicht fuer produktive ASN- oder Sekundaerlabel-Auftraege.

## Entwicklung

Alle Befehle vom Repository-Hauptverzeichnis aus ausfuehren.

### Windows-Client

Der Build nutzt den mit Windows/.NET Framework vorhandenen x64-C#-Compiler.
ZXing-DLL und Material-Icons-Schrift sind inklusive Lizenzhinweisen vendort;
der Build prueft ihre SHA-256-Pruefsummen.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File windows-app/build.ps1
./windows-app/dist/asn-labeler-test.exe --self-test
# Lokal interaktiv testen, ohne Installation oder Release:
./windows-app/dist/asn-labeler-test.exe --development
# Optional: gepinnten Inno-Setup-Compiler installieren und Setup bauen
powershell -NoProfile -ExecutionPolicy Bypass -File windows-app/tools/setup-inno.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File windows-app/build-installer.ps1
```

### Reservation-Service

Python 3.12 und die gepinnte Abhaengigkeit Waitress werden fuer Entwicklung benoetigt.
Tests verwenden Mock-Paperless und temporaere Datenbanken, keinen echten Drucker.

```powershell
python -m venv .venv
./.venv/Scripts/python.exe -m pip install --require-hashes -r reservation-service/requirements.txt
./.venv/Scripts/python.exe reservation-service/package_source.py --output reservation-service/source.zip
./.venv/Scripts/python.exe -m unittest discover -s reservation-service/tests
./.venv/Scripts/python.exe -m unittest discover -s tools/tests
./.venv/Scripts/python.exe -m unittest discover -s windows-app/tests -p test_release_packaging.py
```

Unter Linux entsprechen diese Befehle `.venv/bin/python`. Docker baut das
Service-Quellcodearchiv automatisch aus dem Build-Kontext.

## Dokumentation

- [Update- und Release-Ablauf](docs/RELEASING.md)
- [Versionshistorie](CHANGELOG.md)
- [Bedienung und Dokumentstatus](docs/USAGE.md)
- [Server-Einrichtung](reservation-service/README.md)

## Lizenz

Eigener Code: **AGPL-3.0-only**, siehe [LICENSE](LICENSE) und [NOTICE](NOTICE).
Der Windows-Client enthaelt die begrenzte [b-PAC-Zusatzerlaubnis](LICENSE-BPAC-EXCEPTION.txt).
Drittanbieter behalten ihre [eigenen Lizenzen](THIRD-PARTY-NOTICES.md).
Der Installer verwendet [Inno Setup](windows-app/vendor/InnoSetup/LICENSE.txt).
Kommerzielle Nutzung ist erlaubt; keine Gewaehrleistung.

Bei Weitergabe der EXE den passenden Quellcode gleichwertig am selben Downloadort
anbieten. Der Service bietet `/admin/license`, `/admin/notices` und
`/admin/source.zip` ohne Anmeldung an. Das Archiv enthaelt keine Laufzeitdaten.
Bei Quellcodeaenderungen neu bauen. Vor oeffentlicher Verteilung bleiben
Rechte-/Brother-Pruefung und Anbieter-/Datenschutzhinweise zu klaeren.
