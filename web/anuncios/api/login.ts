import { checkPassword, isSameOrigin, sessionCookie } from "../lib/auth.js";
import { fail, json, safe } from "../lib/http.js";

/** POST {password} → session cookie. A wrong password always costs ~1 s, so guessing is slow. */
export const POST = safe(async (request) => {
  if (!isSameOrigin(request)) return fail(403, "Solicitud rechazada.");
  const body = await request.json().catch(() => null);
  const password = typeof body?.password === "string" ? body.password : "";
  if (!password || password.length > 200 || !checkPassword(password)) {
    await new Promise((resolve) => setTimeout(resolve, 1000));
    return fail(401, "Contraseña incorrecta.");
  }
  return json({ ok: true }, 200, { "set-cookie": sessionCookie() });
});
