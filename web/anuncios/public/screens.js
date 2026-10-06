// «Pantallas»: every Dashboard Metas screen that reports to the panel, its state and its history.
// The data comes from /api/screens (latest report of each screen + events + who saw each announcement).

const $ = (id) => document.getElementById(id);
const pad = (n) => String(n).padStart(2, "0");

const VIEW_NAME = { area: "", general: "Vista general", float: "Custom Float" };
const EVENT_TONE = { "jde-error": "bad", "anuncios-error": "bad", "jde-ok": "good", "anuncios-ok": "good" };

let deps = null; // { api, toast, confirmDialog, publishedVersion }
let data = { configured: true, screens: [], views: {}, onlineSeconds: 720, serverTime: null };
let selected = null;

function stamp(iso) {
  const d = new Date(iso);
  const today = new Date();
  const time = `${pad(d.getHours())}:${pad(d.getMinutes())}`;
  return d.toDateString() === today.toDateString() ? time : `${pad(d.getDate())}/${pad(d.getMonth() + 1)} ${time}`;
}

function ago(iso) {
  const seconds = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 1000));
  if (seconds < 90) return "hace un momento";
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `hace ${minutes} min`;
  const hours = Math.round(minutes / 60);
  if (hours < 48) return `hace ${hours} h`;
  return `hace ${Math.round(hours / 24)} días`;
}

/** Numeric compare of "1.0.10" vs "1.0.9". */
function compareVersions(a, b) {
  const x = String(a).split(".").map(Number);
  const y = String(b).split(".").map(Number);
  for (let i = 0; i < Math.max(x.length, y.length); i++) {
    const d = (x[i] ?? 0) - (y[i] ?? 0);
    if (d) return d;
  }
  return 0;
}

const latestVersion = () => data.screens.map((s) => s.report.appVersion).filter(Boolean).sort(compareVersions).at(-1) ?? "";
const nameOf = (s) => s.label || s.report.machine || s.id;

/** online / warn (online with a problem) / off, and the problem in words. */
export function healthOf(screen, publishedVersion) {
  const online = Date.now() - new Date(screen.lastSeen).getTime() <= data.onlineSeconds * 1000;
  const r = screen.report;
  const problems = [];
  const failing = r.jde.sources.filter((x) => x.error).map((x) => x.name);
  if (r.mode === "real" && failing.length) problems.push(r.jde.needsPassword ? "Falta la contraseña de JDE" : `Sin JDE: ${failing.join(", ")}`);
  // In demo mode the screen reads simulated announcements: nothing to compare with what is published
  if (r.mode === "demo") problems.push("En modo demo");
  else if (!r.announcements.feedUrl) problems.push("Anuncios sin configurar");
  else if (r.announcements.lastError) problems.push(`Anuncios: ${r.announcements.lastError}`);
  else if (publishedVersion && r.announcements.version && r.announcements.version < publishedVersion) problems.push("Anuncios atrasados");
  return { online, problems, state: !online ? "off" : problems.length ? "warn" : "ok" };
}

function el(tag, props = {}, ...children) {
  const node = Object.assign(document.createElement(tag), props);
  for (const child of children) if (child !== null && child !== undefined) node.append(child);
  return node;
}

function row(label, value, className = "") {
  return el("div", { className: "screen-row" }, el("span", { className: "k", textContent: label }), el("span", { className, textContent: value }));
}

