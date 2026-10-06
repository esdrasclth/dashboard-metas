// Panel de anuncios de Dashboard Metas.
// Flow: the draft (list of announcements) lives in this browser until «Publicar»; publishing signs the whole list
// here with the private key (never sent) and the server stores it only if the app would accept it.

import { MAX_ANNOUNCEMENTS, MAX_MESSAGE, MAX_TITLE, validateAnnouncement, validateFeed } from "./rules.js";
import { importPrivateKeyPem, signFeed } from "./signer.js";
import { forgetKey, loadKey, saveKey } from "./keystore.js";
import { initScreens, load as loadScreens, rerender as rerenderScreens, screensData } from "./screens.js";
import { initVersions, load as loadVersions } from "./versions.js";
import { initRemote, load as loadMetas, refreshData, restart, setTrusted, showOn } from "./remote-ui.js";

const $ = (id) => document.getElementById(id);
const DRAFT_KEY = "anuncios.borrador.v1";
const VIEW_KEY = "anuncios.vista";
const SHIFT_END = { hours: 16, minutes: 10 };

const state = {
  published: null, // { version, issuedAt, keyId, announcements }
  trustedKeys: [],
  feedUrl: "",
  draft: [], // announcements being edited (what «Publicar» will sign)
  baseVersion: 0, // published version the draft started from
  filter: "todos",
  selectedId: null, // id of the announcement open in the editor (null + editing = new)
  editing: null, // working copy in the editor
  key: null, // { privateKey, keyId }
  busy: false,
  view: "anuncios", // "pantallas", "versiones"
};

// ---------------------------------------------------------------- helpers

const pad = (n) => String(n).padStart(2, "0");

/** Local time with offset, as the app expects: 2026-10-06T06:00:00-06:00 */
function isoLocal(date) {
  const minutes = -date.getTimezoneOffset();
  const sign = minutes >= 0 ? "+" : "-";
  const abs = Math.abs(minutes);
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}:00${sign}${pad(Math.floor(abs / 60))}:${pad(abs % 60)}`;
}

/** ISO → value of <input type="datetime-local"> */
function toInput(iso) {
  if (!iso) return "";
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? "" : `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

function fromInput(value) {
  if (!value) return undefined;
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? undefined : isoLocal(d);
}

const DAYS = ["dom", "lun", "mar", "mié", "jue", "vie", "sáb"];
const DAYS_EN = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

function shortDate(iso, english = false) {
  const d = new Date(iso);
  const time = `${pad(d.getHours())}:${pad(d.getMinutes())}`;
  return english ? `${DAYS_EN[d.getDay()]} ${pad(d.getMonth() + 1)}/${pad(d.getDate())} ${time}` : `${DAYS[d.getDay()]} ${pad(d.getDate())}/${pad(d.getMonth() + 1)} ${time}`;
}

function relative(iso) {
  const seconds = Math.round((Date.now() - new Date(iso).getTime()) / 1000);
  if (seconds < 60) return "hace un momento";
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `hace ${minutes} min`;
  const hours = Math.round(minutes / 60);
  if (hours < 48) return `hace ${hours} h`;
  return shortDate(iso);
}

function statusOf(a, now = Date.now()) {
  if (a.startsAt && new Date(a.startsAt).getTime() > now) return "programado";
  if (a.endsAt && new Date(a.endsAt).getTime() <= now) return "vencido";
  return "activo";
}

const STATUS_LABEL = { activo: "Activo", programado: "Programado", vencido: "Vencido" };
const STATUS_CLASS = { activo: "badge-active", programado: "badge-scheduled", vencido: "badge-expired" };
const SEVERITY_LABEL = { Info: "Anuncio", Warning: "Aviso", Critical: "Urgente" };

function slug(text) {
  return (text || "anuncio")
    .normalize("NFD").replace(/[\u0300-\u036f]/g, "")
    .toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "")
    .slice(0, 40) || "anuncio";
}

/** Unique id from the title and the start date (an id seen and closed on a PC is never shown there again). */
function newId(title, startsAt, taken) {
  const d = startsAt ? new Date(startsAt) : new Date();
  const base = `${slug(title)}-${d.getFullYear()}${pad(d.getMonth() + 1)}${pad(d.getDate())}`;
  let id = base;
  for (let i = 2; taken.has(id.toLowerCase()); i++) id = `${base}-${i}`;
  return id;
}

