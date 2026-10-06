// «Metas y turnos» (goals and shifts for the approved screens) and remote commands («Mostrar…», «Actualizar
// datos», «Reiniciar»). Everything is signed here, in the browser, with the publisher's key, like the announcements.

import {
  ACTION_REFRESH, ACTION_RESTART, ACTION_SHOW, AREAS, COMMAND_FORMAT, COMMAND_MINUTES, CONFIG_FORMAT, VIEWS,
  describeCommand, validateCommand, validateConfig,
} from "./remote.js";
import { signEnvelope } from "./signer.js";

const $ = (id) => document.getElementById(id);
const pad = (n) => String(n).padStart(2, "0");

let deps = null; // { api, toast, confirmDialog, key, openKeyDialog, screens: () => data, reloadScreens }
let config = { current: null, history: [] };
let commandTargets = { ids: [], label: "" };

function isoLocal(date) {
  const m = -date.getTimezoneOffset();
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}${m >= 0 ? "+" : "-"}${pad(Math.floor(Math.abs(m) / 60))}:${pad(Math.abs(m) % 60)}`;
}

function stamp(iso) {
  const d = new Date(iso);
  return `${pad(d.getDate())}/${pad(d.getMonth() + 1)} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

function el(tag, props = {}, ...children) {
  const { dataset, ...rest } = props; // dataset is read-only: copy its entries
  const node = Object.assign(document.createElement(tag), rest);
  Object.assign(node.dataset, dataset ?? {});
  for (const child of children) if (child !== null && child !== undefined) node.append(child);
  return node;
}

const money = (n) => (n > 0 ? Math.round(n).toLocaleString("en-US") : "");
const parseMoney = (text) => {
  const clean = String(text ?? "").replace(/[\s,$]/g, "");
  return clean === "" ? 0 : Number(clean);
};
const nameOf = (s) => s.label || s.report.machine || s.id;
const areaName = (code) => AREAS.find(([c]) => c === code)?.[1] ?? code;

/** The key, or a hint to load it (signing needs it). */
function keyOrAsk() {
  const key = deps.key();
  if (!key) {
    deps.toast("Primero carga la clave de firma.", "error");
    deps.openKeyDialog();
  }
  return key;
}

// ---------------------------------------------------------------- commands

async function send(command, question) {
  const key = keyOrAsk();
  if (!key) return false;
  const now = new Date();
  const full = {
    id: crypto.randomUUID().replaceAll("-", ""),
    issuedAt: isoLocal(now),
    expiresAt: isoLocal(new Date(now.getTime() + COMMAND_MINUTES * 60_000)),
    targets: commandTargets.ids,
    action: command.action,
    view: command.view ?? "",
    area: command.area ?? "",
    holdMinutes: command.holdMinutes ?? 0,
  };
  const problems = validateCommand(full);
  if (problems.length) {
    deps.toast(problems[0], "error");
    return false;
  }
  if (question && !(await deps.confirmDialog(question.title, question.text, question.yes))) return false;
  try {
    const signed = await signEnvelope(COMMAND_FORMAT, full, key.privateKey, key.keyId);
    await deps.api("/api/commands", { method: "POST", headers: { "content-type": "application/json" }, body: signed });
    deps.toast(`«${describeCommand(full, areaName)}» enviado a ${commandTargets.label}. Lo hacen en 1 minuto o menos.`, "ok");
    // The screens confirm with their next report: refresh the list a bit later
    setTimeout(() => deps.reloadScreens().catch(() => {}), 75_000);
    return true;
  } catch (error) {
    deps.toast(error.details?.length ? `${error.message} ${error.details[0]}` : error.message, "error");
    return false;
  }
}

/** ids = [] for every screen. */
function target(ids, label) {
  commandTargets = { ids, label };
}

export function refreshData(ids, label) {
  target(ids, label);
  return send({ action: ACTION_REFRESH });
}

export function restart(ids, label) {
  target(ids, label);
  return send({ action: ACTION_RESTART }, {
    title: "Reiniciar la app",
    text: `Dashboard Metas se cierra y se vuelve a abrir en ${label} (unos segundos con la pantalla en blanco).`,
    yes: "Firmar y reiniciar",
  });
}

export function showOn(ids, label) {
  target(ids, label);
  $("command-title").textContent = `Mostrar en ${label}`;
  const select = $("command-view");
  select.replaceChildren(
    el("option", { value: VIEWS.float, textContent: "Custom Float" }),
    el("option", { value: VIEWS.overview, textContent: "Vista general (todas las áreas)" }),
    ...AREAS.map(([code, name]) => el("option", { value: `area:${code}`, textContent: `${name} (${code})` })),
    el("option", { value: VIEWS.rotation, textContent: "Volver a la rotación normal" }),
  );
  syncHold();
  $("command-dialog").showModal();
}

function syncHold() {
  $("command-hold-row").hidden = $("command-view").value === VIEWS.rotation;
}

