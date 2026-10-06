import { createPublicKey, verify } from "node:crypto";
import { keyIdOf } from "./signing.js";

// Screen status ("latidos"). Every Dashboard Metas PC has its own P-256 key (created on first run, kept with DPAPI)
// and signs each report with it: the device id IS the key fingerprint, so a report can only update its own screen.

export const MAX_REPORT_BYTES = 16 * 1024;
export const MAX_DEVICES = 200;
/** A screen that has not reported for this long is shown as offline (it reports every 5 min). */
export const ONLINE_SECONDS = 12 * 60;
/** Reports dated further than this from the server clock are refused (replays, wrong PC clock). */
const MAX_SKEW_MS = 15 * 60 * 1000;

export interface SourceStatus {
  name: string;
  lastSuccess?: string;
  error?: string;
  errorAt?: string;
}

/** What the app sends (camelCase JSON from DashboardMetas.App/Services/ScreenReport.vb). */
export interface DeviceReport {
  deviceId: string;
  publicKey: string;
  sentAt: string;
  startedAt: string;
  machine: string;
  branch: string;
  appVersion: string;
  os: string;
  mode: "real" | "demo";
  screen: {
    view: string;
    area: string;
    areaName: string;
    fullScreen: boolean;
    language: string;
    autoRotate: boolean;
    resolution: string;
  };
  jde: { sources: SourceStatus[]; needsPassword: boolean };
  announcements: {
    enabled: boolean;
    feedUrl: string;
    version: number;
    lastCheck?: string;
    lastError?: string;
    active: string[];
    seen: Record<string, string>;
    dismissed: Record<string, string>;
  };
  /** Automatic update on this screen: "al-dia", "descargando", "lista", "instalando", "error"… */
  update: { enabled: boolean; state: string; version: string; detail?: string; at?: string };
  /** «Metas y turnos» from the panel on this screen (revision 0 = none). */
  config: { revision: number; appliedAt?: string; managed: string[]; error?: string };
  /** Goals and shifts in use (configuration, not production figures). */
  areas: AreaSummary[];
  /** Last commands from the panel and what happened, newest first. */
  commands: CommandResult[];
  /** Instant notices (Ably SSE); null for versions without them. */
  realtime: { connected: boolean; tokenExpiresAt?: string; lastMessageAt?: string; error?: string } | null;
}

export interface AreaSummary {
  code: string;
  name: string;
  active: boolean;
  dailyGoal: number;
  monthlyGoal: number;
  shiftStart: string;
  shiftEnd: string;
  breakStart: string;
  breakEnd: string;
}

export interface CommandResult {
  id: string;
  action: string;
  at: string;
  ok: boolean;
  detail: string;
}

export interface DeviceEvent {
  at: string;
  kind: "inicio" | "version" | "jde-error" | "jde-ok" | "anuncios-error" | "anuncios-ok" | "modo" | "nuevo" | "actualizacion" | "actualizacion-error" | "metas" | "metas-error" | "comando" | "comando-error";
  text: string;
}

export interface DeviceRecord {
  id: string;
  publicKey: string;
  label: string;
  /** Approved in the panel: receives «metas y turnos» (business data). */
  trusted?: boolean;
  firstSeen: string;
  lastSeen: string;
  report: DeviceReport;
}

export type HeartbeatCheck = { ok: true; report: DeviceReport } | { ok: false; status: number; error: string };

const text = (value: unknown, max = 200): string => (typeof value === "string" ? value.slice(0, max) : "");
const iso = (value: unknown): string | undefined => {
  const s = text(value, 40);
  return s && !Number.isNaN(Date.parse(s)) ? s : undefined;
};
const amount = (value: unknown): number => (typeof value === "number" && Number.isFinite(value) && value >= 0 ? Math.min(value, 1e10) : 0);
const ids = (value: unknown): string[] =>
  Array.isArray(value) ? value.filter((v) => typeof v === "string").slice(0, 50).map((v) => v.slice(0, 64)) : [];
const stamps = (value: unknown): Record<string, string> => {
  const out: Record<string, string> = {};
  if (value && typeof value === "object") {
    for (const [k, v] of Object.entries(value as Record<string, unknown>).slice(0, 50)) {
      const at = iso(v);
      if (at) out[k.slice(0, 64)] = at;
    }
  }
  return out;
};