/** Exactly the JSON the app reads (no empty optional fields). */
function clean(a) {
  const out = {
    id: a.id.trim(),
    title: a.title.trim(),
    message: a.message.replace(/\r\n/g, "\n").trim(),
    severity: a.severity,
    display: a.display,
    endsAt: a.endsAt,
    targets: (a.targets || []).map((t) => t.trim()).filter(Boolean),
    dismissible: !!a.dismissible,
    sound: !!a.sound,
  };
  if (a.startsAt) out.startsAt = a.startsAt;
  if (a.titleEn?.trim()) out.titleEn = a.titleEn.trim();
  if (a.messageEn?.trim()) out.messageEn = a.messageEn.replace(/\r\n/g, "\n").trim();
  return out;
}

const same = (x, y) => JSON.stringify(x) === JSON.stringify(y);

function toast(message, kind = "") {
  const el = document.createElement("div");
  el.className = `toast ${kind}`;
  el.textContent = message;
  $("toasts").append(el);
  setTimeout(() => el.remove(), kind === "error" ? 9000 : 5000);
}

async function api(path, options = {}) {
  const response = await fetch(path, {
    credentials: "same-origin",
    ...options,
    headers: { "x-anuncios": "1", ...(options.headers || {}) },
  });
  let body = null;
  try {
    body = await response.json();
  } catch {
    /* no body */
  }
  if (response.status === 401 && path !== "/api/login") {
    showLogin();
    throw new Error(body?.error || "La sesión venció: vuelve a entrar.");
  }
  if (!response.ok) {
    const error = new Error(body?.error || `Error ${response.status}`);
    error.details = body?.details;
    throw error;
  }
  return body;
}

function confirmDialog(title, text, yes) {
  return new Promise((resolve) => {
    const dialog = $("confirm-dialog");
    $("confirm-title").textContent = title;
    $("confirm-text").textContent = text;
    $("confirm-yes").textContent = yes;
    dialog.returnValue = "";
    dialog.addEventListener("close", () => resolve(dialog.returnValue === "yes"), { once: true });
    dialog.showModal();
  });
}

// ---------------------------------------------------------------- draft (kept in this browser)

function saveDraft() {
  try {
    localStorage.setItem(DRAFT_KEY, JSON.stringify({ baseVersion: state.baseVersion, announcements: state.draft }));
  } catch {
    /* private mode: the draft lives only in this tab */
  }
}

function loadDraft() {
  try {
    const saved = JSON.parse(localStorage.getItem(DRAFT_KEY) || "null");
    if (saved && Array.isArray(saved.announcements)) return saved;
  } catch {
    /* ignore */
  }
  return null;
}

const publishedList = () => state.published?.announcements ?? [];
const hasChanges = () => !same(state.draft.map(clean), publishedList().map(clean));

// ---------------------------------------------------------------- views

function showLogin() {
  $("app").hidden = true;
  $("login").hidden = false;
  $("login-password").focus();
}

function renderKey() {
  const chip = $("key-chip");
  if (state.key) {
    chip.className = "chip chip-key ok";
    chip.textContent = `Clave ${state.key.keyId.slice(0, 8)}…`;
    chip.title = "Clave de firma cargada en este navegador";
  } else {
    chip.className = "chip chip-key missing";
    chip.textContent = "Cargar clave de firma";
    chip.title = "Sin clave no se puede publicar";
  }
  const status = $("key-status");
  status.className = `key-status ${state.key ? "ok" : ""}`;
  status.textContent = state.key
    ? `Clave cargada: ${state.key.keyId}${state.trustedKeys.includes(state.key.keyId) ? " (aceptada por la app)" : ""}`
    : "No hay clave cargada en este navegador.";
  $("key-forget").hidden = !state.key;
}

function renderStatus() {
  const p = state.published;
  $("status-published").textContent = p
    ? `Publicado ${relative(p.issuedAt)} (${shortDate(p.issuedAt)}) · ${p.announcements.length} anuncio(s), ${p.announcements.filter((a) => statusOf(a) === "activo").length} activo(s) ahora · versión ${p.version}`
    : "Todavía no se ha publicado nada. Las pantallas no muestran anuncios.";
  $("feed-url").textContent = state.feedUrl;
  const changes = hasChanges();
  const outdated = changes && state.published && state.baseVersion && state.baseVersion !== state.published.version;
  $("draft-badge").hidden = !changes;
  $("draft-badge").textContent = outdated ? "Borrador basado en una versión anterior" : "Cambios sin publicar";
  $("discard").hidden = !changes;
  $("publish").disabled = state.busy || !changes;
  $("publish").textContent = state.busy ? "Publicando…" : "Publicar";
}

