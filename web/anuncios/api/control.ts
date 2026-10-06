import { readCurrent } from "../lib/store.js";

/**
 * GET /control.json (rewrite): the signed file the screens download every minute. Public on purpose: it only
 * carries signed announcements the screens will show anyway. ETag + no-cache, so an unchanged file costs a 304
 * and a new publication reaches every PC on its next check.
 */
export async function GET(request: Request): Promise<Response> {
  const headers = { "cache-control": "no-cache", "x-content-type-options": "nosniff" };
  try {
    const current = await readCurrent(request.headers.get("if-none-match") ?? undefined);
    if (current === "not-modified") return new Response(null, { status: 304, headers });
    if (!current) return new Response("Todavía no hay anuncios publicados.", { status: 404, headers });
    return new Response(current.text, {
      status: 200,
      headers: { ...headers, "content-type": "application/json; charset=utf-8", etag: current.etag },
    });
  } catch (error) {
    console.error(error);
    return new Response("Error del servidor.", { status: 503, headers: { ...headers, "retry-after": "60" } });
  }
}
