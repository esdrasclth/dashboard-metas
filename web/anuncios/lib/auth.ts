import { createHmac, scryptSync, timingSafeEqual } from "node:crypto";

// One administrator, a password kept only as a scrypt hash (ADMIN_PASSWORD_HASH), and a signed, HttpOnly,
// SameSite=Strict session cookie (SESSION_SECRET). The password alone cannot publish: every file must also be
// signed with the private key, which never leaves the publisher's browser.

const COOKIE = "anuncios_sesion";
const SESSION_SECONDS = 12 * 60 * 60;

function secret(): Buffer {
  const value = process.env.SESSION_SECRET;
  if (!value || value.length < 32) throw new Error("Falta SESSION_SECRET en el servidor.");
  return Buffer.from(value, "utf8");
}

function b64url(data: Buffer | string): string {
  return Buffer.from(data).toString("base64url");
}

function sign(body: string): string {
  return createHmac("sha256", secret()).update(body).digest("base64url");
}

/** "scrypt:N:r:p:salt:hash" (salt and hash in base64; ":" instead of "$" so no .env loader expands it). */
export function checkPassword(password: string): boolean {
  const stored = (process.env.ADMIN_PASSWORD_HASH ?? "").trim();
  const [scheme, n, r, p, salt, hash] = stored.split(":");
  if (scheme !== "scrypt" || !salt || !hash) throw new Error("Falta ADMIN_PASSWORD_HASH en el servidor.");
  const expected = Buffer.from(hash, "base64");
  const actual = scryptSync(password.normalize("NFC"), Buffer.from(salt, "base64"), expected.length, {
    N: Number(n),
    r: Number(r),
    p: Number(p),
    maxmem: 256 * 1024 * 1024,
  });
  return actual.length === expected.length && timingSafeEqual(actual, expected);
}

export function sessionCookie(): string {
  const body = b64url(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + SESSION_SECONDS }));
  return `${COOKIE}=${body}.${sign(body)}; Path=/; HttpOnly; Secure; SameSite=Strict; Max-Age=${SESSION_SECONDS}`;
}

export function clearedCookie(): string {
  return `${COOKIE}=; Path=/; HttpOnly; Secure; SameSite=Strict; Max-Age=0`;
}

export function isAuthenticated(request: Request): boolean {
  const cookies = request.headers.get("cookie") ?? "";
  const value = cookies
    .split(";")
    .map((c) => c.trim())
    .find((c) => c.startsWith(`${COOKIE}=`))
    ?.slice(COOKIE.length + 1);
  if (!value) return false;
  const [body, mac] = value.split(".");
  if (!body || !mac) return false;
  const expected = Buffer.from(sign(body));
  const given = Buffer.from(mac);
  if (expected.length !== given.length || !timingSafeEqual(expected, given)) return false;
  try {
    const { exp } = JSON.parse(Buffer.from(body, "base64url").toString("utf8"));
    return typeof exp === "number" && exp > Date.now() / 1000;
  } catch {
    return false;
  }
}

/**
 * Requests that change something must come from this same site (the browser sends Origin on POST) and carry
 * the custom header the panel adds; with SameSite=Strict this closes cross-site request forgery.
 */
export function isSameOrigin(request: Request): boolean {
  if (request.headers.get("x-anuncios") !== "1") return false;
  const origin = request.headers.get("origin");
  if (!origin) return true;
  try {
    return new URL(origin).host === new URL(request.url).host;
  } catch {
    return false;
  }
}
