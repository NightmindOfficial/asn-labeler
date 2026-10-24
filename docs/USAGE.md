# Bedienung und Dokumentstatus

## Paperless vorbereiten

Der Labelinhalt ist das konfigurierte ASN-Praefix gefolgt von der Nummer,
beispielsweise `ASN00123`, als DataMatrix ECC 200.
In der Paperless-Konfiguration ASN-Barcode-Erkennung aktivieren:

```env
PAPERLESS_CONSUMER_ENABLE_ASN_BARCODE=true
PAPERLESS_CONSUMER_ASN_BARCODE_PREFIX=ASN
```

Die installierte Paperless-Version muss DataMatrix decodieren koennen. Bei
Versionen mit waehlbarem Scanner das ZXing-Backend verwenden; die passende
Konfiguration in der Dokumentation der installierten Paperless-Version pruefen.
Nach Konfigurationsaenderungen den Paperless-Container neu erstellen.

## Client einrichten und drucken

1. Brother-Treiber und x64-b-PAC installieren. Der Drucker muss unter Windows
   eingerichtet sein. Ein P-touch-Test hilft bei der Pruefung des Treibers.
2. In den Einstellungen Serveradresse und Client-Schluessel hinterlegen.
   Einstellungen werden automatisch uebernommen. Der API-Schluessel wird im
   Windows Credential Manager gespeichert.
3. Drucker auswaehlen. Bei PF-B11221-kompatiblen 23x23-mm-Rollen das passende
   Rollenprofil verwenden; Originalmedien behalten das Standardprofil.
4. Die App prueft zuerst den Server, anschliessend Drucker und Medium. Sobald
   die Vorschau bereit ist, mit dem Drucken-Button oder Enter drucken.

Es koennen 1 bis 10 Labels je Serie gedruckt werden. Bei mehreren Labels bestimmt
"einzeln schneiden", ob nach jedem Label oder erst am Serienende geschnitten wird.
Die App sperrt weitere Druckaktionen waehrend eines Auftrags. Ein erfolgreicher
Versand allein gilt nicht als bestaetigter Ausdruck.

Die Druckvorschau folgt dem erkannten Medium. Stanz- und Endlosmedien werden
unterstuetzt, soweit das Layout in die verfuegbare Druckflaeche passt. Ruhezone
und ganzzahlige DataMatrix-Module bleiben erhalten; unpassende Layouts werden
abgelehnt. Nach einem Medien-/Treiberwechsel einen Testdruck aus den Einstellungen
erstellen und mit einem DataMatrix-faehigen Scanner pruefen.

## Dokumente

Eintraege sind nach ASN absteigend sortiert. Der Client aktualisiert standardmaessig
alle 30 Sekunden, das Adminpanel alle 15 Sekunden; beide Intervalle sind einstellbar.
Filter fuer erledigte Eintraege werden gespeichert. Ausblenden ist keine Loeschung.

| Anzeige | Bedeutung |
| --- | --- |
| Nicht gedruckt | Reserviert, der Druck wurde noch nicht gestartet |
| In Druck | Druck gestartet; Bestaetigungsfrist noch nicht abgelaufen |
| Druck unklar | Druck nicht bestaetigt und Wartefrist abgelaufen; physisch pruefen |
| Scan ausstehend | Labeldruck bestaetigt, zugehoerige ASN noch nicht in Paperless gefunden |
| In Paperless | Dokument mit dieser konkreten ASN gefunden |
| Gesperrt | Nummer darf aktuell nicht erneut vergeben werden |

Die Liste enthaelt nur Reservierungen dieses Dienstes. Bereits anderweitig in
Paperless vergebene ASNs beeinflussen die Nummernvergabe, erzeugen aber nicht
automatisch eigene Listeneintraege. Die Differenz zum Paperless-Maximum ist
keine verlaessliche Anzahl ausstehender Dokumente.

## Unklare Drucke und Freigabe

Solange der Auftrag als "In Druck" gilt, ist Beheben gesperrt. Nach Ablauf der
Frist wird ein unbestaetigter Auftrag als unklar angezeigt, aber nicht abgebrochen
oder erneut gedruckt. Die Standardfrist betraegt 30 Sekunden, mindestens 10;
sie wird durch `PRINTING_SETTLE_SECONDS` im Service festgelegt.

- **Beheben:** Existiert ein lesbares Label, kann der Eintrag zu Scan ausstehend
  wechseln. Wird die ASN in Paperless gefunden, gilt sie als erledigt.
- **Abbrechen:** Auch Scan ausstehend kann beendet werden. Zuvor das Label
  entfernen/vernichten und sein Fehlen ausdruecklich bestaetigen.
- **Erneute Vergabe:** Nur nach serverseitiger Pruefung von Paperless,
  gespeichertem Hoechststand und hoeheren aktiven Reservierungen/Drucken. Sind
  diese Voraussetzungen nicht erfuellt, wird die Nummer gesperrt statt freigegeben.
  Hoehere gesperrte oder wiederverwendbare Nummern gelten dabei nicht als aktive Drucke.
- **Entsperren:** Eine gesperrte Nummer kann im Adminpanel nach erneuter Pruefung
  und ausdruecklicher Bestaetigung freigegeben werden, nicht durch blosses Loeschen.

Nur nie gestartete Reservierungen koennen nach ihrer TTL automatisch auslaufen.
Ein moeglicherweise bereits gedrucktes Label wird niemals automatisch freigegeben.
Bei Verbindungsfehlern keine manuelle Doppelvergabe vornehmen. Ein globaler
Zaehlerabgleich ist kein Ersatz fuer die Klaerung eines einzelnen Druckauftrags.

## Sekundaerlabels und Demo

In den Einstellungen koennen Sekundaerlabels fuer bereits in Paperless vorhandene
Dokumente gedruckt werden. Sie tragen `! -COPY- !` und vergeben keine neue ASN.
Listen zeigen die Anzahl bestaetigter Sekundaerlabels. Die Zahl ist kein
garantierter physischer Bestand: Aeltere Clients melden keine Kopien, und ein
Absturz vor Speicherung der Druckbestaetigung kann zu Unterzaehlung fuehren.

Der farblich markierte Demo-Modus simuliert Server und Drucker lokal, ohne echte
API-Aufrufe oder Ausdrucke. Er eignet sich zum Kennenlernen der Oberflaeche;
fuer echte Drucke muss er ausgeschaltet werden.

## Drucken ohne Fenster (ab 2.1)

Die optionale CLI im Installer aktivieren, App einrichten und schliessen.
Danach ein neues Terminal oeffnen:

```powershell
asnlbl help
asnlbl print -c 3
```

Syntax, Flags, Installation und Exitcodes stehen in der separaten [CLI-Anleitung](CLI.md).