function render() {
  const publishedVersion = deps.publishedVersion();
  const latest = latestVersion();
  const list = $("screens");
  list.replaceChildren();
  $("screens-missing").hidden = data.configured;
  $("screens-empty").hidden = !data.configured || data.screens.length > 0;

  let online = 0, warn = 0, off = 0; // online counts every screen with signal; warn is a part of it
  for (const s of data.screens) {
    const h = healthOf(s, publishedVersion);
    if (h.state === "off") off++;
    else online++;
    if (h.state === "warn") warn++;
    const r = s.report;

    const card = el("button", { type: "button", className: `screen-card ${h.state === "ok" ? "" : h.state}` });
    card.addEventListener("click", () => openDetail(s.id));
    const dot = el("span", { className: `state-dot ${h.state === "off" ? "off" : h.state === "warn" ? "warn" : ""}` });
    const head = el("div", { className: "screen-head" }, dot, el("span", { className: "screen-name", textContent: nameOf(s) }));
    if (r.mode === "demo") head.append(el("span", { className: "badge badge-demo", textContent: "DEMO" }));
    if (latest && compareVersions(r.appVersion, latest) < 0) head.append(el("span", { className: "badge badge-old", textContent: "Versión anterior" }));

    const sub = el("div", { className: "screen-sub", textContent: `${s.label ? r.machine + " · " : ""}planta ${r.branch || "—"} · versión ${r.appVersion}` });

    const showing = r.screen.view === "area" ? r.screen.areaName || r.screen.area : VIEW_NAME[r.screen.view] ?? r.screen.view;
    const failing = r.jde.sources.filter((x) => x.error);
    const jdeText = r.mode === "demo" ? "Modo demo (datos inventados)"
      : failing.length ? (r.jde.needsPassword ? "Falta la contraseña" : `Sin conexión: ${failing.map((x) => x.name).join(", ")}`)
      : "Conectado";
    const annText = r.mode === "demo" ? "Simulados (modo demo)"
      : !r.announcements.feedUrl ? "Sin configurar"
      : r.announcements.lastError ? r.announcements.lastError
      : publishedVersion && r.announcements.version < publishedVersion ? "Todavía con la versión anterior"
      : r.announcements.active.length ? `Al día · ${r.announcements.active.length} en pantalla` : "Al día";

    const rows = el("div", { className: "screen-rows" },
      row("Mostrando", `${showing}${r.screen.fullScreen ? " · pantalla completa" : ""}${r.screen.autoRotate ? " · rotando" : ""}`),
      row("JDE", jdeText, r.mode === "demo" ? "warn-text" : failing.length ? "danger-text" : "ok-text"),
      row("Anuncios", annText, r.mode === "demo" ? "warn-text" : !r.announcements.feedUrl || r.announcements.lastError ? "danger-text" : annText.startsWith("Todavía") ? "warn-text" : "ok-text"),
    );

    const foot = el("div", { className: "screen-foot" },
      el("span", { textContent: h.online ? `Reportó ${ago(s.lastSeen)}` : `Sin señal desde ${stamp(s.lastSeen)} (${ago(s.lastSeen)})` }),
      el("span", { textContent: `Encendida desde ${stamp(r.startedAt)}` }),
    );
    card.append(head, sub, rows, foot);
    list.append(card);
  }

  const total = data.screens.length;
  $("screens-summary").textContent = !data.configured
    ? "Las pantallas todavía no pueden reportar."
    : total === 0 ? "Ninguna pantalla ha reportado todavía." : `${total} pantalla(s). Reportan cada 5 minutos; sin señal después de ${Math.round(data.onlineSeconds / 60)} minutos.`;
  const counters = $("screens-counters");
  counters.replaceChildren();
  if (total) {
    for (const [count, label, cls] of [[online, "en línea", ""], [warn, "con avisos", "warn"], [off, "sin señal", "off"]]) {
      if (!count && cls) continue;
      counters.append(el("span", { className: "counter-chip" }, el("span", { className: `state-dot ${cls}` }), `${count} ${label}`));
    }
  }
  $("screens-updated").textContent = data.serverTime ? `Actualizado ${stamp(data.serverTime)}` : "";
  $("screens-alert").hidden = !(warn || off);
  if (selected && $("screen-dialog").open) fillDetail(selected);
}