async function confirmShow(event) {
  event.preventDefault();
  const value = $("command-view").value;
  const [view, area] = value.startsWith("area:") ? [VIEWS.area, value.slice(5)] : [value, ""];
  const holdMinutes = view === VIEWS.rotation ? 0 : Number($("command-hold").value);
  if (await send({ action: ACTION_SHOW, view, area, holdMinutes })) $("command-dialog").close();
}

// ---------------------------------------------------------------- approval

export async function setTrusted(id, trusted) {
  await deps.api("/api/screens", { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ action: "trust", id, trusted }) });
  await deps.reloadScreens();
  renderScreens();
}

// ---------------------------------------------------------------- «Metas y turnos»

/** Rows of the editor: from the published config, else from the most recently seen screen. */
function startingRows() {
  const managed = new Map((config.current?.areas ?? []).map((a) => [a.code, a]));
  const screens = [...(deps.screens().screens ?? [])].sort((a, b) => b.lastSeen.localeCompare(a.lastSeen));
  const fromScreen = new Map();
  for (const s of screens) for (const a of s.report.areas ?? []) if (!fromScreen.has(a.code)) fromScreen.set(a.code, { ...a, name: a.name });
  return AREAS.map(([code, name]) => {
    const m = managed.get(code);
    const seen = fromScreen.get(code);
    const base = m ?? seen ?? { dailyGoal: 0, monthlyGoal: 0, shiftStart: "06:30", shiftEnd: "16:10", breakStart: "", breakEnd: "" };
    return { code, name: seen?.name || name, managed: !!m, ...base };
  });
}

function renderEditor() {
  const body = $("metas-rows");
  body.replaceChildren();
  for (const r of startingRows()) {
    const input = (field, value, props = {}) => el("input", { value: value ?? "", dataset: { field }, ...props });
    const tr = el("tr", { dataset: { code: r.code } },
      el("td", {}, el("input", { type: "checkbox", checked: r.managed, dataset: { field: "managed" }, title: "Administrar esta área desde el panel" })),
      el("td", {}, el("b", { textContent: r.code }), " ", el("span", { className: "muted", textContent: r.name })),
      el("td", {}, input("dailyGoal", money(r.dailyGoal), { inputMode: "numeric", className: "num", placeholder: "150,000" })),
      el("td", {}, input("monthlyGoal", money(r.monthlyGoal), { inputMode: "numeric", className: "num", placeholder: "automática" })),
      el("td", {}, input("shiftStart", r.shiftStart, { className: "time", placeholder: "06:30" })),
      el("td", {}, input("shiftEnd", r.shiftEnd, { className: "time", placeholder: "16:10" })),
      el("td", {}, input("breakStart", r.breakStart, { className: "time", placeholder: "—" })),
      el("td", {}, input("breakEnd", r.breakEnd, { className: "time", placeholder: "—" })),
    );
    body.append(tr);
  }
  syncRows();
}

/** Rows not managed are greyed out (each screen keeps its own values for them). */
function syncRows() {
  for (const tr of $("metas-rows").children) {
    const on = tr.querySelector('[data-field="managed"]').checked;
    tr.classList.toggle("off", !on);
    for (const i of tr.querySelectorAll("input:not([data-field=managed])")) i.disabled = !on;
  }
  $("metas-problems").replaceChildren(...validateConfig(build()).map((p) => el("li", { textContent: p })));
}

function build() {
  const areas = [];
  for (const tr of $("metas-rows").children) {
    if (!tr.querySelector('[data-field="managed"]').checked) continue;
    const v = (f) => tr.querySelector(`[data-field="${f}"]`).value.trim();
    areas.push({
      code: tr.dataset.code,
      dailyGoal: parseMoney(v("dailyGoal")),
      monthlyGoal: parseMoney(v("monthlyGoal")),
      shiftStart: v("shiftStart"),
      shiftEnd: v("shiftEnd"),
      breakStart: v("breakStart"),
      breakEnd: v("breakEnd"),
    });
  }
  return {
    revision: Math.max(Math.floor(Date.now() / 1000), (config.current?.revision ?? 0) + 1),
    publishedAt: isoLocal(new Date()),
    areas,
  };
}

