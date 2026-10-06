// Signing in the browser with Web Crypto (also runs in Node 22 for the interoperability check).
// The private key is imported as a NON-extractable CryptoKey: once loaded, not even this page can read it back,
// and it is never sent anywhere. The signature (ECDSA P-256 / SHA-256, IEEE P1363 r||s) is the same format
// DashboardMetas verifies.

import { FORMAT } from "./rules.js";

const ALGORITHM = { name: "ECDSA", namedCurve: "P-256" };

function base64(bytes) {
  let text = "";
  const view = new Uint8Array(bytes);
  for (let i = 0; i < view.length; i += 0x8000) text += String.fromCharCode(...view.subarray(i, i + 0x8000));
  return btoa(text);
}

function fromBase64(text) {
  const binary = atob(text);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes;
}

/** Same fingerprint as the app: first 8 bytes of SHA-256(SubjectPublicKeyInfo), in hex. */
export async function keyIdOf(spki) {
  const hash = new Uint8Array(await crypto.subtle.digest("SHA-256", spki));
  return Array.from(hash.subarray(0, 8), (b) => b.toString(16).padStart(2, "0")).join("");
}

/**
 * Reads a PKCS#8 PEM P-256 private key («clave-privada-anuncios.pem»). Returns a non-extractable signing key
 * and the key id; the temporary extractable copy (needed once to derive the public key) is discarded.
 */
export async function importPrivateKeyPem(pem) {
  const match = /-----BEGIN PRIVATE KEY-----([\s\S]+?)-----END PRIVATE KEY-----/.exec(pem ?? "");
  if (!match) throw new Error("El archivo no es una clave privada PEM (debe empezar con «-----BEGIN PRIVATE KEY-----»).");
  const der = fromBase64(match[1].replace(/\s+/g, ""));
  let jwk;
  try {
    const temporary = await crypto.subtle.importKey("pkcs8", der, ALGORITHM, true, ["sign"]);
    jwk = await crypto.subtle.exportKey("jwk", temporary);
  } catch {
    throw new Error("La clave no es ECDSA P-256 o el archivo está dañado.");
  }
  const publicKey = await crypto.subtle.importKey("jwk", { kty: jwk.kty, crv: jwk.crv, x: jwk.x, y: jwk.y, ext: true }, ALGORITHM, true, ["verify"]);
  const keyId = await keyIdOf(await crypto.subtle.exportKey("spki", publicKey));
  const privateKey = await crypto.subtle.importKey("jwk", jwk, ALGORITHM, false, ["sign"]);
  jwk.d = "";
  return { privateKey, keyId };
}

/** Any payload in a signed envelope of the given format (announcements, versions…), as JSON text. */
export async function signEnvelope(format, value, privateKey, keyId) {
  const payload = new TextEncoder().encode(JSON.stringify(value));
  const signature = await crypto.subtle.sign({ name: "ECDSA", hash: "SHA-256" }, privateKey, payload);
  return JSON.stringify({ format, keyId, payload: base64(payload), signature: base64(signature) }, null, 2);
}

/** The signed control file (JSON text) for a feed: exactly what the app downloads. */
export async function signFeed(feed, privateKey, keyId) {
  return signEnvelope(FORMAT, feed, privateKey, keyId);
}
