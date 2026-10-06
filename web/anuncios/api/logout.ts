import { clearedCookie } from "../lib/auth.js";
import { json, safe } from "../lib/http.js";

export const POST = safe(async () => json({ ok: true }, 200, { "set-cookie": clearedCookie() }));