async function publishConfig() {
  const c = build();
  const problems = validateConfig(c);
  if (problems.length) return deps.toast(problems[0], "error");
  const key = keyOrAsk();
  if (!key) return;
  const approved = (deps.screens().screens ?? []).filter((s) => s.trusted).length;
  const who = approved === 1 ? "La pantalla aprobada" : `Las ${approved} pantallas aprobadas`;
  const text = !approved
    ? "Todavía no hay pantallas aprobadas: se publica, pero ninguna TV la recibe hasta que apruebes alguna (abajo)."
    : c.areas.length
      ? `${who} ${approved === 1 ? "usará" : "usarán"} estas metas y turnos para ${c.areas.map((a) => a.code).join(", ")} en su siguiente reporte (5 min o menos). En cada TV esas áreas quedan bloqueadas en Configuración.`
      : `${who} ${approved === 1 ? "vuelve" : "vuelven"} a usar sus propias metas y turnos.`;
  if (!(await deps.confirmDialog("Publicar metas y turnos", text, "Firmar y publicar"))) return;
  try {
    const signed = await signEnvelope(CONFIG_FORMAT, c, key.privateKey, key.keyId);
    await deps.api("/api/config", { method: "POST", headers: { "content-type": "application/json" }, body: signed });
    delete $("metas-rows").dataset.dirty;
    await load();
    deps.toast(`Revisión ${c.revision} publicada.`, "ok");
  } catch (error) {
    deps.toast(error.details?.length ? `${error.message} ${error.details[0]}` : error.message, "error");
  }
}

function renderStatus() {
  const c = config.current;
  $("metas-summary").textContent = !c
    ? "El panel todavía no administra metas ni turnos: cada pantalla usa los suyos (Configuración › Áreas y metas)."
    : c.areas.length
      ? `Revisión ${c.revision} · publicada ${stamp(c.publishedAt)} · administra ${c.areas.map((a) => a.code).join(", ")}`
      : `Revisión ${c.revision} · publicada ${stamp(c.publishedAt)} · no administra ninguna área (cada pantalla usa las suyas)`;
}

function renderScreens() {
  const list = $("metas-screens");
  list.replaceChildren();
  const screens = deps.screens().screens ?? [];
  const current = config.current?.revision ?? 0;
  let applied = 0;
  for (const s of screens) {
    const cfg = s.report.config ?? { revision: 0, managed: [] };
    let state, cls;
    if (!s.trusted) [state, cls] = ["No aprobada: no recibe metas", "muted"];
    else if (cfg.error) [state, cls] = [`Rechazó las metas: ${cfg.error}`, "danger-text"];
    else if (!current) [state, cls] = ["Aprobada", "ok-text"];
    else if (cfg.revision === current) [state, cls] = [`Al día (revisión ${cfg.revision})`, "ok-text"];
    else if (!s.report.config) [state, cls] = ["Su versión no recibe metas (actualízala a la 1.3)", "warn-text"];
    else [state, cls] = ["Pendiente: la recibe en su siguiente reporte", "warn-text"];
    if (s.trusted && current && cfg.revision === current) applied++;
    const toggle = el("button", { type: "button", className: `btn btn-small ${s.trusted ? "" : "btn-primary"}`, textContent: s.trusted ? "Quitar aprobación" : "Aprobar" });
    toggle.addEventListener("click", async () => {
      if (s.trusted && !(await deps.confirmDialog("Quitar aprobación", `«${nameOf(s)}» deja de recibir metas y turnos nuevos (se queda con los últimos que recibió).`, "Quitar"))) return;
      setTrusted(s.id, !s.trusted).catch((e) => deps.toast(e.message, "error"));
    });
    list.append(el("li", {},
      el("span", { className: `state-dot ${s.trusted ? (cls === "ok-text" ? "" : "warn") : "off"}` }),
      el("span", { className: "name", textContent: nameOf(s), title: `${s.report.machine} · ${s.id}` }),
      el("span", { className: "version", textContent: `v${s.report.appVersion}` }),
      el("span", { className: `state ${cls}`, textContent: state }),
      toggle,
    ));
  }
  if (!screens.length) list.append(el("li", {}, el("span"), el("span", { className: "muted", textContent: "Ninguna pantalla ha reportado todavía." })));
  const trusted = screens.filter((s) => s.trusted).length;
  $("metas-count").textContent = screens.length ? `${trusted} de ${screens.length} ${screens.length === 1 ? "aprobada" : "aprobadas"}${current ? ` · ${applied} con la revisión ${current}` : ""}` : "";
}

export async function load() {
  const [c] = await Promise.all([deps.api("/api/config"), deps.reloadScreens()]);
  config = c;
  renderStatus();
  renderScreens();
  // Do not wipe what is being typed when the list refreshes by itself
  if (!$("metas-rows").dataset.dirty) renderEditor();
  return config;
}

export function initRemote(dependencies) {
  deps = dependencies;
  $("metas-rows").addEventListener("input", () => {
    $("metas-rows").dataset.dirty = "1";
    syncRows();
  });
  $("metas-publish").addEventListener("click", publishConfig);
  $("metas-reset").addEventListener("click", () => {
    delete $("metas-rows").dataset.dirty;
    renderEditor();
  });
  $("command-view").addEventListener("change", syncHold);
  $("command-form").addEventListener("submit", confirmShow);
  $("command-cancel").addEventListener("click", () => $("command-dialog").close());
  $("all-show").addEventListener("click", () => showOn([], "todas las pantallas"));
  $("all-refresh").addEventListener("click", () => refreshData([], "todas las pantallas"));
  $("all-restart").addEventListener("click", () => restart([], "todas las pantallas"));
}
