import { fail, guard, json, safe } from "../lib/http.js";
import { verifySigned } from "../lib/signing.js";
import { currentVersion, publish } from "../lib/store.js";

/**
 * POST the signed control file (signed in the browser). It is stored only if the app would accept it:
 * trusted key, valid signature, valid announcements and a version newer than the published one.
 */
export const POST = safe(async (request) => {
  const denied = guard(request, true);
  if (denied) return denied;
  const text = await request.text();
  const check = verifySigned(text);
  if (!check.ok) return fail(check.status, check.error, check.details);
  const published = await currentVersion();
  if (check.feed.version <= published) {
    return fail(409, `Ya hay una versión igual o más nueva publicada (${published}). Recarga y vuelve a publicar.`);
  }
  await publish(text, check.feed.version);
  console.log(`Publicada la versión ${check.feed.version} con ${check.feed.announcements.length} anuncio(s), clave ${check.keyId}`);
  return json({ ok: true, version: check.feed.version });
});