function renderList() {
  const list = $("list");
  list.replaceChildren();
  const now = Date.now();
  const published = new Map(publishedList().map((a) => [a.id, a]));
  const items = state.draft
    .map((a) => ({ a, status: statusOf(a, now) }))
    .filter(({ status }) => state.filter === "todos" || status === state.filter)
    .sort((x, y) => ["activo", "programado", "vencido"].indexOf(x.status) - ["activo", "programado", "vencido"].indexOf(y.status) ||
      new Date(y.a.startsAt || 0) - new Date(x.a.startsAt || 0));

  for (const { a, status } of items) {
    const li = document.createElement("li");
    const button = document.createElement("button");
    button.type = "button";
    button.className = `item sev-${a.severity}${a.id === state.selectedId ? " selected" : ""}`;
    button.addEventListener("click", () => openEditor(a.id));

    const top = document.createElement("div");
    top.className = "item-top";
    const badge = document.createElement("span");
    badge.className = `badge ${STATUS_CLASS[status]}`;
    badge.textContent = STATUS_LABEL[status];
    const title = document.createElement("span");
    title.className = "item-title";
    title.textContent = a.title;
    top.append(badge, title);
    const original = published.get(a.id);
    if (!original || !same(clean(original), clean(a))) {
      const draft = document.createElement("span");
      draft.className = "badge badge-draft";
      draft.textContent = original ? "Modificado" : "Nuevo";
      top.append(draft);
    }

    const message = document.createElement("div");
    message.className = "item-message";
    message.textContent = a.message;

    const meta = document.createElement("div");
    meta.className = "item-meta";
    const targets = a.targets?.length ? a.targets.join(", ") : "Todas las pantallas";
    for (const text of [
      `${SEVERITY_LABEL[a.severity]} · ${a.display === "Modal" ? "tarjeta" : "franja"}`,
      `${a.startsAt ? shortDate(a.startsAt) : "Al publicar"} → ${shortDate(a.endsAt)}`,
      targets,
      a.dismissible ? null : "No se puede cerrar",
      a.sound ? "Con sonido" : null,
    ]) {
      if (!text) continue;
      const span = document.createElement("span");
      span.textContent = text;
      meta.append(span);
    }
    button.append(top, message, meta);
    const seen = viewsLine(a.id);
    if (seen) button.append(seen);
    li.append(button);
    list.append(li);
  }

  // Announcements removed from the draft that are still published
  const removed = publishedList().filter((p) => !state.draft.some((a) => a.id === p.id));
  const emptyText = state.draft.length === 0
    ? "No hay anuncios. Crea uno con «+ Nuevo anuncio»."
    : items.length === 0 ? "Ninguno en este filtro." : "";
  $("list-empty").hidden = !emptyText;
  $("list-empty").textContent = emptyText;
  const expired = state.draft.filter((a) => statusOf(a, now) === "vencido").length;
  $("remove-expired").hidden = expired === 0;
  $("remove-expired").textContent = `Quitar vencidos (${expired})`;
  $("list-count").textContent = `${state.draft.length} de ${MAX_ANNOUNCEMENTS}` +
    (removed.length ? ` · ${removed.length} se retirará(n) al publicar` : "");
  $("new").disabled = state.draft.length >= MAX_ANNOUNCEMENTS;

  for (const tab of document.querySelectorAll(".tab")) tab.setAttribute("aria-selected", String(tab.dataset.filter === state.filter));
}

/** «Visto en 4 de 6 pantallas · cerrado en 1» for a published announcement (screens that report). */
function viewsLine(id) {
  const data = screensData();
  if (!data.configured || !data.screens.length || !publishedList().some((p) => p.id === id)) return null;
  const v = data.views[id] ?? { seen: [], dismissed: [] };
  const known = new Set(data.screens.map((s) => s.id));
  const seen = v.seen.filter((x) => known.has(x)).length;
  const dismissed = v.dismissed.filter((x) => known.has(x)).length;
  const line = document.createElement("div");
  line.className = "views-line";
  line.append("Visto en ", Object.assign(document.createElement("b"), { textContent: `${seen} de ${data.screens.length}` }), " pantallas");
  if (dismissed) line.append(` · cerrado en ${dismissed}`);
  return line;
}

function render() {
  renderStatus();
  renderList();
  renderKey();
}

