import { get, head, issueSignedToken, list, presignUrl, put } from "@vercel/blob";
import { compareVersions, isForScreen, RELEASE_FORMAT, validateManifest } from "../public/releases.js";
import { openEnvelope } from "./signing.js";

// Versions (automatic updates) in the private Blob store:
//   versiones/paquetes/<version>-<random>.zip   installer packages (uploaded by scripts/publish-version.mjs)
//   versiones/actual.json                        the signed manifest the screens follow
//   versiones/historial/<release>.json           every published manifest
// STORE_PREFIX ("dev/" in `vercel dev`) keeps local tests away from what the real screens download.

export interface ReleaseManifest {
  release: number;
  version: string;
  publishedAt: string;
  notes: string;
  file: { pathname: string; size: number; sha256: string };
  install: "fuera-de-turno" | "ahora";
  targets: string[];
  paused: boolean;
  allowDowngrade: boolean;
}

const prefix = () => (process.env.STORE_PREFIX ?? "").replace(/^\/+/, "");
const path = (p: string) => `${prefix()}${p}`;
const CURRENT = "versiones/actual.json";
const HISTORY = "versiones/historial/";
const PACKAGES = "versiones/paquetes/";
/** Download links handed to the screens last this long (a 50 MB package downloads in minutes even on slow links). */
const LINK_MS = 2 * 60 * 60 * 1000;

export type ReleaseCheck = { ok: true; manifest: ReleaseManifest; text: string } | { ok: false; status: number; error: string; details?: string[] };

export function verifyRelease(text: string): ReleaseCheck {
  const opened = openEnvelope(text, RELEASE_FORMAT);
  if (!opened.ok) return opened;
  let manifest: ReleaseManifest;
  try {
    manifest = JSON.parse(opened.payload.toString("utf8"));
  } catch {
    return { ok: false, status: 400, error: "El contenido firmado no es un JSON válido." };
  }
  const problems = validateManifest(manifest);
  if (problems.length) return { ok: false, status: 422, error: "El manifiesto no es válido.", details: problems };
  return { ok: true, manifest, text };
}

async function readText(pathname: string): Promise<string | null> {
  const result = await get(path(pathname), { access: "private", useCache: false });
  if (!result || result.statusCode !== 200) return null;
  return await new Response(result.stream).text();
}

/** The manifest the screens follow now (already verified when it was stored). */
export async function currentRelease(): Promise<{ text: string; manifest: ReleaseManifest } | null> {
  const text = await readText(CURRENT);
  if (!text) return null;
  const check = verifyRelease(text);
  return check.ok ? { text, manifest: check.manifest } : null;
}

/** The package must exist and have the size the manifest promises. */
export async function packageMatches(pathname: string, size: number): Promise<boolean> {
  try {
    const info = await head(path(pathname));
    return info.size === size;
  } catch {
    return false;
  }
}

export async function saveRelease(text: string, manifest: ReleaseManifest): Promise<void> {
  const options = { access: "private" as const, contentType: "application/json", addRandomSuffix: false };
  await put(path(`${HISTORY}${String(manifest.release).padStart(12, "0")}.json`), text, { ...options, allowOverwrite: false });
  await put(path(CURRENT), text, { ...options, allowOverwrite: true, cacheControlMaxAge: 60 });
}

export async function releaseHistory(limit = 30): Promise<ReleaseManifest[]> {
  const page = await list({ prefix: path(HISTORY), limit: 1000 });
  const names = page.blobs.map((b) => b.pathname).sort().reverse().slice(0, limit);
  const manifests = await Promise.all(names.map(async (name) => {
    const text = await readText(name.slice(prefix().length));
    const check = text ? verifyRelease(text) : null;
    return check?.ok ? check.manifest : null;
  }));
  return manifests.filter((m): m is ReleaseManifest => !!m);
}

/** Packages uploaded so far (to go back to an earlier version without uploading it again). */
export async function packages(): Promise<{ pathname: string; size: number; uploadedAt: string }[]> {
  const page = await list({ prefix: path(PACKAGES), limit: 1000 });
  return page.blobs
    .filter((b) => b.pathname.toLowerCase().endsWith(".zip"))
    .map((b) => ({ pathname: b.pathname.slice(prefix().length), size: b.size, uploadedAt: new Date(b.uploadedAt).toISOString() }))
    .sort((a, b) => b.uploadedAt.localeCompare(a.uploadedAt));
}

/** A temporary link to download a package straight from Blob (the function never carries the 50 MB). */
export async function downloadUrl(pathname: string): Promise<{ url: string; validUntil: string }> {
  const validUntil = Date.now() + LINK_MS;
  const full = path(pathname);
  const token = await issueSignedToken({ pathname: full, operations: ["get"], validUntil });
  const { presignedUrl } = await presignUrl(token, { operation: "get", pathname: full, access: "private", validUntil });
  return { url: presignedUrl, validUntil: new Date(validUntil).toISOString() };
}

/**
 * What to offer a screen in the reply to its report: the signed manifest and a download link, only when the
 * manifest is for it, not paused, and its version differs (the app decides the rest: downgrade, blocked, when).
 */
export async function offerFor(machine: string, branch: string, appVersion: string): Promise<{ manifest: string; downloadUrl: string; validUntil: string } | null> {
  const current = await currentRelease();
  if (!current) return null;
  const m = current.manifest;
  if (m.paused || !isForScreen(m.targets, machine, branch)) return null;
  const diff = compareVersions(m.version, appVersion);
  if (diff === 0 || (diff < 0 && !m.allowDowngrade)) return null;
  const link = await downloadUrl(m.file.pathname);
  return { manifest: current.text, downloadUrl: link.url, validUntil: link.validUntil };
}
