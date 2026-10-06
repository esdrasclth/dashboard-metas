import { fail, guard, json, safe } from "../lib/http.js";
import { configHistory, currentConfig, saveConfig, verifyConfig } from "../lib/remote.js";

/** GET → the «metas y turnos» the approved screens follow now, and the earlier ones. */
export const GET = safe(async (request) => {
  const denied = guard(request);
  if (denied) return denied;
  const [current, history] = await Promise.all([currentConfig(), configHistory()]);
  return json({ current: current?.config ?? null, history });
});

/**
 * POST the signed «metas y turnos» (signed in the browser). Stored only if the app would accept it: trusted key,
 * valid signature, known areas, valid goals and shifts, and a revision newer than the current one.
 */
export const POST = safe(async (request) => {
  const denied = guard(request, true);
  if (denied) return denied;
  const check = verifyConfig(await request.text());
  if (!check.ok) return fail(check.status, check.error, check.details);
  const current = await currentConfig();
  if (current && check.value.revision <= current.config.revision) {
    return fail(409, `Ya hay una revisión igual o más nueva (${current.config.revision}). Recarga y vuelve a publicar.`);
  }
  await saveConfig(check.text, check.value);
  console.log(`Metas y turnos: revisión ${check.value.revision} (${check.value.areas.map((a) => a.code).join(", ") || "ninguna área"})`);
  return json({ ok: true, revision: check.value.revision });
});