function showView(view) {
  state.view = ["pantallas", "metas", "versiones"].includes(view) ? view : "anuncios";
  try {
    localStorage.setItem(VIEW_KEY, state.view);
  } catch {
    /* ignore */
  }
  $("view-anuncios").hidden = state.view !== "anuncios";
  $("view-pantallas").hidden = state.view !== "pantallas";
  $("view-versiones").hidden = state.view !== "versiones";
  $("view-metas").hidden = state.view !== "metas";
  for (const tab of document.querySelectorAll(".view-tab")) tab.setAttribute("aria-selected", String(tab.dataset.view === state.view));
  if (state.view === "pantallas") loadScreens().catch((e) => toast(e.message, "error"));
  if (state.view === "versiones") loadVersions().catch((e) => toast(e.message, "error"));
  if (state.view === "metas") loadMetas().catch((e) => toast(e.message, "error"));
}

// ---------------------------------------------------------------- editor

function blankAnnouncement() {
  const now = new Date();
  now.setSeconds(0, 0);
  const ends = new Date(now);
  ends.setDate(ends.getDate() + 1);
  return {
    id: "",
    title: "",
    message: "",
    titleEn: "",
    messageEn: "",
    severity: "Info",
    display: "Modal",
    startsAt: isoLocal(now),
    endsAt: isoLocal(ends),
    targets: [],
    dismissible: true,
    sound: false,
  };
}

function openEditor(id) {
  const existing = id ? state.draft.find((a) => a.id === id) : null;
  state.selectedId = existing ? existing.id : null;
  state.editing = structuredClone(existing ?? blankAnnouncement());
  state.editing.idTouched = !!existing;
  fillEditor();
  $("editor").hidden = false;
  $("editor-empty").hidden = true;
  renderList();
  if (window.matchMedia("(max-width: 980px)").matches) $("editor").scrollIntoView({ behavior: "smooth", block: "start" });
  $("f-title").focus();
}

function closeEditor() {
  state.selectedId = null;
  state.editing = null;
  $("editor").hidden = true;
  $("editor-empty").hidden = false;
  renderList();
}

function setRadio(name, value) {
  for (const input of document.querySelectorAll(`input[name="${name}"]`)) input.checked = input.value === value;
}

function fillEditor() {
  const e = state.editing;
  const isNew = !state.selectedId;
  const published = publishedList().find((a) => a.id === state.selectedId);
  $("editor-title").textContent = isNew ? "Nuevo anuncio" : "Editar anuncio";
  $("f-title").value = e.title;
  $("f-message").value = e.message;
  $("f-title-en").value = e.titleEn || "";
  $("f-message-en").value = e.messageEn || "";
  $("f-id").value = e.id;
  $("f-starts").value = toInput(e.startsAt);
  $("f-ends").value = toInput(e.endsAt);
  setRadio("severity", e.severity);
  setRadio("display", e.display);
  setRadio("audience", e.targets.length ? "some" : "all");
  $("targets-box").hidden = e.targets.length === 0 && !$("targets-box").dataset.forced;
  $("f-dismissible").checked = e.dismissible;
  $("f-sound").checked = e.sound;
  $("f-renew").checked = false;
  $("renew-box").hidden = !published;
  $("delete").hidden = isNew;
  $("save").textContent = isNew ? "Agregar al borrador" : "Guardar en el borrador";
  renderTargets();
  updateEditor();
}

function renderTargets() {
  const box = $("targets");
  box.replaceChildren();
  for (const target of state.editing.targets) {
    const chip = document.createElement("span");
    chip.className = "target-chip";
    chip.textContent = target;
    const remove = document.createElement("button");
    remove.type = "button";
    remove.textContent = "×";
    remove.setAttribute("aria-label", `Quitar ${target}`);
    remove.addEventListener("click", () => {
      state.editing.targets = state.editing.targets.filter((t) => t !== target);
      renderTargets();
      updateEditor();
    });
    chip.append(remove);
    box.append(chip);
  }
}

function addTarget(raw) {
  let value = raw.trim();
  if (!value) return;
  if (!value.includes(":")) value = /^\d+$/.test(value) ? `planta:${value}` : `equipo:${value.toUpperCase()}`;
  const [kind, ...rest] = value.split(":");
  value = `${kind.trim().toLowerCase()}:${rest.join(":").trim()}`;
  if (!["planta", "equipo"].includes(value.split(":")[0]) || value.endsWith(":")) {
    toast("Escribe «planta:027» o «equipo:NOMBRE-PC».", "error");
    return;
  }
  if (!state.editing.targets.some((t) => t.toLowerCase() === value.toLowerCase())) state.editing.targets.push(value);
  $("f-target").value = "";
  renderTargets();
  updateEditor();
}