/** Keeps only known fields with bounded sizes: whatever a PC sends, the panel stores a small, predictable record. */
function sanitize(raw: any): DeviceReport {
  const screen = raw?.screen ?? {};
  const jde = raw?.jde ?? {};
  const ann = raw?.announcements ?? {};
  const upd = raw?.update ?? {};
  const cfg = raw?.config ?? {};
  const rt = raw?.realtime;
  return {
    deviceId: text(raw?.deviceId, 32),
    publicKey: text(raw?.publicKey, 300),
    sentAt: iso(raw?.sentAt) ?? "",
    startedAt: iso(raw?.startedAt) ?? "",
    machine: text(raw?.machine, 64),
    branch: text(raw?.branch, 12),
    appVersion: text(raw?.appVersion, 32),
    os: text(raw?.os, 120),
    mode: raw?.mode === "demo" ? "demo" : "real",
    screen: {
      view: text(screen.view, 20),
      area: text(screen.area, 12),
      areaName: text(screen.areaName, 80),
      fullScreen: screen.fullScreen === true,
      language: text(screen.language, 4),
      autoRotate: screen.autoRotate === true,
      resolution: text(screen.resolution, 20),
    },
    jde: {
      sources: (Array.isArray(jde.sources) ? jde.sources : []).slice(0, 10).map((s: any) => ({
        name: text(s?.name, 30),
        lastSuccess: iso(s?.lastSuccess),
        error: text(s?.error, 300) || undefined,
        errorAt: iso(s?.errorAt),
      })),
      needsPassword: jde.needsPassword === true,
    },
    announcements: {
      enabled: ann.enabled === true,
      feedUrl: text(ann.feedUrl, 300),
      version: Number.isSafeInteger(ann.version) ? ann.version : 0,
      lastCheck: iso(ann.lastCheck),
      lastError: text(ann.lastError, 300) || undefined,
      active: ids(ann.active),
      seen: stamps(ann.seen),
      dismissed: stamps(ann.dismissed),
    },
    update: {
      enabled: upd.enabled === true,
      state: text(upd.state, 20),
      version: text(upd.version, 20),
      detail: text(upd.detail, 300) || undefined,
      at: iso(upd.at),
    },
    config: {
      revision: Number.isSafeInteger(cfg.revision) && cfg.revision > 0 ? cfg.revision : 0,
      appliedAt: iso(cfg.appliedAt),
      managed: ids(cfg.managed).slice(0, 20).map((c) => c.slice(0, 8)),
      error: text(cfg.error, 300) || undefined,
    },
    areas: (Array.isArray(raw?.areas) ? raw.areas : []).slice(0, 20).map((a: any) => ({
      code: text(a?.code, 8),
      name: text(a?.name, 80),
      active: a?.active === true,
      dailyGoal: amount(a?.dailyGoal),
      monthlyGoal: amount(a?.monthlyGoal),
      shiftStart: text(a?.shiftStart, 5),
      shiftEnd: text(a?.shiftEnd, 5),
      breakStart: text(a?.breakStart, 5),
      breakEnd: text(a?.breakEnd, 5),
    })),
    realtime: rt && typeof rt === "object"
      ? { connected: rt.connected === true, tokenExpiresAt: iso(rt.tokenExpiresAt), lastMessageAt: iso(rt.lastMessageAt), error: text(rt.error, 300) || undefined }
      : null,
    commands: (Array.isArray(raw?.commands) ? raw.commands : []).slice(0, 10).map((c: any) => ({
      id: text(c?.id, 64),
      action: text(c?.action, 120),
      at: iso(c?.at) ?? "",
      ok: c?.ok === true,
      detail: text(c?.detail, 300),
    })),
  };
}

/**
 * Checks a report: size, the device id is the fingerprint of the public key it carries, the ECDSA P-256 / SHA-256
 * signature (IEEE P1363, header x-dm-signature) over the exact body bytes, and the date is close to the server's.
 */
