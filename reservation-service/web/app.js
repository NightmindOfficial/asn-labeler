"use strict";
(() => {
  const $ = id => document.getElementById(id);
  const T = ASNLocale.t;
  $("language").onchange = () => {
    ASNLocale.apply($("language").value);
    notice("");
    if (key) run(async () => { lastView = ""; await load(); });
  };
  const version = document.querySelector('meta[name="asn-version"]').content;
  let disconnected = false, updateRequired = false, retryAt = 0, reconnecting = false;
  const connectionError = () => Object.assign(new Error(T("Server nicht erreichbar.")), {connection:true});
  function requireUpdate() {
    updateRequired = true;
    if ($("connectionDialog").open) $("connectionDialog").close();
    if (!$("updateDialog").open) $("updateDialog").showModal();
    throw connectionError();
  }
  function checkVersion(response) {
    const current = response.headers.get("X-ASN-Version");
    if (current && current !== version) requireUpdate();
  }
  function offline() {
    if (!updateRequired && !disconnected) {
      disconnected = true; retryAt = performance.now() + 10000;
      $("retryCountdown").textContent = T("Erneuter Verbindungsversuch in 10 Sekunden");
      $("reconnected").hidden = true;
      $("connectionDialog").showModal();
    }
    return connectionError();
  }
  $("connectionDialog").oncancel = $("updateDialog").oncancel = event => event.preventDefault();
  $("reloadApp").onclick = () => location.reload();
  const names = {reserved:"Reserviert",printing:"Druck gestartet",in_print:"In Druck",printed:"Gedruckt",reusable:"Wiederverwendbar",blocked:"Gesperrt",pending:"Scan ausstehend",uncertain:"Druck unklar",imported:"In Paperless",unused:"Nicht gedruckt"};
  const completedPreference = "asn-admin.hideCompleted";
  try {
    const saved = localStorage.getItem(completedPreference);
    $("completed").checked = saved === null ? localStorage.getItem("asn-admin.showCompleted") !== "true" : saved !== "false";
  } catch (_) { /* Keep the default when storage is unavailable. */ }
  const reasons = {allocated:"ASN reserviert",client_printing:"Druck gestartet",client_printed:"Druck bestätigt",client_released:"Reservierung freigegeben",label_exists:"Label vorhanden",label_destroyed_confirmed:"Label fehlt / vernichtet",safe_sync:"Sicherer Abgleich",reservation_expired:"Reservierung abgelaufen",admin_archive_open:"Durch Admin gesperrt",migration_baseline:"Datenbestand übernommen",replaced_by_new_reservation:"Erneut reserviert"};
  reasons.paperless_import_confirmed = "In Paperless erfasst; Sperre aufgehoben";
  let key = "", tab = "documents", page = 1, busy = false;
  let eventRows = [], eventBefore = null, eventHasMore = false;
  const printedPreference = "asn-admin.hidePrinted";
  try { $("hidePrinted").checked = localStorage.getItem(printedPreference) !== "false"; } catch (_) {}
  document.querySelector('.view-heading').append($("printedControl"));
  let refreshInterval = 15000;
  try {
    const saved = Number(localStorage.getItem("asn-admin.refreshSeconds"));
    if (Number.isInteger(saved) && saved >= 10 && saved <= 3600) refreshInterval = saved * 1000;
  } catch (_) {}
  $("refreshSeconds").value = refreshInterval / 1000;
  let refreshAt = performance.now() + refreshInterval, autoRefreshing = false;
  function saveRefreshInterval() {
    const seconds = Number($("refreshSeconds").value);
    if (!Number.isInteger(seconds) || seconds < 10 || seconds > 3600) return false;
    refreshInterval = seconds * 1000;
    refreshAt = performance.now() + refreshInterval;
    try { localStorage.setItem("asn-admin.refreshSeconds", String(refreshInterval / 1000)); } catch (_) {}
    return true;
  }
  $("refreshSeconds").oninput = saveRefreshInterval;
  $("refreshSeconds").onchange = () => {
    if (!saveRefreshInterval()) $("refreshSeconds").value = refreshInterval / 1000;
  };
  function notice(text, error = false) { $("notice").textContent = text; $("notice").className = error ? "error" : ""; $("notice").hidden = !text; }
  function time(value) { return value ? new Date(value * 1000).toLocaleString(ASNLocale.language === "en" ? "en-GB" : "de-DE") : "—"; }
  function logout() { key = ""; $("settingsDialog").close(); $("settings").hidden = true; $("key").value = ""; $("login").hidden = false; $("workspace").hidden = true; $("logout").hidden = true; $("rows").replaceChildren(); $("maximum").textContent = "—"; $("total").textContent = "—"; $("connection").textContent = T("Nicht geprüft"); }
  $("settings").onclick = () => {
    if (!key || busy) return;
    notice("");
    $("settingsNotice").append($("notice"));
    $("settingsDialog").showModal();
  };
  $("closeSettings").onclick = () => { if (!busy) $("settingsDialog").close(); };
  $("settingsDialog").oncancel = event => { if (busy) event.preventDefault(); };
  $("settingsDialog").onclose = () => {
    if (!saveRefreshInterval()) $("refreshSeconds").value = refreshInterval / 1000;
    document.querySelector("main").prepend($("notice"));
    refreshAt = performance.now() + refreshInterval;
  };
  async function api(path, body, binary = false) {
    if (updateRequired || disconnected) throw connectionError();
    const controller = new AbortController(), timer = setTimeout(() => controller.abort(), 15000);
    try {
      let response;
      try { response = await fetch(path, {method:body === undefined ? "GET" : "POST", headers:{Authorization:"Bearer " + key,"Content-Type":"application/json","X-ASN-Version":version}, body:body === undefined ? undefined : JSON.stringify(body), cache:"no-store", credentials:"omit", redirect:"error", signal:controller.signal}); }
      catch (_) { throw offline(); }
      checkVersion(response);
      if (response.status === 401) { logout(); throw new Error(T("Admin-Schlüssel ungültig oder ADMIN_API_KEYS nicht eingerichtet.")); }
      if (binary && response.ok && response.headers.get("Content-Type")?.includes("application/octet-stream")) return response.blob();
      let data;
      try { data = await response.json(); if (!data || typeof data !== "object") throw new Error(); }
      catch (_) { throw offline(); }
      if (!response.ok) throw new Error((data.error || T("Anfrage fehlgeschlagen")) + (data.errorCode ? "\n" + data.errorCode : "") + (data.validationFields?.length ? T("\nFelder: ") + data.validationFields.join(", ") : "") + (data.requestId ? T("\nRequest-ID: ") + data.requestId : ""));
      return data;
    } catch (error) {
      if (error.name === "AbortError") throw new Error(T("Zeitüberschreitung. Vor Wiederholung einer Änderung den aktuellen Zustand prüfen."));
      throw error;
    } finally { clearTimeout(timer); }
  }
  async function run(action) {
    if (busy || disconnected || updateRequired) return;
    busy = true; $("busy").hidden = autoRefreshing;
    const controls = [...document.querySelectorAll("button,input,select")].filter(e => !e.closest("#confirmDialog,#updateDialog,#connectionDialog"));
    const states = controls.map(e => e.disabled); controls.forEach(e => e.disabled = true);
    try { await action(); } catch (error) { if (!error.connection) notice(error.message, true); }
    finally { controls.forEach((e,i) => e.disabled = states[i]); busy = false; $("busy").hidden = true; refreshAt = performance.now() + refreshInterval; updatePager(); }
  }
  let hasNext = false;
  function updatePager() {
    $("previous").hidden = tab === "events";
    $("previous").disabled = busy || page <= 1 || tab === "logs";
    if (tab === "events") $("next").textContent = T("Mehr laden");
    else {
      const icon = document.createElement("span"); icon.className = "material-icon";
      icon.setAttribute("aria-hidden","true"); icon.textContent = "\ue5cc";
      $("next").replaceChildren(icon);
    }
    $("next").setAttribute("aria-label", tab === "events" ? T("Mehr laden") : T("Nächste Seite"));
    $("next").title = tab === "events" ? T("Weitere 20 Änderungen laden") : T("Nächste Seite");
    $("next").disabled = busy || !hasNext;
  }
  function row(values) { const tr = document.createElement("tr"); for (const value of values) { const td = document.createElement("td"); if (value instanceof Node) td.append(value); else td.textContent = String(value ?? "—"); tr.append(td); } $("rows").append(tr); }
  function documentTitle(item) {
    const label = document.createElement("span"); label.textContent = item.documentTitle || "—";
    if (Number.isInteger(item.secondaryLabelCount) && item.secondaryLabelCount > 0) {
      const marker = document.createElement("span"); marker.className = "copy-marker"; marker.tabIndex = 0;
      marker.title = T("Zu diesem Dokument wurden {0} Sekundärlabels erstellt.").replace("{0}", item.secondaryLabelCount);
      marker.setAttribute("aria-label", marker.title);
      const icon = document.createElement("span"); icon.className = "material-icon"; icon.textContent = "\ue14d"; icon.setAttribute("aria-hidden", "true");
      marker.append(icon, document.createTextNode(" " + item.secondaryLabelCount)); label.append(marker);
    }
    return label;
  }
  function status(value) { const span = document.createElement("span"); span.className = "status " + (Object.hasOwn(names,value) ? value : ""); span.textContent = T(names[value] || value); return span; }
  function headers(labels) { $("head").replaceChildren(); const tr=document.createElement("tr"); labels.forEach(label=>{const th=document.createElement("th"); th.scope="col"; th.textContent=label; tr.append(th);}); $("head").append(tr); }
  async function summary() { const data = await api("/api/admin/state"); $("version").textContent = "Service " + data.version; $("footerVersion").textContent = data.version; $("maximum").textContent = data.paperlessHighWater; $("total").textContent = data.total; }
  let lastView = "";
  async function load(more = false) {
    // Fetch before replacing rows, so background refresh never blanks the table.
    const endpoint = tab === "documents" ? "/api/documents/status?page="+page+"&include_completed=true&hide_imported="+$("completed").checked
      : tab === "state" ? "/api/admin/state?page="+page+"&hide_printed="+$("hidePrinted").checked
      : tab === "events" ? "/api/admin/events?order=desc"+(more && eventBefore ? "&before="+eventBefore : "") : "/api/admin/logs";
    const data = await api(endpoint);
    if (tab === "events") {
      // A burst larger than one page leaves a gap: restart at the newest page
      // instead of silently joining non-contiguous history segments.
      const reset = !eventRows.length || (!more && data.hasMore && data.items.at(-1).seq > eventRows[0].seq);
      if (reset) eventRows = [];
      const merged = new Map(eventRows.map(item => [item.seq,item]));
      data.items.forEach(item => merged.set(item.seq,item));
      eventRows = [...merged.values()].sort((a,b)=>b.seq-a.seq);
      if (more || reset) { eventBefore = data.nextBefore; eventHasMore = data.hasMore; }
      data.items = eventRows;
    }
    if (data.paperlessSyncError) notice(T("Paperless-Abgleich nicht möglich. Lokale Reservierungszustände können veraltet sein. ") + data.paperlessSyncError, true);
    if (["documents","state"].includes(tab)) {
      const lastPage = Math.max(1, Math.ceil(data.total / (data.pageSize || 10)));
      if (page > lastPage) { page = lastPage; return load(); }
    }
    if (tab === "documents") {
      $("connection").textContent = T("Verbunden · ASN ") + data.paperlessMax;
      $("maximum").textContent = Math.max(Number($("maximum").textContent) || 0, data.paperlessMax);
    }
    const signature = JSON.stringify([tab,page,$("completed").checked,data.items,data.total]);
    if (signature === lastView && $("rows").children.length) return;
    lastView = signature;
    $("rows").replaceChildren(); $("empty").hidden = true; hasNext = false;
    $("completedControl").hidden = tab !== "documents";
    $("printedControl").hidden = tab !== "state";
    $("viewTitle").textContent = {documents:T("Dokumente"),state:T("Reservierungen"),events:T("Änderungsverlauf"),logs:T("Service-Protokoll · letzte 300 Ereignisse")}[tab];
    if (tab === "documents") {
      headers(["ASN","Status",T("Dokumenttitel"),T("Aktualisiert"),T("Aktion")]);
      $("connection").textContent = T("Verbunden · ASN ") + data.paperlessMax;
      $("maximum").textContent = Math.max(Number($("maximum").textContent) || 0, data.paperlessMax);
      data.items.forEach(item => { const action = document.createElement("span"); if (["pending","uncertain"].includes(item.status)) { const button=document.createElement("button"); button.textContent=item.status === "pending" ? T("Abbrechen…") : T("Beheben…"); button.addEventListener("click",()=>run(()=>resolve(item))); action.append(button); } if(item.status === "blocked") action.append(unblockButton(item.reservationId,item.barcodeText)); row([item.barcodeText,status(item.status),documentTitle(item),time(item.updatedAt),action]); });
      hasNext = page * 10 < data.total;
    } else if (tab === "state") {
      headers(["ASN",T("Lokaler Zustand"),T("Aktualisiert"),T("Reserviert bis"),T("Aktion")]);
      data.items.forEach(item => row([item.barcode_text,status(item.displayStatus || item.status),time(item.updated_at),time(item.expires_at),item.status === "blocked" ? unblockButton(item.id,item.barcode_text) : "—"]));
      hasNext = page * data.pageSize < data.total;
    } else if (tab === "events") {
      headers([T("Nr. / ASN"),T("Zeit"),T("Übergang"),T("Grund"),"Admin / Client"]);
      data.items.forEach(item=>row([item.seq+" / "+item.asn_number,time(item.time),T(names[item.old_status] || item.old_status || "Neu")+" → "+T(names[item.new_status] || item.new_status),item.reason === "admin_high_water_corrected" ? T("Höchststand korrigiert") : T(reasons[item.reason] || item.reason),item.actor]));
      hasNext = eventHasMore;
    } else {
      headers([T("Zeit"),T("Ereignis"),"Details"]);
      data.items.reverse().forEach(item=>{const detail=document.createElement("div"); detail.className="log-detail"; const {time:at,event,...rest}=item; detail.textContent=JSON.stringify(rest,null,2); row([time(at),event,detail]);});
    }
    $("empty").hidden=!!$("rows").children.length; $("page").textContent=tab === "logs" ? T("Aktuelle Sitzung") : tab === "events" ? eventRows.length+T(" Änderungen") : T("Seite ")+page; updatePager();
  }
  function unblockButton(id, barcode) {
    const button = document.createElement("button"); button.textContent = T("Freigeben…");
    button.onclick = () => run(async () => {
      if (!await confirmAction(barcode+T(" freigeben?"),T("Alle Druck-Clients und zugehörigen Druckaufträge stoppen. Eine Freigabe ist nur möglich, wenn keine höhere geschützte ASN existiert."))) return;
      if (!await confirmAction(T("Label entfernt und vernichtet?"),T("Ich bestätige, dass kein physisches Label mit ")+barcode+T(" mehr existiert und kein alter Auftrag es noch drucken kann."),{typed:true,check:false,phrase:"FREIGEBEN"})) return;
      const result = await api("/api/admin/reservations/"+encodeURIComponent(id)+"/unblock",{confirmation:"UNBLOCK ASN",physicalLabelAbsent:true});
      notice(barcode+T(" ist wiederverwendbar. Nächste ASN: ")+result.nextBarcodeText); await summary(); await load();
    });
    return button;
  }
  function confirmAction(title,text,{choose=false,typed=false,check=true,phrase="SPERREN",acceptLabel=T("Bestätigen")}={}) {
    return new Promise(resolve=>{
      $("dialogTitle").textContent=title; $("dialogText").textContent=text; $("checkLabel").hidden=!check; $("check").checked=false;
      $("typedLabel").hidden=!typed; $("typed").value=""; $("exists").hidden=!choose;
      $("typedPrompt").textContent=T("Zur Bestätigung ")+phrase+T(" eingeben");
      $("accept").textContent=choose ? T("Label fehlt / vernichtet") : acceptLabel;
      const valid=()=>{ $("accept").disabled=(check&&!$("check").checked)||(typed&&$("typed").value!==phrase); };
      $("check").onchange=valid; $("typed").oninput=valid; valid();
      let answer=null; $("confirmForm").onsubmit=e=>{e.preventDefault(); if (!$("accept").disabled) {answer="missing"; $("confirmDialog").close();}};
      $("dismiss").onclick=()=>$("confirmDialog").close(); $("exists").onclick=()=>{answer="exists"; $("confirmDialog").close();};
      $("confirmDialog").onclose=()=>resolve(answer); $("confirmDialog").showModal();
    });
  }
  async function resolve(item) {
    const answer=await confirmAction(item.barcodeText+": "+(item.status==="pending"?T("Abbrechen"):T("Beheben")),T("Eine Freigabe ist nur erlaubt, wenn das physische Label nicht existiert oder alle Exemplare vernichtet wurden. Höhere geschützte ASNs verhindern die Wiederverwendung."),{choose:item.status==="uncertain"});
    if (!answer) return;
    const action=item.status==="pending"?"cancel":answer==="exists"?"resolve-exists":"resolve-missing";
    const path="/api/reservations/"+encodeURIComponent(item.reservationId)+"/"+action;
    const body={physicalLabelAbsent:answer!=="exists"}; let result=await api(path,body);
    if (result.status==="confirmation_required") { if (!await confirmAction(T("ASN dauerhaft sperren?"),T("Es gibt höhere lokal geschützte ASNs. Diese Nummer kann deshalb nicht wiederverwendet werden. Dauerhaft sperren?"),{check:false})) return; result=await api(path+"-confirmed",body); }
    notice(result.status==="next"?T("Nummer ist wiederverwendbar."):T("Neuer Status: ")+T(names[result.status]||result.status)); await summary(); await load();
  }
  $("loginForm").addEventListener("submit",e=>{e.preventDefault(); run(async()=>{if(location.protocol!=="https:" && !["localhost","127.0.0.1"].includes(location.hostname)) throw new Error(T("Bitte die HTTPS-Tunneladresse verwenden, damit der Admin-Schlüssel geschützt bleibt.")); key=$("key").value.trim(); $("key").value=""; await summary(); $("login").hidden=true; $("workspace").hidden=false; $("logout").hidden=false; $("settings").hidden=false; notice(""); page=1; tab="documents"; document.querySelectorAll("[data-tab]").forEach(b=>b.setAttribute("aria-current",b.dataset.tab===tab?"page":"false")); await load();});});
  $("logout").onclick=()=>run(async()=>{
    if (!await confirmAction(T("Wirklich abmelden?"),T("Für die nächste Anmeldung wird dein Admin-Schlüssel benötigt."),{check:false,acceptLabel:T("Abmelden")})) return;
    logout(); notice("");
  });
  $("refresh").onclick=()=>run(async()=>{notice("");await summary();await load();});
  document.querySelectorAll("[data-tab]").forEach(button=>button.onclick=()=>run(async()=>{tab=button.dataset.tab;page=1;eventRows=[];eventBefore=null;eventHasMore=false;document.querySelectorAll("[data-tab]").forEach(b=>b.setAttribute("aria-current",b===button?"page":"false"));notice("");await load();}));
  $("completed").onchange=()=>run(async()=>{
    try { localStorage.setItem(completedPreference,String($("completed").checked)); }
    catch (_) { notice(T("Ansicht kann in diesem Browser nicht dauerhaft gespeichert werden."),true); }
    page=1;await load();
  });
  $("previous").onclick=()=>run(async()=>{page--;await load();});
  $("next").onclick=()=>run(async()=>{if(tab==="events") await load(true); else {page++;await load();}});
  $("hidePrinted").onchange=()=>run(async()=>{
    try { localStorage.setItem(printedPreference,String($("hidePrinted").checked)); } catch (_) {}
    page=1;lastView="";await load();
  });
  $("diagnostics").onclick=()=>run(async()=>{try{const data=await api("/api/admin/diagnostics");$("connection").textContent=T("Verbunden · ASN ")+data.paperlessMax;notice(T("Paperless erreichbar. Höchste erfasste ASN: ")+data.paperlessMax);}catch(error){$("connection").textContent=T("Verbindung fehlgeschlagen");throw error;}});
  $("backup").onclick=()=>run(async()=>{const blob=await api("/api/admin/backup",{},true),url=URL.createObjectURL(blob),a=document.createElement("a");a.href=url;a.download="asn-backup-"+new Date().toISOString().slice(0,10)+".sqlite3";document.body.append(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),60000);notice(T("Backup erstellt. Ein späteres Wiederherstellen muss seitdem gedruckte Labels berücksichtigen."));});
  $("sync").onclick=()=>run(async()=>{if(!await confirmAction(T("Mit Paperless abgleichen?"),T("Nicht gestartete Reservierungen werden sicher freigegeben oder gesperrt. Gedruckte, unklare und gesperrte ASNs bleiben erhalten."))) return;const result=await api("/api/admin/reset-to-paperless",{});notice(result.releasedReservations+T(" Reservierungen freigegeben. Nächste ASN: ")+result.nextBarcodeText);await summary();await load();});
  $("archive").onclick=()=>run(async()=>{const preview=await api("/api/admin/archive-preview",{});if(!preview.count){notice(T("Keine offenen Einträge vorhanden."));return;}if(!await confirmAction(T("Offene Einträge sperren?"),preview.count+T(" Einträge werden dauerhaft gesperrt. Keine Nummer wird dadurch wiederverwendbar.")))return;if(!await confirmAction(T("Endgültig bestätigen"),preview.count+T(" ASNs dauerhaft sperren?"),{typed:true,check:false}))return;const result=await api("/api/admin/archive-open",{previewToken:preview.previewToken,confirmation:"BLOCK OUTSTANDING"});notice(result.blocked+T(" ASNs gesperrt."));await summary();await load();});
  $("correctHighWater").onclick=()=>run(async()=>{
    const preview=await api("/api/admin/high-water-preview",{});
    if (preview.needsCorrection === false) { notice(T("Keine Korrektur nötig: Der gespeicherte Höchststand liegt nicht über dem aktuellen Paperless-Stand. Lokale Sperren können einzeln in der Liste freigegeben werden.")); return; }
    const explanation=T("Gespeichert: ")+preview.storedHighWater+T(" → Paperless aktuell: ")+preview.paperlessMax+T(". Nächste vergebbare ASN danach: ")+preview.nextAsnNumber+". "+preview.protectedReservations+T(" lokale Einträge oberhalb des Paperless-Stands bleiben geschützt. Bitte auch andere Änderungen in Paperless währenddessen stoppen.");
    if(!await confirmAction(T("Höchststand korrigieren?"),explanation)) return;
    if(!await confirmAction(T("Keine physischen Labels vorhanden?"),T("Ich bestätige, dass für die dadurch wieder verfügbaren Nummern keine physischen Labels existieren. Bei Unsicherheit abbrechen. Lokale Druck- und Reservierungsdaten werden nicht gelöscht."),{typed:true,check:false,phrase:"KORRIGIEREN"})) return;
    const result=await api("/api/admin/high-water-correct",{previewToken:preview.previewToken,confirmation:"LOWER HIGH WATER",physicalLabelsAbsent:true});
    notice(T("Höchststand von ")+result.previousHighWater+T(" auf ")+result.paperlessHighWater+T(" korrigiert. Nächste ASN: ")+result.nextBarcodeText); await summary(); await load();
  });
  let refreshFailed = false;
  setInterval(async () => {
    if (updateRequired) return;
    if (disconnected) {
      const remaining = Math.max(0, Math.ceil((retryAt - performance.now()) / 1000));
      $("retryCountdown").textContent = reconnecting ? T("Verbindung wird geprüft…") : T("Erneuter Verbindungsversuch in ") + remaining + T(" Sekunden");
      if (remaining || reconnecting) return;
      reconnecting = true;
      const controller = new AbortController(), timeout = setTimeout(() => controller.abort(), 8000);
      try {
        const response = await fetch("/health", {cache:"no-store",credentials:"omit",redirect:"error",signal:controller.signal});
        checkVersion(response);
        const health = await response.json();
        if (!response.ok || health.ok !== true || !response.headers.get("X-ASN-Version")) throw new Error();
        disconnected = false; $("connectionDialog").close();
        $("reconnected").hidden = false;
        setTimeout(() => { $("reconnected").hidden = true; }, 2500);
        if (key && !$("workspace").hidden) await run(async () => { await summary(); await load(); });
      } catch (_) { /* Retry reads only; never replay a mutation. */ }
      finally { clearTimeout(timeout); reconnecting = false; retryAt = performance.now() + 10000; }
      return;
    }
    $("refreshCountdown").hidden = !key;
    if (!key || busy || document.hidden || $("confirmDialog").open || $("settingsDialog").open) {
      refreshAt = performance.now() + refreshInterval;
      $("refreshCountdown").textContent = autoRefreshing ? T("Wird aktualisiert…") : T("Refresh pausiert");
      return;
    }
    const remaining = Math.max(0, Math.ceil((refreshAt - performance.now()) / 1000));
    $("refreshCountdown").textContent = T("Refresh in ") + remaining + T(" Sekunden");
    if (remaining > 0) return;
    autoRefreshing = true;
    $("refreshCountdown").textContent = T("Wird aktualisiert…");
    try {
      await run(async () => {
        try { await load(); await summary(); if(refreshFailed) notice(""); refreshFailed = false; }
        catch (error) { refreshFailed = true; if (!error.connection) notice(T("Automatischer Abgleich fehlgeschlagen. Daten möglicherweise veraltet. ") + error.message, true); }
      });
    } finally { autoRefreshing = false; refreshAt = performance.now() + refreshInterval; updatePager(); }
  }, 250);
  document.addEventListener("visibilitychange", () => { refreshAt = performance.now() + refreshInterval; });
})();