function fillDetail(id) {
  const s = data.screens.find((x) => x.id === id);
  if (!s) return;
  const r = s.report;
  const h = healthOf(s, deps.publishedVersion());
  $("screen-title").textContent = nameOf(s);
  const facts = [
    ["Estado", h.online ? (h.problems.length ? `En línea, con avisos: ${h.problems.join(" · ")}` : "En línea, todo bien") : `Sin señal desde ${stamp(s.lastSeen)}`],
    ["PC", `${r.machine} · planta ${r.branch || "—"}`],
    ["Versión", `${r.appVersion}${r.mode === "demo" ? " · modo demo" : ""}`],
    ["Sistema", r.os],
    ["Pantalla", `${r.screen.resolution}${r.screen.fullScreen ? " · pantalla completa" : " · en ventana"} · idioma ${r.screen.language}${r.screen.autoRotate ? " · cambio automático" : ""}`],
    ["Mostrando", r.screen.view === "area" ? `${r.screen.areaName} (${r.screen.area})` : VIEW_NAME[r.screen.view] ?? r.screen.view],
    ...r.jde.sources.map((x) => [`JDE · ${x.name}`, x.error ? `ERROR ${x.errorAt ? stamp(x.errorAt) : ""}: ${x.error}` : x.lastSuccess ? `Correcto (${stamp(x.lastSuccess)})` : "Sin consultar"]),
    ["Anuncios", r.announcements.feedUrl ? `versión ${r.announcements.version || "—"}${r.announcements.lastCheck ? ` · consultado ${stamp(r.announcements.lastCheck)}` : ""}${r.announcements.lastError ? ` · ERROR: ${r.announcements.lastError}` : ""}` : "Sin dirección configurada"],
    ["Encendida desde", stamp(r.startedAt)],
    ["Primer reporte", stamp(s.firstSeen)],
    ["Último reporte", `${stamp(s.lastSeen)} (${ago(s.lastSeen)})`],
    ["Id", s.id],
  ];
  $("screen-facts").replaceChildren(...facts.flatMap(([k, v]) => [el("dt", { textContent: k }), el("dd", { textContent: v })]));
  const events = $("screen-events");
  events.replaceChildren();
  if (!s.events.length) events.append(el("li", {}, el("span", { className: "when", textContent: "—" }), "Sin eventos todavía."));
  for (const e of s.events) {
    events.append(el("li", { className: EVENT_TONE[e.kind] ?? "" }, el("span", { className: "when", textContent: stamp(e.at) }), e.text));
  }
}

function openDetail(id) {
  selected = id;
  fillDetail(id);
  $("screen-dialog").showModal();
}

async function rename() {
  const s = data.screens.find((x) => x.id === selected);
  if (!s) return;
  const dialog = $("rename-dialog");
  $("rename-input").value = s.label;
  dialog.returnValue = "";
  dialog.addEventListener("close", async () => {
    if (dialog.returnValue !== "yes") return;
    try {
      await deps.api("/api/screens", { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ action: "rename", id: s.id, label: $("rename-input").value }) });
      await load();
      deps.toast("Nombre guardado.", "ok");
    } catch (error) {
      deps.toast(error.message, "error");
    }
  }, { once: true });
  dialog.showModal();
  $("rename-input").focus();
}

async function remove() {
  const s = data.screens.find((x) => x.id === selected);
  if (!s) return;
  const ok = await deps.confirmDialog("Quitar pantalla", `«${nameOf(s)}» desaparece del panel. Si sigue encendida, vuelve a aparecer con su siguiente reporte.`, "Quitar");
  if (!ok) return;
  try {
    await deps.api("/api/screens", { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ action: "remove", id: s.id }) });
    $("screen-dialog").close();
    await load();
  } catch (error) {
    deps.toast(error.message, "error");
  }
}

/** Loads /api/screens; returns the data (also used for «visto en X de Y» in the announcements list). */
export async function load() {
  data = await deps.api("/api/screens");
  render();
  return data;
}

export const screensData = () => data;

export function initScreens(dependencies) {
  deps = dependencies;
  $("screens-refresh").addEventListener("click", () => load().catch((e) => deps.toast(e.message, "error")));
  $("screen-rename").addEventListener("click", rename);
  $("screen-remove").addEventListener("click", remove);
  $("screen-dialog").addEventListener("close", () => (selected = null));
  $("rename-cancel").addEventListener("click", () => $("rename-dialog").close("no"));
}

export const rerender = () => deps && render();
