import { get, list, put } from "@vercel/blob";
import { payloadOf, type SignedFeed } from "./signing.js";

// Private Vercel Blob store:
//   control/actual.json        what the screens download (overwritten on every publication)
//   historial/<version>.json   every publication, never overwritten
const CURRENT = "control/actual.json";
const HISTORY = "historial/";

export interface Current {
  text: string;
  etag: string;
  signed: SignedFeed;
}

async function readText(stream: ReadableStream<Uint8Array>): Promise<string> {
  return await new Response(stream).text();
}

/** The published file, straight from storage (no CDN cache). Null when nothing was published yet. */
export async function readCurrent(ifNoneMatch?: string): Promise<Current | "not-modified" | null> {
  const result = await get(CURRENT, { access: "private", useCache: false, ifNoneMatch });
  if (!result) return null;
  if (result.statusCode === 304) return "not-modified";
  const text = await readText(result.stream);
  return { text, etag: result.blob.etag, signed: JSON.parse(text) };
}

export async function currentVersion(): Promise<number> {
  const current = await readCurrent();
  return current && current !== "not-modified" ? payloadOf(current.signed).version : 0;
}

/** History first, so a published file always has its copy. */
export async function publish(text: string, version: number): Promise<void> {
  const options = { access: "private" as const, contentType: "application/json", addRandomSuffix: false };
  await put(`${HISTORY}${String(version).padStart(12, "0")}.json`, text, { ...options, allowOverwrite: false });
  await put(CURRENT, text, { ...options, allowOverwrite: true, cacheControlMaxAge: 60 });
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
    const page = await list({ prefix: HISTORY, cursor, limit: 1000 });
    for (const b of page.blobs) {
      const version = Number(b.pathname.slice(HISTORY.length).replace(/\.json$/, ""));
      if (Number.isSafeInteger(version)) entries.push({ version, publishedAt: new Date(b.uploadedAt).toISOString(), size: b.size });
    }
    cursor = page.hasMore ? page.cursor : undefined;
  } while (cursor);
  return entries.sort((a, b) => b.version - a.version).slice(0, limit);
}

export async function readVersion(version: number): Promise<SignedFeed | null> {
  const result = await get(`${HISTORY}${String(version).padStart(12, "0")}.json`, { access: "private", useCache: false });
  if (!result || result.statusCode !== 200) return null;
  return JSON.parse(await readText(result.stream));
}
