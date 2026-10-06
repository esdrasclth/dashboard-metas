import { ONLINE_SECONDS } from "../lib/devices.js";
import { fail, guard, json, safe } from "../lib/http.js";
import { isConfigured, listScreens, removeScreen, renameScreen, trustScreen, views } from "../lib/screens.js";
import { currentAnnouncements } from "../lib/store.js";

/** GET → every screen with its latest report and history, plus who saw / dismissed each published announcement. */
export const GET = safe(async (request) => {
  const denied = guard(request);
  if (denied) return denied;
  if (!isConfigured()) return json({ configured: false, screens: [], views: {}, onlineSeconds: ONLINE_SECONDS });
  const current = await currentAnnouncements();
  const announcementIds = current ? current.feed.announcements.map((a) => a.id) : [];
  const [screens, seen] = await Promise.all([listScreens(), views(announcementIds)]);
  return json({ configured: true, screens, views: seen, onlineSeconds: ONLINE_SECONDS, serverTime: new Date().toISOString() });
});

/** POST {action: "rename", id, label} | {action: "remove", id} | {action: "trust", id, trusted}. */
export const POST = safe(async (request) => {
  const denied = guard(request, true);
  if (denied) return denied;
  const body = await request.json().catch(() => null);
  const id = typeof body?.id === "string" ? body.id : "";
  if (!/^[0-9a-f]{16}$/.test(id)) return fail(400, "Pantalla no válida.");
  if (body.action === "rename") {
    const label = typeof body.label === "string" ? body.label : "";
    return (await renameScreen(id, label)) ? json({ ok: true }) : fail(404, "Esa pantalla no existe.");
  }
  if (body.action === "trust") {
    return (await trustScreen(id, body.trusted === true)) ? json({ ok: true }) : fail(404, "Esa pantalla no existe.");
  }
  if (body.action === "remove") {
    await removeScreen(id);
    return json({ ok: true });
  }
  return fail(400, "Acción no válida.");
});
