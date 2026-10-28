"use strict";
window.ASNLocale = (() => {
  const en = {
    "Nach Updates suchen":"Check for updates",
    "Updates werden geprüft…":"Checking for updates…",
    "Prüfung läuft noch. Bitte später erneut prüfen.":"Check still running. Please check again later.",
    "Updateprüfung fehlgeschlagen. Bitte später erneut versuchen.":"Update check failed. Please try again later.",
    "Der Server ist aktuell.":"The server is up to date.",
    "Entwicklermodus":"Developer mode",
    "Eintrag kopieren":"Copy entry", "Kopiert":"Copied",
    "Kopieren fehlgeschlagen. Bitte die Browserberechtigung prüfen.":"Copy failed. Please check browser permissions.",
    "Wird geladen…":"Loading…",
    "Inhalt konnte nicht geladen werden. Bitte schließe den Dialog und versuche es erneut.":"Could not load content. Please close the dialog and try again.",
    "Verbindungsversuche nach zwei Minuten beendet. Bitte lade die Seite neu.":"Connection attempts stopped after two minutes. Please reload the page.",
    "Sicherheitsupdate erforderlich. Neue Druckaufträge sind gesperrt.":"Security update required. New print jobs are blocked.",
    "Server-Update verfügbar.":"Server update available.",
    "Sicherheitsstand derzeit nicht prüfbar. Keine neue Sperre aktiviert.":"Security status cannot currently be checked. No new block has been activated.",
    "Release öffnen":"View release",
    "Quellcode":"Source code", "Drittanbieter":"Third-party notices", "Ohne Gewährleistung":"No warranty",
    "Zu diesem Dokument wurden {0} Sekundärlabels erstellt.":"{0} secondary labels have been created for this document.",
    "copy_prepared":"Secondary label prepared", "copy_printed":"Secondary label printed",
    "secondary_label_confirmed":"Secondary label confirmed",
    "ASN Administration":"ASN Administration",
    "Sprache":"Language", "Anmelden":"Sign in", "Abmelden":"Sign out", "Admin-Schlüssel":"Admin key",
    "Einstellungen":"Settings", "Einstellungen schließen":"Close settings", "Schließen":"Close",
    "Paperless-Verbindung":"Paperless connection", "Nicht geprüft":"Not checked",
    "Paperless-Höchststand · gespeichert":"Paperless high-water mark · saved", "Lokale Einträge":"Local entries",
    "Ansicht aktualisieren":"Refresh view", "Paperless prüfen":"Check Paperless", "Backup herunterladen":"Download backup",
    "Mit Paperless abgleichen":"Sync with Paperless", "Ansichten":"Views", "Dokumente":"Documents",
    "Reservierungen":"Reservations", "Änderungsverlauf":"Change history", "Service-Protokoll":"Service log",
    "Gedruckte ausblenden":"Hide printed", "Erledigte ausblenden":"Hide completed", "Keine Einträge vorhanden.":"No entries.",
    "Vorherige Seite":"Previous page", "Nächste Seite":"Next page", "Refresh (Sekunden)":"Refresh (seconds)",
    "Höchststand korrigieren":"Correct high-water mark", "Höchststand korrigieren…":"Correct high-water mark…",
    "Gespeicherten Paperless-Höchststand auf den aktuell abgefragten Wert senken. Nur wenn für die dadurch wieder verfügbaren Nummern keine physischen Labels existieren. Lokale Reservierungen und Druckzustände bleiben geschützt.":"Lower the saved Paperless high-water mark to its current value. Only do this if no physical labels exist for the numbers that become available again. Local reservations and print states remain protected.",
    "Offene Einträge sperren":"Block outstanding entries", "Offene Einträge sperren…":"Block outstanding entries…",
    "Offene Einträge sperren. Gedruckte Labels bleiben damit vor einer erneuten Vergabe geschützt. Eine spätere Einzelfreigabe unterliegt erneut allen Sicherheitsprüfungen.":"Block outstanding entries to prevent printed labels from being allocated again. Releasing individual entries later still requires all safety checks.",
    "Druck-Clients sind gestoppt; kein Druckauftrag läuft mehr.":"Print clients are stopped; no print job is running.",
    "Zur Bestätigung SPERREN eingeben":"Type SPERREN to confirm", "Zurück":"Back", "Label existiert":"Label exists", "Bestätigen":"Confirm",
    "Server nicht erreichbar.":"Server unavailable.", "Erneuter Verbindungsversuch in 10 Sekunden":"Retrying in 10 seconds",
    "Die angezeigten Daten können veraltet sein. Änderungen werden nicht automatisch wiederholt.":"Displayed data may be outdated. Changes are never retried automatically.",
    "Service wurde aktualisiert":"Service updated", "Bitte lade das Adminpanel neu, bevor du weiterarbeitest. Anschließend ist eine erneute Anmeldung erforderlich.":"Reload the admin panel before continuing. You will need to sign in again.",
    "Jetzt neu laden":"Reload now", "Wird verarbeitet…":"Processing…", "Verbindung wiederhergestellt":"Connection restored",
    "Reserviert":"Reserved", "Druck gestartet":"Print started", "In Druck":"Printing", "Gedruckt":"Printed", "Wiederverwendbar":"Reusable",
    "Gesperrt":"Blocked", "Scan ausstehend":"Awaiting scan", "Druck unklar":"Print uncertain", "In Paperless":"In Paperless", "Nicht gedruckt":"Not printed",
    "ASN reserviert":"ASN reserved", "Druck bestätigt":"Print confirmed", "Reservierung freigegeben":"Reservation released",
    "Label vorhanden":"Label exists", "Label fehlt / vernichtet":"Label missing / destroyed", "Sicherer Abgleich":"Safe synchronization",
    "Reservierung abgelaufen":"Reservation expired", "Durch Admin gesperrt":"Blocked by admin", "Datenbestand übernommen":"Existing data imported",
    "Erneut reserviert":"Reserved again", "In Paperless erfasst; Sperre aufgehoben":"Imported in Paperless; block lifted",
    "Admin-Schlüssel ungültig oder ADMIN_API_KEYS nicht eingerichtet.":"Invalid admin key or ADMIN_API_KEYS not configured.",
    "Anfrage fehlgeschlagen":"Request failed", "\nFelder: ":"\nFields: ", "\nRequest-ID: ":"\nRequest ID: ",
    "Zeitüberschreitung. Vor Wiederholung einer Änderung den aktuellen Zustand prüfen.":"Request timed out. Check the current state before retrying a change.",
    "Mehr laden":"Load more", "Weitere 20 Änderungen laden":"Load 20 more changes",
    "Paperless-Abgleich nicht möglich. Lokale Reservierungszustände können veraltet sein. ":"Paperless sync unavailable. Local reservation states may be outdated. ",
    "Verbunden · ASN ":"Connected · ASN ", "Service-Protokoll · letzte 300 Ereignisse":"Service log · last 300 events",
    "Dokumenttitel":"Document title", "Aktualisiert":"Updated", "Aktion":"Action", "Abbrechen…":"Cancel…", "Beheben…":"Resolve…",
    "Lokaler Zustand":"Local state", "Reserviert bis":"Reserved until", "Nr. / ASN":"No. / ASN", "Zeit":"Time", "Übergang":"Transition",
    "Grund":"Reason", "Neu":"New", "Höchststand korrigiert":"High-water mark corrected", "Ereignis":"Event", "Aktuelle Sitzung":"Current session",
    " Änderungen":" changes", "Seite ":"Page ", "Freigeben…":"Release…", " freigeben?":": release?",
    "Alle Druck-Clients und zugehörigen Druckaufträge stoppen. Eine Freigabe ist nur möglich, wenn keine höhere geschützte ASN existiert.":"Stop all print clients and their print jobs. Release is only possible if no higher protected ASN exists.",
    "Label entfernt und vernichtet?":"Label removed and destroyed?", "Ich bestätige, dass kein physisches Label mit ":"I confirm that no physical label with ",
    " mehr existiert und kein alter Auftrag es noch drucken kann.":" exists and no old job can still print it.",
    " ist wiederverwendbar. Nächste ASN: ":" is reusable. Next ASN: ", "Zur Bestätigung ":"Type ", " eingeben":" to confirm",
    "Abbrechen":"Cancel", "Beheben":"Resolve",
    "Eine Freigabe ist nur erlaubt, wenn das physische Label nicht existiert oder alle Exemplare vernichtet wurden. Höhere geschützte ASNs verhindern die Wiederverwendung.":"Release is only allowed if the physical label does not exist or all copies have been destroyed. Higher protected ASNs prevent reuse.",
    "ASN dauerhaft sperren?":"Permanently block ASN?",
    "Es gibt höhere lokal geschützte ASNs. Diese Nummer kann deshalb nicht wiederverwendet werden. Dauerhaft sperren?":"Higher locally protected ASNs exist. This number cannot be reused. Block permanently?",
    "Nummer ist wiederverwendbar.":"Number is reusable.", "Neuer Status: ":"New status: ",
    "Bitte die HTTPS-Tunneladresse verwenden, damit der Admin-Schlüssel geschützt bleibt.":"Use the HTTPS tunnel URL to protect your admin key.",
    "Wirklich abmelden?":"Sign out?", "Für die nächste Anmeldung wird dein Admin-Schlüssel benötigt.":"You will need your admin key to sign in again.",
    "Ansicht kann in diesem Browser nicht dauerhaft gespeichert werden.":"This browser cannot save the view preference.",
    "Paperless erreichbar. Höchste erfasste ASN: ":"Paperless available. Highest recorded ASN: ", "Verbindung fehlgeschlagen":"Connection failed",
    "Backup erstellt. Ein späteres Wiederherstellen muss seitdem gedruckte Labels berücksichtigen.":"Backup created. A future restore must account for labels printed since this backup.",
    "Mit Paperless abgleichen?":"Sync with Paperless?",
    "Nicht gestartete Reservierungen werden sicher freigegeben oder gesperrt. Gedruckte, unklare und gesperrte ASNs bleiben erhalten.":"Reservations not yet started are safely released or blocked. Printed, uncertain and blocked ASNs are preserved.",
    " Reservierungen freigegeben. Nächste ASN: ":" reservations released. Next ASN: ", "Keine offenen Einträge vorhanden.":"No outstanding entries.",
    "Offene Einträge sperren?":"Block outstanding entries?", " Einträge werden dauerhaft gesperrt. Keine Nummer wird dadurch wiederverwendbar.":" entries will be permanently blocked. No number becomes reusable.",
    "Endgültig bestätigen":"Final confirmation", " ASNs dauerhaft sperren?":" ASNs: block permanently?", " ASNs gesperrt.":" ASNs blocked.",
    "Keine Korrektur nötig: Der gespeicherte Höchststand liegt nicht über dem aktuellen Paperless-Stand. Lokale Sperren können einzeln in der Liste freigegeben werden.":"No correction needed: the saved high-water mark is not above the current Paperless value. Local blocks can be released individually in the list.",
    "Gespeichert: ":"Saved: ", " → Paperless aktuell: ":" → Current Paperless: ", " Nächste vergebbare ASN danach: ":" Next available ASN afterwards: ", ". Nächste vergebbare ASN danach: ":". Next available ASN afterwards: ",
    " lokale Einträge oberhalb des Paperless-Stands bleiben geschützt. Bitte auch andere Änderungen in Paperless währenddessen stoppen.":" local entries above the Paperless value remain protected. Also stop other changes in Paperless while proceeding.",
    "Höchststand korrigieren?":"Correct high-water mark?", "Keine physischen Labels vorhanden?":"No physical labels exist?",
    "Ich bestätige, dass für die dadurch wieder verfügbaren Nummern keine physischen Labels existieren. Bei Unsicherheit abbrechen. Lokale Druck- und Reservierungsdaten werden nicht gelöscht.":"I confirm that no physical labels exist for the numbers made available again. Cancel if unsure. Local print and reservation records will not be deleted.",
    "Höchststand von ":"High-water mark changed from ", " auf ":" to ", " korrigiert. Nächste ASN: ":". Next ASN: ",
    "Verbindung wird geprüft…":"Checking connection…", "Erneuter Verbindungsversuch in ":"Retrying in ", " Sekunden":" seconds",
    "Wird aktualisiert…":"Refreshing…", "Refresh pausiert":"Refresh paused", "Refresh in ":"Refresh in ",
    "Automatischer Abgleich fehlgeschlagen. Daten möglicherweise veraltet. ":"Automatic sync failed. Data may be outdated. "
  };
  let language = "de";
  try { if (localStorage.getItem("asn-admin.language") === "en") language = "en"; } catch (_) {}
  const t = text => language === "en" ? (en[text] ?? text) : text;
  // Only capture static shell text. Never translate document names or API data.
  const bindings = [];
  const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
  while (walker.nextNode()) {
    const node = walker.currentNode, raw = node.textContent, source = raw.trim();
    if (Object.hasOwn(en, source)) bindings.push(() => {
      if (node.isConnected) node.textContent = raw.replace(source, t(source));
    });
  }
  document.querySelectorAll('[title],[aria-label]').forEach(element => {
    for (const attribute of ['title','aria-label']) {
      const source = element.getAttribute(attribute);
      if (Object.hasOwn(en, source)) bindings.push(() => element.setAttribute(attribute, t(source)));
    }
  });
  function apply(value) {
    language = value === "en" ? "en" : "de";
    document.documentElement.lang = language;
    document.getElementById("language").value = language;
    bindings.forEach(update => update());
    try { localStorage.setItem("asn-admin.language", language); } catch (_) {}
  }
  apply(language);
  return {t, apply, get language() { return language; }};
})();
