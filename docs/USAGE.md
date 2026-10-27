# Bedienung und Dokumentstatus

## Paperless vorbereiten

Der Labelinhalt ist das konfigurierte ASN-Präfix gefolgt von der Nummer,
beispielsweise `ASN00123`, als DataMatrix ECC 200.
In der Paperless-Konfiguration ASN-Barcode-Erkennung aktivieren:

```env
PAPERLESS_CONSUMER_ENABLE_ASN_BARCODE=true
PAPERLESS_CONSUMER_ASN_BARCODE_PREFIX=ASN
```

Die installierte Paperless-Version muss DataMatrix decodieren können. Bei
Versionen mit wählbarem Scanner das ZXing-Backend verwenden; die passende
Konfiguration in der Dokumentation der installierten Paperless-Version prüfen.
Nach Konfigurationsänderungen den Paperless-Container neu erstellen.

## Client einrichten und drucken

1. Brother-Treiber und x64-b-PAC installieren. Der Drucker muss unter Windows
   eingerichtet sein. Ein P-touch-Test hilft bei der Prüfung des Treibers.
2. In den Einstellungen Serveradresse und Client-Schlüssel hinterlegen.
   Einstellungen werden automatisch übernommen. Der API-Schlüssel wird im
   Windows Credential Manager gespeichert.
3. Drucker auswählen. Bei PF-B11221-kompatiblen 23x23-mm-Rollen das passende
   Rollenprofil verwenden; Originalmedien behalten das Standardprofil.
4. Die App prüft zuerst den Server, anschließend Drucker und Medium. Sobald
   die Vorschau bereit ist, mit dem Drucken-Button oder Enter drucken.

Es können 1 bis 10 Labels je Serie gedruckt werden. Bei mehreren Labels bestimmt
"einzeln schneiden", ob nach jedem Label oder erst am Serienende geschnitten wird.
Die App sperrt weitere Druckaktionen während eines Auftrags. Ein erfolgreicher
Versand allein gilt nicht als bestätigter Ausdruck.

Die Druckvorschau folgt dem erkannten Medium. Stanz- und Endlosmedien werden
unterstützt, soweit das Layout in die verfügbare Druckfläche passt. Ruhezone
und ganzzahlige DataMatrix-Module bleiben erhalten; unpassende Layouts werden
abgelehnt. Nach einem Medien-/Treiberwechsel einen Testdruck aus den Einstellungen
erstellen und mit einem DataMatrix-fähigen Scanner prüfen.

## Dokumente

Einträge sind nach ASN absteigend sortiert. Der Client aktualisiert standardmäßig
alle 30 Sekunden, das Adminpanel alle 15 Sekunden; beide Intervalle sind einstellbar.
Filter für erledigte Einträge werden gespeichert. Ausblenden ist keine Löschung.

| Anzeige | Bedeutung |
| --- | --- |
| Nicht gedruckt | Reserviert, der Druck wurde noch nicht gestartet |
| In Druck | Druck gestartet; Bestätigungsfrist noch nicht abgelaufen |
| Druck unklar | Druck nicht bestätigt und Wartefrist abgelaufen; physisch prüfen |
| Scan ausstehend | Labeldruck bestätigt, zugehörige ASN noch nicht in Paperless gefunden |
| In Paperless | Dokument mit dieser konkreten ASN gefunden |
| Gesperrt | Nummer darf aktuell nicht erneut vergeben werden |

Die Liste enthält nur Reservierungen dieses Dienstes. Bereits anderweitig in
Paperless vergebene ASNs beeinflussen die Nummernvergabe, erzeugen aber nicht
automatisch eigene Listeneinträge. Die Differenz zum Paperless-Maximum ist
keine verlässliche Anzahl ausstehender Dokumente.

## Unklare Drucke und Freigabe

Solange der Auftrag als "In Druck" gilt, ist Beheben gesperrt. Nach Ablauf der
Frist wird ein unbestätigter Auftrag als unklar angezeigt, aber nicht abgebrochen
oder erneut gedruckt. Die Standardfrist beträgt 30 Sekunden, mindestens 10;
sie wird durch `PRINTING_SETTLE_SECONDS` im Service festgelegt.

- **Beheben:** Existiert ein lesbares Label, kann der Eintrag zu Scan ausstehend
  wechseln. Wird die ASN in Paperless gefunden, gilt sie als erledigt.
- **Abbrechen:** Auch Scan ausstehend kann beendet werden. Zuvor das Label
  entfernen/vernichten und sein Fehlen ausdrücklich bestätigen.
- **Erneute Vergabe:** Nur nach serverseitiger Prüfung von Paperless,
  gespeichertem Höchststand und höheren aktiven Reservierungen/Drucken. Sind
  diese Voraussetzungen nicht erfüllt, wird die Nummer gesperrt statt freigegeben.
  Höhere gesperrte oder wiederverwendbare Nummern gelten dabei nicht als aktive Drucke.
- **Entsperren:** Eine gesperrte Nummer kann im Adminpanel nach erneuter Prüfung
  und ausdrücklicher Bestätigung freigegeben werden, nicht durch bloßes Löschen.

Nur nie gestartete Reservierungen können nach ihrer TTL automatisch auslaufen.
Ein möglicherweise bereits gedrucktes Label wird niemals automatisch freigegeben.
Bei Verbindungsfehlern keine manuelle Doppelvergabe vornehmen. Ein globaler
Zählerabgleich ist kein Ersatz für die Klärung eines einzelnen Druckauftrags.

## Sekundärlabels und Demo

In den Einstellungen können Sekundärlabels für bereits in Paperless vorhandene
Dokumente gedruckt werden. Sie tragen `! -COPY- !` und vergeben keine neue ASN.
Die Auswahl bietet eine Suche nach der vollständigen ASN-Nummer, auch außerhalb
der aktuell angezeigten Seite. Das Suchergebnis vor dem Drucken auswählen.
Ein orangefarbener Punkt kennzeichnet ein bestätigtes Sekundärlabel, ein Ring
mehrere. Beim Überfahren mit der Maus erscheint die genaue Anzahl. Die Zahl ist kein
garantierter physischer Bestand: Ältere Clients melden keine Kopien, und ein
Absturz vor Speicherung der Druckbestätigung kann zu Unterzählung führen.

Der farblich markierte Demo-Modus unter **Einstellungen → Wartung** simuliert Server und Drucker lokal, ohne echte
API-Aufrufe oder Ausdrucke. Er eignet sich zum Kennenlernen der Oberfläche;
für echte Drucke muss er ausgeschaltet werden.

## Drucken ohne Fenster (ab 2.1)

Die optionale CLI im Installer aktivieren, App einrichten und schließen.
Danach ein neues Terminal öffnen:

```powershell
asnlbl help
asnlbl print -c 3
```

Syntax, Flags, Installation und Exitcodes stehen in der separaten [CLI-Anleitung](CLI.md).