/** The announcement as it would be saved (id generated when not set by hand). */
function editedAnnouncement() {
  const e = state.editing;
  const taken = new Set(state.draft.filter((a) => a.id !== state.selectedId).map((a) => a.id.toLowerCase()));
  let id = e.id.trim();
  if (!e.idTouched || !id) id = newId(e.title, e.startsAt, taken);
  if ($("f-renew").checked) id = newId(e.title, isoLocal(new Date()), new Set([...taken, ...publishedList().map((a) => a.id.toLowerCase())]));
  return clean({ ...e, id });
}

function updateEditor() {
  const e = state.editing;
  if (!e) return;
  const a = editedAnnouncement();
  if (!e.idTouched) $("f-id").value = a.id;

  for (const counter of document.querySelectorAll(".counter")) {
    const field = counter.dataset.for;
    const length = (e[field] || "").length;
    const max = field === "title" ? MAX_TITLE : MAX_MESSAGE;
    counter.textContent = `${length}/${max}`;
    counter.classList.toggle("over", length > max);
  }

  const status = statusOf(a);
  $("editor-state").className = `badge ${STATUS_CLASS[status]}`;
  $("editor-state").textContent = STATUS_LABEL[status];

  const problems = validateAnnouncement(a);
  const taken = state.draft.some((x) => x.id !== state.selectedId && x.id.toLowerCase() === a.id.toLowerCase());
  if (taken) problems.push(`El id «${a.id}» ya existe en el borrador.`);
  const errors = $("editor-errors");
  errors.replaceChildren(...problems.map((p) => Object.assign(document.createElement("li"), { textContent: p.replace(/^[^:]+: /, "") })));
  errors.hidden = problems.length === 0 || !e.showErrors;
  renderPreview(a);
  return problems;
}

function renderPreview(a) {
  const english = document.querySelector('input[name="previewLang"]:checked')?.value === "en";
  const title = (english && a.titleEn) || a.title || (english ? "Title" : "Título del anuncio");
  const message = (english && a.messageEn) || a.message || (english ? "Message" : "El mensaje aparece aquí.");
  const until = a.endsAt ? `${english ? "Showing until" : "Visible hasta el"} ${shortDate(a.endsAt, english)}` : "";
  const modal = a.display === "Modal";
  $("pv-modal").hidden = !modal;
  $("pv-banner").hidden = modal;
  $("pv-modal").className = `screen-modal sev-${a.severity}`;
  $("pv-banner").className = `screen-banner sev-${a.severity}`;
  const captions = english ? { Info: "ANNOUNCEMENT", Warning: "NOTICE", Critical: "URGENT" } : { Info: "ANUNCIO", Warning: "AVISO", Critical: "URGENTE" };
  $("pv-caption").textContent = captions[a.severity];
  $("pv-title").textContent = title;
  $("pv-message").textContent = message;
  $("pv-until").textContent = until;
  $("pv-button").textContent = a.dismissible ? (english ? "Got it" : "Entendido") : "";
  $("pv-banner-title").textContent = title;
  $("pv-banner-message").textContent = message.replace(/\s+/g, " ");
  $("pv-banner-until").textContent = until;
}

function saveEditor(event) {
  event.preventDefault();
  state.editing.showErrors = true;
  const problems = updateEditor();
  if (problems.length) {
    toast("Revisa los datos marcados en rojo.", "error");
    return;
  }
  const a = editedAnnouncement();
  if (state.selectedId) {
    state.draft = state.draft.map((x) => (x.id === state.selectedId ? a : x));
  } else {
    state.draft.push(a);
  }
  if (!state.baseVersion) state.baseVersion = state.published?.version ?? 0;
  saveDraft();
  state.selectedId = a.id;
  state.editing = { ...structuredClone(a), idTouched: true };
  fillEditor();
  render();
  toast("Guardado en el borrador. Publica para enviarlo a las pantallas.", "ok");
}

async function deleteSelected() {
  const a = state.draft.find((x) => x.id === state.selectedId);
  if (!a) return;
  const published = publishedList().some((p) => p.id === a.id);
  const ok = await confirmDialog("Eliminar anuncio", published
    ? `«${a.title}» se quitará de las pantallas al publicar.`
    : `«${a.title}» se quitará del borrador.`, "Eliminar");
  if (!ok) return;
  state.draft = state.draft.filter((x) => x.id !== a.id);
  saveDraft();
  closeEditor();
  render();
}

