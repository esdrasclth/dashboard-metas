// «Versiones»: the version the screens follow, how far each screen got, and the changes that only need a new
// signature (pause, schedule, targets, go back). New packages are uploaded with scripts/publish-version.mjs.

import { compareVersions, INSTALL_NOW, INSTALL_OFF_SHIFT, RELEASE_FORMAT, validateManifest } from "./releases.js";
import { signEnvelope } from "./signer.js";

const $ = (id) => document.getElementById(id);
const pad = (n) => String(n).padStart(2, "0");

let deps = null; // { api, toast, confirmDialog, key: () => ({privateKey, keyId}) | null, openKeyDialog }
let data = { current: null, history: [], packages: [], screens: [] };

const STATE = {
  "al-dia": ["Al día", "ok-text"],
  "no-aplica": ["No le toca", ""],
  descargando: ["Descargando", "warn-text"],
  lista: ["Descargada, esperando", "warn-text"],
  instalando: ["Instalando", "warn-text"],
  error: ["Error", "danger-text"],
  revertida: ["Volvió a la anterior", "danger-text"],
};

function stamp(iso) {
  const d = new Date(iso);
  return `${pad(d.getDate())}/${pad(d.getMonth() + 1)} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

function isoLocal(date) {
  const m = -date.getTimezoneOffset();
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}${m >= 0 ? "+" : "-"}${pad(Math.floor(Math.abs(m) / 60))}:${pad(Math.abs(m) % 60)}`;
}

function el(tag, props = {}, ...children) {
  const node = Object.assign(document.createElement(tag), props);
  for (const child of children) if (child !== null && child !== undefined) node.append(child);
  return node;
}

function targetsText(targets) {
  return targets?.length ? targets.join(", ") : "todas las pantallas";
}

function render() {
  const c = data.current;
  $("release-actions").hidden = !c;
  $("release-notes").hidden = !c?.notes;
  if (!c) {
    $("release-summary").textContent = "Todavía no se ha publicado ninguna versión: las pantallas se quedan con la que tienen.";
  } else {
    $("release-summary").textContent =
      `Versión ${c.version} · publicada ${stamp(c.publishedAt)} · ${c.install === INSTALL_NOW ? "se instala al descargarla" : "se instala fuera de turno"} · para ${targetsText(c.targets)}` +
      (c.paused ? " · EN PAUSA" : "") + (c.allowDowngrade ? " · (versión anterior a propósito)" : "");
    $("release-notes").textContent = c.notes;
    $("release-pause").textContent = c.paused ? "Reanudar" : "Pausar";
    $("release-install").textContent = c.install === INSTALL_NOW ? "Instalar fuera de turno" : "Instalar ahora";
  }

  const list = $("rollout");
  list.replaceChildren();
  const screens = [...data.screens].sort((a, b) => (a.label || a.machine).localeCompare(b.label || b.machine));
  let done = 0;
  for (const s of screens) {
    const upToDate = c && compareVersions(s.appVersion, c.version) === 0;
    if (upToDate) done++;
    const u = s.update ?? { state: "", version: "" };
    let [label, cls] = STATE[u.state] ?? ["Sin datos (versión sin actualizaciones)", "muted"];
    if (upToDate) [label, cls] = ["Al día", "ok-text"];
    const detail = u.detail && !upToDate ? ` · ${u.detail}` : "";
    const dot = el("span", { className: `state-dot ${upToDate ? "" : u.state === "error" || u.state === "revertida" ? "danger" : "warn"}` });
    list.append(el("li", {},
      dot,
      el("span", { className: "name", textContent: s.label || s.machine, title: s.machine }),
      el("span", { className: "version", textContent: `v${s.appVersion}` }),
      el("span", { className: `state ${cls}`, textContent: `${label}${u.version && !upToDate && u.version !== s.appVersion ? ` (${u.version})` : ""}${detail}` }),
      el("span", { className: "when", textContent: `reportó ${stamp(s.lastSeen)}` }),
    ));
  }
  if (!screens.length) list.append(el("li", {}, el("span"), el("span", { className: "muted", textContent: "Ninguna pantalla ha reportado todavía." })));
  $("rollout-count").textContent = c && screens.length ? `${done} de ${screens.length} con la versión ${c.version}` : "";

  const history = $("release-history");
  history.replaceChildren();
  if (!data.history.length) history.append(el("li", { textContent: "Sin publicaciones todavía." }));
  for (const h of data.history) {
    const isCurrent = c && h.release === c.release;
    const li = el("li", {},
      el("b", { textContent: `v${h.version}` }),
      el("span", { className: "muted", textContent: `${stamp(h.publishedAt)}${h.paused ? " · en pausa" : ""}${isCurrent ? " · actual" : ""}` }),
      el("span", { className: "spacer" }),
    );
    if (c && h.version !== c.version) {
      const back = el("button", { type: "button", className: "btn btn-small", textContent: compareVersions(h.version, c.version) < 0 ? "Volver a esta" : "Usar esta" });
      back.addEventListener("click", () => goBackTo(h));
      li.append(back);
    }
    history.append(li);
  }
}