export function verifyHeartbeat(body: Buffer, signatureHeader: string | null, now = Date.now()): HeartbeatCheck {
  if (body.length > MAX_REPORT_BYTES) return { ok: false, status: 413, error: "Reporte demasiado grande." };
  let raw: any;
  try {
    raw = JSON.parse(body.toString("utf8"));
  } catch {
    return { ok: false, status: 400, error: "No es un JSON válido." };
  }
  const report = sanitize(raw);
  if (!report.deviceId || !report.publicKey) return { ok: false, status: 400, error: "Falta la identidad del equipo." };

  let spki: Buffer;
  try {
    spki = Buffer.from(report.publicKey, "base64");
  } catch {
    return { ok: false, status: 400, error: "Clave del equipo no válida." };
  }
  if (keyIdOf(spki) !== report.deviceId) return { ok: false, status: 400, error: "El id no corresponde a la clave del equipo." };

  let valid = false;
  try {
    const key = createPublicKey({ key: spki, format: "der", type: "spki" });
    if (key.asymmetricKeyDetails?.namedCurve !== "prime256v1") return { ok: false, status: 400, error: "La clave del equipo debe ser P-256." };
    valid = verify("sha256", body, { key, dsaEncoding: "ieee-p1363" }, Buffer.from(signatureHeader ?? "", "base64"));
  } catch {
    valid = false;
  }
  if (!valid) return { ok: false, status: 401, error: "Firma del equipo no válida." };

  const sent = Date.parse(report.sentAt);
  if (Number.isNaN(sent) || Math.abs(now - sent) > MAX_SKEW_MS) {
    return { ok: false, status: 400, error: "La hora del equipo está desfasada más de 15 minutos." };
  }
  return { ok: true, report };
}

const failing = (r: DeviceReport) => r.jde.sources.filter((s) => s.error).map((s) => s.name).sort().join(", ");

/** What changed since the previous report, for the screen's history. */
export function eventsBetween(previous: DeviceReport | null, current: DeviceReport, at: string): DeviceEvent[] {
  const events: DeviceEvent[] = [];
  if (!previous) {
    events.push({ at, kind: "nuevo", text: `Primera conexión (${current.machine}, versión ${current.appVersion})` });
    return events;
  }
  if (previous.startedAt !== current.startedAt) events.push({ at, kind: "inicio", text: `App iniciada (${current.mode === "demo" ? "modo demo" : "JDE real"})` });
  if (previous.appVersion !== current.appVersion) events.push({ at, kind: "version", text: `Versión ${previous.appVersion} → ${current.appVersion}` });
  if (previous.mode !== current.mode) events.push({ at, kind: "modo", text: current.mode === "demo" ? "Pasó a modo demo" : "Pasó a JDE real" });
  const before = failing(previous);
  const now = failing(current);
  if (before !== now) {
    events.push(now
      ? { at, kind: "jde-error", text: `Sin conexión con JDE: ${now}${current.jde.needsPassword ? " (falta la contraseña)" : ""}` }
      : { at, kind: "jde-ok", text: "JDE respondió de nuevo" });
  }
  const upd = current.update;
  const previousUpdate = previous.update ?? { state: "", version: "" };
  if (upd.state && (upd.state !== previousUpdate.state || upd.version !== previousUpdate.version)) {
    const labels: Record<string, string> = {
      descargando: `Descargando la versión ${upd.version}`,
      lista: `Versión ${upd.version} descargada; se instala ${upd.detail ?? "fuera de turno"}`,
      instalando: `Instalando la versión ${upd.version}`,
      error: `Actualización a ${upd.version}: ${upd.detail ?? "error"}`,
      revertida: `La versión ${upd.version} no arrancó bien: se volvió a la anterior`,
    };
    if (labels[upd.state]) events.push({ at, kind: upd.state === "error" || upd.state === "revertida" ? "actualizacion-error" : "actualizacion", text: labels[upd.state] });
  }
  const cfgBefore = previous.config ?? { revision: 0, managed: [] as string[], error: undefined };
  const cfg = current.config;
  if (cfg.revision !== cfgBefore.revision && cfg.revision > 0) {
    events.push({ at, kind: "metas", text: `Metas y turnos: revisión ${cfg.revision} aplicada (${cfg.managed.length ? cfg.managed.join(", ") : "ninguna área administrada"})` });
  }
  if (cfg.error && cfg.error !== cfgBefore.error) events.push({ at, kind: "metas-error", text: `Metas y turnos rechazadas: ${cfg.error}` });
  const known = new Set((previous.commands ?? []).map((c) => c.id));
  for (const c of [...current.commands].reverse()) {
    if (!known.has(c.id)) events.push({ at, kind: c.ok ? "comando" : "comando-error", text: c.ok ? `Comando: ${c.action}${c.detail ? ` (${c.detail})` : ""}` : `Comando sin hacer: ${c.action} · ${c.detail}` });
  }
  const annBefore = previous.announcements.lastError ?? "";
  const annNow = current.announcements.lastError ?? "";
  if (annBefore !== annNow) {
    events.push(annNow ? { at, kind: "anuncios-error", text: `Anuncios: ${annNow}` } : { at, kind: "anuncios-ok", text: "Anuncios: consulta correcta" });
  }
  return events;
}