// ---------------------------------------------------------------- publish

async function publish() {
  if (!state.key) {
    toast("Primero carga la clave de firma.", "error");
    $("key-dialog").showModal();
    return;
  }
  if (!state.trustedKeys.includes(state.key.keyId)) {
    toast(`La clave ${state.key.keyId} no es la que acepta la app.`, "error");
    return;
  }
  const announcements = state.draft.map(clean);
  const version = Math.max(Math.floor(Date.now() / 1000), (state.published?.version ?? 0) + 1);
  const feed = { version, issuedAt: isoLocal(new Date()), announcements };
  const problems = validateFeed(feed);
  if (problems.length) {
    toast(problems[0], "error");
    return;
  }
  const active = announcements.filter((a) => statusOf(a) === "activo").length;
  const ok = await confirmDialog("Publicar", announcements.length === 0
    ? "Se publicará sin anuncios: las pantallas retirarán todos los que tengan."
    : `Se publicarán ${announcements.length} anuncio(s) (${active} activo(s) ahora). Las pantallas los tomarán en su siguiente consulta (unos 30 segundos).`, "Publicar");
  if (!ok) return;

  state.busy = true;
  renderStatus();
  try {
    const signed = await signFeed(feed, state.key.privateKey, state.key.keyId);
    await api("/api/publish", { method: "POST", headers: { "content-type": "application/json" }, body: signed });
    localStorage.removeItem(DRAFT_KEY);
    await refresh(true);
    toast(`Publicado (versión ${version}). Las pantallas lo muestran en unos 30 segundos.`, "ok");
  } catch (error) {
    toast(error.details?.length ? `${error.message} ${error.details[0]}` : error.message, "error");
  } finally {
    state.busy = false;
    renderStatus();
  }
}

// ---------------------------------------------------------------- history

async function loadHistory() {
  const list = $("history");
  list.replaceChildren(Object.assign(document.createElement("li"), { textContent: "Cargando…" }));
  try {
    const { history } = await api("/api/history");
    list.replaceChildren();
    if (!history.length) list.append(Object.assign(document.createElement("li"), { textContent: "Sin publicaciones todavía." }));
    for (const entry of history) {
      const li = document.createElement("li");
      const when = Object.assign(document.createElement("b"), { textContent: shortDate(entry.publishedAt) });
      const version = Object.assign(document.createElement("span"), { className: "muted", textContent: `versión ${entry.version}` });
      if (entry.version === state.published?.version) version.textContent += " · actual";
      const view = Object.assign(document.createElement("button"), { type: "button", className: "btn btn-small", textContent: "Ver" });
      view.addEventListener("click", () => showVersion(entry.version));
      li.append(when, version, Object.assign(document.createElement("span"), { className: "spacer" }), view);
      list.append(li);
    }
  } catch (error) {
    list.replaceChildren(Object.assign(document.createElement("li"), { textContent: error.message }));
  }
}

async function showVersion(version) {
  try {
    const feed = await api(`/api/history?version=${version}`);
    $("version-title").textContent = `Publicación del ${shortDate(feed.issuedAt)}`;
    const list = $("version-list");
    list.replaceChildren();
    if (!feed.announcements.length) list.append(Object.assign(document.createElement("li"), { textContent: "Sin anuncios (se retiraron todos)." }));
    for (const a of feed.announcements) {
      const li = document.createElement("li");
      li.append(
        Object.assign(document.createElement("b"), { textContent: a.title }),
        Object.assign(document.createElement("div"), { className: "muted", textContent: `${SEVERITY_LABEL[a.severity] ?? a.severity} · ${a.startsAt ? shortDate(a.startsAt) : "al publicar"} → ${shortDate(a.endsAt)}` }),
      );
      list.append(li);
    }
    const dialog = $("version-dialog");
    dialog.returnValue = "";
    dialog.addEventListener("close", async () => {
      if (dialog.returnValue !== "restore") return;
      if (hasChanges() && !(await confirmDialog("Usar como borrador", "Se reemplazarán los cambios sin publicar.", "Reemplazar"))) return;
      state.draft = feed.announcements.map(clean);
      state.baseVersion = state.published?.version ?? 0;
      saveDraft();
      closeEditor();
      render();
      toast("Cargado en el borrador. Revisa las fechas y publica.", "ok");
    }, { once: true });
    dialog.showModal();
  } catch (error) {
    toast(error.message, "error");
  }
}

