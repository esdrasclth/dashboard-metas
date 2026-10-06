import { fail, guard, json, safe } from "../lib/http.js";
import { payloadOf } from "../lib/signing.js";
import { history, readVersion } from "../lib/store.js";

/** GET → last publications; GET ?version=N → the announcements of that publication. */
export const GET = safe(async (request) => {
  const denied = guard(request);
  if (denied) return denied;
  const version = new URL(request.url).searchParams.get("version");
  if (version === null) return json({ history: await history() });
  const number = Number(version);
  if (!Number.isSafeInteger(number) || number <= 0) return fail(400, "Versión no válida.");
  const signed = await readVersion(number);
  return signed ? json(payloadOf(signed)) : fail(404, "Esa versión no existe.");
});
