import { get, list, put } from "@vercel/blob";
import { COMMAND_FORMAT, CONFIG_FORMAT, validateCommand, validateConfig } from "../public/remote.js";
import { openEnvelope, type SignedFeed } from "./signing.js";

// «Metas y turnos» (private Blob store, never public: goals in US$ are business data):
//   metas/actual.json             the signed config the approved screens get in the reply to their report
//   metas/historial/<rev>.json    every publication
// Commands travel inside control/actual.json (see store.ts): signed, short-lived, harmless to read.
// STORE_PREFIX ("dev/" in `vercel dev`) keeps local tests away from the real screens (config only: the control
// file has no prefix, as the announcements).

export interface AreaConfig {
  code: string;
  dailyGoal: number;
  monthlyGoal: number;
  shiftStart: string;
  shiftEnd: string;
  breakStart: string;
  breakEnd: string;
}

export interface RemoteConfig {
  revision: number;
  publishedAt: string;
  areas: AreaConfig[];
}

export interface RemoteCommand {
  id: string;
  issuedAt: string;
  expiresAt: string;
  targets: string[];
  action: string;
  view?: string;
  area?: string;
  holdMinutes?: number;
}

const prefix = () => (process.env.STORE_PREFIX ?? "").replace(/^\/+/, "");
const path = (p: string) => `${prefix()}${p}`;
const CONFIG = "metas/actual.json";
const CONFIG_HISTORY = "metas/historial/";

type Checked<T> = { ok: true; value: T; text: string; signed: SignedFeed } | { ok: false; status: number; error: string; details?: string[] };

function check<T>(text: string, format: string, validate: (v: any) => string[], what: string): Checked<T> {
  const opened = openEnvelope(text, format);
  if (!opened.ok) return opened;
  let value: T;
  try {
    value = JSON.parse(opened.payload.toString("utf8"));
  } catch {
    return { ok: false, status: 400, error: "El contenido firmado no es un JSON válido." };
  }
  const problems = validate(value);
  if (problems.length) return { ok: false, status: 422, error: `${what} no es válido.`, details: problems };
  return { ok: true, value, text, signed: opened.signed };
}

export const verifyConfig = (text: string) => check<RemoteConfig>(text, CONFIG_FORMAT, validateConfig, "El archivo de metas");
export const verifyCommand = (text: string) => check<RemoteCommand>(text, COMMAND_FORMAT, validateCommand, "El comando");

async function readText(pathname: string): Promise<string | null> {
  const result = await get(path(pathname), { access: "private", useCache: false });
  if (!result || result.statusCode !== 200) return null;
  return await new Response(result.stream).text();
}

/** The config the approved screens follow (already verified when stored). */
export async function currentConfig(): Promise<{ text: string; config: RemoteConfig } | null> {
  const text = await readText(CONFIG);
  if (!text) return null;
  const c = verifyConfig(text);
  return c.ok ? { text, config: c.value } : null;
}

export async function saveConfig(text: string, config: RemoteConfig): Promise<void> {
  const options = { access: "private" as const, contentType: "application/json", addRandomSuffix: false };
  await put(path(`${CONFIG_HISTORY}${String(config.revision).padStart(12, "0")}.json`), text, { ...options, allowOverwrite: false });
  await put(path(CONFIG), text, { ...options, allowOverwrite: true, cacheControlMaxAge: 60 });
}

export async function configHistory(limit = 20): Promise<RemoteConfig[]> {
  const page = await list({ prefix: path(CONFIG_HISTORY), limit: 1000 });
  const names = page.blobs.map((b) => b.pathname).sort().reverse().slice(0, limit);
  const configs = await Promise.all(names.map(async (name) => {
    const text = await readText(name.slice(prefix().length));
    const c = text ? verifyConfig(text) : null;
    return c?.ok ? c.value : null;
  }));
  return configs.filter((c): c is RemoteConfig => !!c);
}

/** Commands still worth keeping in the control file (the screens ignore expired ones anyway). */
export function liveCommands(commands: unknown, now = Date.now()): SignedFeed[] {
  if (!Array.isArray(commands)) return [];
  return commands.filter((c): c is SignedFeed => {
    const checked = verifyCommand(JSON.stringify(c));
    return checked.ok && Date.parse(checked.value.expiresAt) > now;
  });
}