/** Signs a new manifest (based on the current one, with changes) and publishes it. */
async function publish(changes, question) {
  const key = deps.key();
  if (!key) {
    deps.toast("Primero carga la clave de firma.", "error");
    deps.openKeyDialog();
    return;
  }
  const base = changes.base ?? data.current;
  const manifest = {
    release: Math.max(Math.floor(Date.now() / 1000), (data.current?.release ?? 0) + 1),
    version: base.version,
    publishedAt: isoLocal(new Date()),
    notes: base.notes ?? "",
    file: base.file,
    install: base.install,
    targets: base.targets ?? [],
    paused: base.paused ?? false,
    allowDowngrade: base.allowDowngrade ?? false,
    ...changes.set,
  };
  const problems = validateManifest(manifest);
  if (problems.length) return deps.toast(problems[0], "error");
  if (question && !(await deps.confirmDialog(question.title, question.text, question.yes))) return;
  try {
    const signed = await signEnvelope(RELEASE_FORMAT, manifest, key.privateKey, key.keyId);
    await deps.api("/api/release", { method: "POST", headers: { "content-type": "application/json" }, body: signed });
    await load();
    deps.toast("Listo. Las pantallas se enteran en unos 30 segundos.", "ok");
  } catch (error) {
    deps.toast(error.details?.length ? `${error.message} ${error.details[0]}` : error.message, "error");
  }
}

function goBackTo(entry) {
  const older = compareVersions(entry.version, data.current.version) < 0;
  publish(
    { base: entry, set: { paused: false, allowDowngrade: older, targets: data.current.targets } },
    { title: older ? `Volver a la versión ${entry.version}` : `Usar la versión ${entry.version}`,
      text: `Las pantallas instalarán la versión ${entry.version} ${data.current.install === INSTALL_NOW ? "en cuanto la descarguen" : "fuera de turno"}.`, yes: "Firmar y publicar" },
  );
}

export async function load() {
  data = await deps.api("/api/release");
  render();
  return data;
}

export function initVersions(dependencies) {
  deps = dependencies;
  $("release-pause").addEventListener("click", () => {
    const pause = !data.current.paused;
    publish({ set: { paused: pause } }, pause
      ? { title: "Pausar la actualización", text: "Las pantallas que todavía no la instalaron esperan. Las que ya la tienen no cambian.", yes: "Pausar" }
      : { title: "Reanudar la actualización", text: `Las pantallas pendientes vuelven a descargar e instalar la versión ${data.current.version}.`, yes: "Reanudar" });
  });
  $("release-install").addEventListener("click", () => {
    const now = data.current.install !== INSTALL_NOW;
    publish({ set: { install: now ? INSTALL_NOW : INSTALL_OFF_SHIFT } }, now
      ? { title: "Instalar ahora", text: "Las pantallas la instalan en cuanto la descarguen, aunque estén en turno (la pantalla se reinicia unos segundos).", yes: "Instalar ahora" }
      : null);
  });
  $("release-targets").addEventListener("click", () => {
    const dialog = $("targets-dialog");
    $("targets-input").value = (data.current.targets ?? []).join(", ");
    dialog.returnValue = "";
    dialog.addEventListener("close", () => {
      if (dialog.returnValue !== "yes") return;
      const targets = $("targets-input").value.split(",").map((t) => t.trim()).filter(Boolean);
      publish({ set: { targets } });
    }, { once: true });
    dialog.showModal();
  });
  $("targets-cancel").addEventListener("click", () => $("targets-dialog").close("no"));
}

export const rerender = () => deps && render();
