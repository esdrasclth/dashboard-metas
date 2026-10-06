import { MAX_REPORT_BYTES, verifyHeartbeat } from "../lib/devices.js";
import { offerFor } from "../lib/releases.js";
import { saveReport } from "../lib/screens.js";

/**
 * POST from each Dashboard Metas screen every few minutes: its status, signed with its own device key
 * (header x-dm-signature). Public on purpose (the screens have no session); the signature ties every report to
 * one screen, and the store rate-limits and caps them. The reply carries the server time and, when there is a
 * version for this screen, the signed manifest with a temporary download link.
 */
export async function POST(request: Request): Promise<Response> {
  const headers = { "content-type": "application/json; charset=utf-8", "cache-control": "no-store" };
  const reply = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers });
  try {
    const length = Number(request.headers.get("content-length") ?? 0);
    if (length > MAX_REPORT_BYTES) return reply(413, { error: "Reporte demasiado grande." });
    const body = Buffer.from(await request.arrayBuffer());
    const check = verifyHeartbeat(body, request.headers.get("x-dm-signature"));
    if (!check.ok) return reply(check.status, { error: check.error });
    const at = new Date().toISOString();
    const saved = await saveReport(check.report, at);
    if (!saved.ok) return reply(saved.status, { error: saved.error });
    // A new version for this screen, if there is one (a failure here never fails the report)
    let update = null;
    if (check.report.update.enabled) {
      try {
        update = await offerFor(check.report.machine, check.report.branch, check.report.appVersion);
      } catch (error) {
        console.error("No se pudo preparar la actualización", error);
      }
    }
    return reply(200, { ok: true, serverTime: at, update });
  } catch (error) {
    console.error(error);
    return reply(503, { error: "Servicio no disponible." });
  }
}
