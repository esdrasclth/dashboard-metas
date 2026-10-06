import { isAuthenticated, isSameOrigin } from "./auth.js";

const NO_STORE = { "cache-control": "no-store" };

export function json(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json; charset=utf-8", ...NO_STORE, ...headers },
  });
}

export function fail(status: number, error: string, details?: string[]): Response {
  return json({ error, details }, status);
}

/** 401 unless the session cookie is valid; for writes also 403 unless the request comes from this site. */
export function guard(request: Request, write = false): Response | null {
  if (!isAuthenticated(request)) return fail(401, "La sesión venció: vuelve a entrar.");
  if (write && !isSameOrigin(request)) return fail(403, "Solicitud rechazada.");
  return null;
}

/** Turns unexpected errors into a clean 500 (the detail goes to the function log, not to the browser). */
export function safe(handler: (request: Request) => Promise<Response>): (request: Request) => Promise<Response> {
  return async (request) => {
    try {
      return await handler(request);
    } catch (error) {
      console.error(error);
      return fail(500, "Error del servidor. Intenta de nuevo en un momento.");
    }
  };
}
