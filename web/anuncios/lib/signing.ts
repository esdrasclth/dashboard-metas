import { createHash, createPublicKey, verify } from "node:crypto";
import { FORMAT, MAX_FILE_BYTES, validateFeed, type AnnouncementFeed } from "./rules.js";

// Public keys the app trusts: keep in sync with DashboardMetas.Core/Announcements/AnnouncementKeys.vb.
// Only public keys live here; the private key never leaves the publisher's browser.
export const TRUSTED_KEYS: Record<string, string> = {
  "510704804fe45c35":
    "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE0Hq0u0UV/oYvpxoGYibg+Idvx6VRdK3BMK8KpD/Wq3ORAIn0k63CrwPcT4v16NMLcfKWsZzINQuSslgvexkFAg==",
};

export { FORMAT };

export interface SignedFeed {
  format: string;
  keyId: string;
  payload: string;
  signature: string;
}

export type Verification =
  | { ok: true; feed: AnnouncementFeed; keyId: string; signed: SignedFeed }
  | { ok: false; status: number; error: string; details?: string[] };

/** Same fingerprint as AnnouncementSigning.KeyIdOf: first 8 bytes of SHA-256(SubjectPublicKeyInfo), hex. */
export function keyIdOf(spki: Buffer): string {
  return createHash("sha256").update(spki).digest().subarray(0, 8).toString("hex");
}

export type Opened = { ok: true; payload: Buffer; signed: SignedFeed } | { ok: false; status: number; error: string };

/**
 * Opens a signed file of any kind exactly like the app does: format, trusted key, ECDSA P-256 / SHA-256 signature
 * (IEEE P1363) over the payload bytes.
 */
export function openEnvelope(json: string, format: string): Opened {
  if (Buffer.byteLength(json, "utf8") > MAX_FILE_BYTES) {
    return { ok: false, status: 413, error: `El archivo pasa de ${MAX_FILE_BYTES / 1024} KB.` };
  }
  let signed: SignedFeed;
  try {
    signed = JSON.parse(json);
  } catch {
    return { ok: false, status: 400, error: "No es un JSON válido." };
  }
  if (!signed || signed.format !== format) return { ok: false, status: 400, error: `Formato no reconocido (se espera «${format}»).` };
  const publicKey = TRUSTED_KEYS[signed.keyId];
  if (!publicKey) return { ok: false, status: 403, error: `Firmado con una clave que la app no acepta (${signed.keyId || "sin id"}).` };

  const payload = Buffer.from(String(signed.payload ?? ""), "base64");
  const signature = Buffer.from(String(signed.signature ?? ""), "base64");
  let valid = false;
  try {
    const key = createPublicKey({ key: Buffer.from(publicKey, "base64"), format: "der", type: "spki" });
    valid = verify("sha256", payload, { key, dsaEncoding: "ieee-p1363" }, signature);
  } catch {
    valid = false;
  }
  if (!valid) return { ok: false, status: 403, error: "La firma no corresponde a la clave indicada." };
  return { ok: true, payload, signed };
}

/** A signed announcements file: envelope + the announcement rules. Nothing is stored unless this passes. */
export function verifySigned(json: string): Verification {
  const opened = openEnvelope(json, FORMAT);
  if (!opened.ok) return opened;
  let feed: AnnouncementFeed;
  try {
    feed = JSON.parse(opened.payload.toString("utf8"));
  } catch {
    return { ok: false, status: 400, error: "El contenido firmado no es un JSON válido." };
  }
  const problems = validateFeed(feed);
  if (problems.length > 0) return { ok: false, status: 422, error: "Hay anuncios que la app rechazaría.", details: problems };
  return { ok: true, feed, keyId: opened.signed.keyId, signed: opened.signed };
}

/** The feed inside a signed file, without checking it (only for files this service already verified). */
export function payloadOf(signed: SignedFeed): AnnouncementFeed {
  return JSON.parse(Buffer.from(signed.payload, "base64").toString("utf8"));
}