// ---------------------------------------------------------------- key

async function onKeyFile() {
  const file = $("key-file").files?.[0];
  $("key-error").hidden = true;
  if (!file) return;
  try {
    if (file.size > 10_000) throw new Error("Ese archivo no parece una clave (es demasiado grande).");
    const { privateKey, keyId } = await importPrivateKeyPem(await file.text());
    if (!state.trustedKeys.includes(keyId)) throw new Error(`Esta clave (${keyId}) no es la que acepta la app (${state.trustedKeys.join(", ")}).`);
    state.key = { privateKey, keyId };
    if ($("key-remember").checked) await saveKey(privateKey, keyId);
    else await forgetKey();
    renderKey();
    renderStatus();
    toast("Clave cargada. Ya puedes publicar.", "ok");
  } catch (error) {
    $("key-error").textContent = error.message;
    $("key-error").hidden = false;
  } finally {
    $("key-file").value = "";
  }
}

// ---------------------------------------------------------------- startup

async function refresh(resetDraft = false) {
  const data = await api("/api/state");
  state.published = data.published;
  state.trustedKeys = data.trustedKeys;
  state.feedUrl = data.feedUrl;
  const saved = resetDraft ? null : loadDraft();
  if (saved) {
    state.draft = saved.announcements;
    state.baseVersion = saved.baseVersion;
  } else {
    state.draft = publishedList().map(clean);
    state.baseVersion = state.published?.version ?? 0;
  }
  if (state.editing && state.selectedId && !state.draft.some((a) => a.id === state.selectedId)) closeEditor();
  render();
  if ($("history-box").open) loadHistory();
  // Screens: for «visto en X de Y» and the alert dot (if the database is missing it just shows the notice)
  loadScreens().then(() => renderList()).catch(() => {});
}

async function startPanel() {
  $("login").hidden = true;
  $("app").hidden = false;
  const stored = await loadKey();
  state.key = stored ? { privateKey: stored.privateKey, keyId: stored.keyId } : null;
  let view = "anuncios";
  try {
    view = localStorage.getItem(VIEW_KEY) || "anuncios";
  } catch {
    /* ignore */
  }
  showView(view);
  await refresh();
}

