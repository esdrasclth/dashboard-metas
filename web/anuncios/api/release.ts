import { fail, guard, json, safe } from "../lib/http.js";
import { currentRelease, packageMatches, packages, releaseHistory, saveRelease, verifyRelease } from "../lib/releases.js";
import { isConfigured, listScreens } from "../lib/screens.js";

/** GET → the version the screens follow, earlier publications, uploaded packages and each screen's version. */
export const GET = safe(async (request) => {
  const denied = guard(request);
  if (denied) return denied;
  const [current, history, files, screens] = await Promise.all([
    currentRelease(),
    releaseHistory(),
    packages(),
    isConfigured() ? listScreens(0) : Promise.resolve([]),
  ]);
  return json({
    current: current?.manifest ?? null,
    history,
    packages: files,
    screens: screens.map((s) => ({
      id: s.id, label: s.label, machine: s.report.machine, branch: s.report.branch, lastSeen: s.lastSeen,
      appVersion: s.report.appVersion, mode: s.report.mode, update: s.report.update ?? null,
    })),
  });
});

/**
 * POST a manifest signed in the browser (pause, targets, schedule, go back) or by scripts/publish-version.mjs.
 * Stored only if the app would accept it: trusted key, valid manifest, package present with that size, and
 * a publication number newer than the current one.
 */
export const POST = safe(async (request) => {
  const denied = guard(request, true);
  if (denied) return denied;
  const text = await request.text();
  const check = verifyRelease(text);
  if (!check.ok) return fail(check.status, check.error, check.details);
  const current = await currentRelease();
  if (current && check.manifest.release <= current.manifest.release) {
    return fail(409, "Ya hay una publicación igual o más nueva. Recarga y vuelve a intentar.");
  }
  if (!(await packageMatches(check.manifest.file.pathname, check.manifest.file.size))) {
    return fail(422, "El paquete no existe en el almacenamiento o su tamaño no coincide.");
  }
  await saveRelease(text, check.manifest);
  console.log(`Versión ${check.manifest.version} publicada (publicación ${check.manifest.release}, ${check.manifest.paused ? "en pausa" : check.manifest.install})`);
  return json({ ok: true, release: check.manifest.release });
});
