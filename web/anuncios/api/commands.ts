import { fail, guard, json, safe } from "../lib/http.js";
import { verifyCommand } from "../lib/remote.js";
import { addCommand, pendingCommands } from "../lib/store.js";

/** GET → the commands still in date (what each screen did with them comes in its report). */
export const GET = safe(async (request) => {
  const denied = guard(request);
  if (denied) return denied;
  const pending = await pendingCommands();
  const commands = pending.map((c) => JSON.parse(Buffer.from(c.payload, "base64").toString("utf8")));
  return json({ commands, serverTime: new Date().toISOString() });
});

/**
 * POST a signed command (signed in the browser). It goes into the control file every screen reads each minute,
 * only if the app would accept it: trusted key, valid signature, known action and a short life.
 */
export const POST = safe(async (request) => {
  const denied = guard(request, true);
  if (denied) return denied;
  const check = verifyCommand(await request.text());
  if (!check.ok) return fail(check.status, check.error, check.details);
  if (Date.parse(check.value.expiresAt) <= Date.now()) return fail(422, "El comando ya venció: revisa la hora de esta PC.");
  if (Math.abs(Date.parse(check.value.issuedAt) - Date.now()) > 5 * 60_000) return fail(422, "La hora de esta PC está desfasada más de 5 minutos.");
  await addCommand(check.signed);
  console.log(`Comando ${check.value.action}${check.value.view ? ` ${check.value.view}` : ""} para ${check.value.targets.length ? check.value.targets.join(", ") : "todas"}`);
  return json({ ok: true, id: check.value.id });
});
