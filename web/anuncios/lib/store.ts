import { get, list, put } from "@vercel/blob";
import { notify } from "./realtime.js";
import { liveCommands } from "./remote.js";
import { payloadOf, type SignedFeed } from "./signing.js";

// Private Vercel Blob store:
//   control/actual.json        what the screens download every 30 s: the signed announcements file plus
//                              "commands" (signed remote commands still in date) and "signals" (revision numbers of
//                              the goals and the version: a change makes the screens report right away and get them
//                              through the private reply; never the goals themselves). Overwritten on every change.
//   historial/<version>.json   every announcements publication, never overwritten
// STORE_PREFIX ("dev/" in `vercel dev`) keeps local tests away from the file the real screens read.
const prefix = () => (process.env.STORE_PREFIX ?? "").replace(/^\/+/, "");
const CURRENT = () => `${prefix()}control/actual.json`;
const HISTORY = () => `${prefix()}historial/`;

export interface Current {
  text: string;
  etag: string;
  /** The announcements envelope; payload is empty while no announcements were ever published (only commands). */
  signed: SignedFeed & { commands?: unknown; signals?: Signals };
}

/** Numbers only: they tell the screens that something new waits for them in the reply to their report. */
export interface Signals {
  config?: number;
  release?: number;
}

const hasAnnouncements = (signed: SignedFeed | null | undefined) => !!signed?.payload;

async function readText(stream: ReadableStream<Uint8Array>): Promise<string> {
  return await new Response(stream).text();
}

/** The published file, straight from storage (no CDN cache). Null when nothing was published yet. */
export async function readCurrent(ifNoneMatch?: string): Promise<Current | "not-modified" | null> {
  const result = await get(CURRENT(), { access: "private", useCache: false, ifNoneMatch });
  if (!result) return null;
  if (result.statusCode === 304) return "not-modified";
  const text = await readText(result.stream);
  return { text, etag: result.blob.etag, signed: JSON.parse(text) };
}

export async function currentVersion(): Promise<number> {
  const current = await readCurrent();
  return current && current !== "not-modified" && hasAnnouncements(current.signed) ? payloadOf(current.signed).version : 0;
}

/** The announcements published now (decoded), or null. */
export async function currentAnnouncements(): Promise<{ signed: SignedFeed; feed: ReturnType<typeof payloadOf> } | null> {
  const current = await readCurrent();
  if (!current || current === "not-modified" || !hasAnnouncements(current.signed)) return null;
  return { signed: current.signed, feed: payloadOf(current.signed) };
}

const CURRENT_OPTIONS = { access: "private" as const, contentType: "application/json", addRandomSuffix: false, allowOverwrite: true, cacheControlMaxAge: 60 };

type ControlParts = { envelope: Partial<SignedFeed>; commands: SignedFeed[]; signals: Signals };

/**
 * Reads the control file, changes one part and writes it back: announcements, commands and signals never undo each
 * other. Expired commands are dropped on every write.
 */
async function updateControl(change: (parts: ControlParts) => ControlParts, what: string): Promise<void> {
  const current = await readCurrent();
  const doc: Current["signed"] | Record<string, never> = current && current !== "not-modified" ? current.signed : {};
  const { format, keyId, payload, signature } = doc as Partial<SignedFeed>;
  const next = change({
    envelope: { format, keyId, payload, signature },
    commands: liveCommands((doc as Current["signed"]).commands),
    signals: { ...((doc as Current["signed"]).signals ?? {}) },
  });
  const announcements = next.envelope.payload ? next.envelope : {};
  await put(CURRENT(), JSON.stringify({ ...announcements, commands: next.commands, signals: next.signals }), CURRENT_OPTIONS);
  // The screens listening check it now instead of in up to 30 s
  await notify(what);
}

/** Adds a verified signed command to the control file: every screen gets it on its next check (≤ 30 s). */
export async function addCommand(command: SignedFeed, keep = 30): Promise<void> {
  await updateControl((p) => ({ ...p, commands: [...p.commands, command].slice(-keep) }), "comando");
}

/** New goals or a new version: the screens see the number change and report right away to get them. */
export async function signal(changes: Signals): Promise<void> {
  await updateControl((p) => ({ ...p, signals: { ...p.signals, ...changes } }), changes.config ? "metas" : "version");
}

/** The commands in the control file that are still in date. */
export async function pendingCommands(): Promise<SignedFeed[]> {
  const current = await readCurrent();
  return current && current !== "not-modified" ? liveCommands(current.signed.commands) : [];
}

/** History first, so a published file always has its copy. */
export async function publish(text: string, version: number): Promise<void> {
  const options = { access: "private" as const, contentType: "application/json", addRandomSuffix: false };
  await put(`${HISTORY()}${String(version).padStart(12, "0")}.json`, text, { ...options, allowOverwrite: false });
  const { format, keyId, payload, signature } = JSON.parse(text) as SignedFeed;
  await updateControl((p) => ({ ...p, envelope: { format, keyId, payload, signature } }), "anuncios");
}

export interface HistoryEntry {
  version: number;
  publishedAt: string;
  size: number;
}

export async function history(limit = 30): Promise<HistoryEntry[]> {
  const entries: HistoryEntry[] = [];
  let cursor: string | undefined;
  do {
    const page = await list({ prefix: HISTORY(), cursor, limit: 1000 });
    for (const b of page.blobs) {
      const version = Number(b.pathname.slice(HISTORY().length).replace(/\.json$/, ""));
      if (Number.isSafeInteger(version)) entries.push({ version, publishedAt: new Date(b.uploadedAt).toISOString(), size: b.size });
    }
    cursor = page.hasMore ? page.cursor : undefined;
  } while (cursor);
  return entries.sort((a, b) => b.version - a.version).slice(0, limit);
}

export async function readVersion(version: number): Promise<SignedFeed | null> {
  const result = await get(`${HISTORY()}${String(version).padStart(12, "0")}.json`, { access: "private", useCache: false });
  if (!result || result.statusCode !== 200) return null;
  return JSON.parse(await readText(result.stream));
}