function wire() {
  initScreens({ api, toast, confirmDialog, publishedVersion: () => state.published?.version ?? 0, commands: { showOn, refreshData, restart, setTrusted } });
  initRemote({ api, toast, confirmDialog, key: () => state.key, openKeyDialog: () => $("key-dialog").showModal(), screens: screensData, reloadScreens: loadScreens });
  initVersions({ api, toast, confirmDialog, key: () => state.key, openKeyDialog: () => $("key-dialog").showModal() });
  for (const tab of document.querySelectorAll(".view-tab")) tab.addEventListener("click", () => showView(tab.dataset.view));
  $("login-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    const button = $("login-button");
    button.disabled = true;
    $("login-error").hidden = true;
    try {
      await api("/api/login", { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ password: $("login-password").value }) });
      $("login-password").value = "";
      await startPanel();
    } catch (error) {
      $("login-error").textContent = error.message;
      $("login-error").hidden = false;
    } finally {
      button.disabled = false;
    }
  });
  $("logout").addEventListener("click", async () => {
    await api("/api/logout", { method: "POST" }).catch(() => {});
    showLogin();
  });

  $("new").addEventListener("click", () => openEditor(null));
  $("cancel").addEventListener("click", closeEditor);
  $("delete").addEventListener("click", deleteSelected);
  $("editor").addEventListener("submit", saveEditor);
  $("publish").addEventListener("click", publish);
  $("discard").addEventListener("click", async () => {
    if (!(await confirmDialog("Descartar cambios", "El borrador vuelve a ser igual a lo publicado.", "Descartar"))) return;
    localStorage.removeItem(DRAFT_KEY);
    closeEditor();
    await refresh(true);
  });
  $("remove-expired").addEventListener("click", () => {
    state.draft = state.draft.filter((a) => statusOf(a) !== "vencido");
    saveDraft();
    if (state.selectedId && !state.draft.some((a) => a.id === state.selectedId)) closeEditor();
    render();
  });
  $("copy-url").addEventListener("click", async () => {
    try {
      await navigator.clipboard.writeText(state.feedUrl);
      toast("Dirección copiada. Pégala en Dashboard Metas › Configuración › Anuncios.", "ok");
    } catch {
      toast(state.feedUrl);
    }
  });
  for (const tab of document.querySelectorAll(".tab")) {
    tab.addEventListener("click", () => {
      state.filter = tab.dataset.filter;
      renderList();
    });
  }

  // Editor fields → working copy
  const bind = (id, field, transform = (v) => v) => $(id).addEventListener("input", (event) => {
    if (!state.editing) return;
    state.editing[field] = transform(event.target.value);
    updateEditor();
  });
  bind("f-title", "title");
  bind("f-message", "message");
  bind("f-title-en", "titleEn");
  bind("f-message-en", "messageEn");
  bind("f-starts", "startsAt", fromInput);
  bind("f-ends", "endsAt", fromInput);
  $("f-id").addEventListener("input", (event) => {
    state.editing.id = event.target.value.trim();
    state.editing.idTouched = event.target.value.trim() !== "";
    updateEditor();
  });
  for (const name of ["severity", "display"]) {
    for (const input of document.querySelectorAll(`input[name="${name}"]`)) {
      input.addEventListener("change", () => {
        state.editing[name] = input.value;
        updateEditor();
      });
    }
  }
  for (const input of document.querySelectorAll('input[name="audience"]')) {
    input.addEventListener("change", () => {
      const some = input.value === "some";
      $("targets-box").hidden = !some;
      if (!some) {
        state.editing.targets = [];
        renderTargets();
      } else {
        $("f-target").focus();
      }
      updateEditor();
    });
  }
  $("f-target").addEventListener("keydown", (event) => {
    if (event.key === "Enter" || event.key === ",") {
      event.preventDefault();
      addTarget($("f-target").value);
    }
  });
  $("f-target").addEventListener("blur", () => addTarget($("f-target").value));
  $("f-dismissible").addEventListener("change", (event) => { state.editing.dismissible = event.target.checked; updateEditor(); });
  $("f-sound").addEventListener("change", (event) => { state.editing.sound = event.target.checked; updateEditor(); });
  $("f-renew").addEventListener("change", updateEditor);
  for (const input of document.querySelectorAll('input[name="previewLang"]')) input.addEventListener("change", updateEditor);
  for (const button of document.querySelectorAll("[data-until]")) {
    button.addEventListener("click", () => {
      const from = state.editing.startsAt ? new Date(state.editing.startsAt) : new Date();
      const until = new Date(from);
      if (button.dataset.until === "shift") {
        until.setHours(SHIFT_END.hours, SHIFT_END.minutes, 0, 0);
        if (until <= from) until.setDate(until.getDate() + 1);
      } else if (button.dataset.until === "day") {
        until.setDate(until.getDate() + 1);
      } else {
        until.setDate(until.getDate() + 7);
      }
      state.editing.endsAt = isoLocal(until);
      $("f-ends").value = toInput(state.editing.endsAt);
      updateEditor();
    });
  }

  // Key
  $("key-chip").addEventListener("click", () => {
    $("key-error").hidden = true;
    $("key-dialog").showModal();
  });
  $("key-file").addEventListener("change", onKeyFile);
  $("key-forget").addEventListener("click", async () => {
    await forgetKey();
    state.key = null;
    renderKey();
    renderStatus();
    toast("Clave olvidada en este navegador.");
  });

  $("history-box").addEventListener("toggle", () => {
    if ($("history-box").open) loadHistory();
  });

  const offset = -new Date().getTimezoneOffset();
  $("tz-hint").textContent = `Horas en la zona de este navegador (UTC${offset >= 0 ? "+" : "−"}${pad(Math.floor(Math.abs(offset) / 60))}:${pad(Math.abs(offset) % 60)}).`;

  // Status line ages every minute ("hace 3 min"), and states change (activo → vencido)
  setInterval(() => { if (!$("app").hidden) { renderStatus(); renderList(); rerenderScreens(); } }, 60_000);
  // While «Pantallas» is open its data refreshes by itself (screens report every 5 min)
  setInterval(() => {
    if ($("app").hidden || document.visibilityState !== "visible") return;
    if (state.view === "pantallas") loadScreens().catch(() => {});
    if (state.view === "versiones") loadVersions().catch(() => {});
    if (state.view === "metas") loadMetas().catch(() => {});
  }, 30_000);
}

wire();
api("/api/state")
  .then(() => startPanel())
  .catch(() => showLogin());
