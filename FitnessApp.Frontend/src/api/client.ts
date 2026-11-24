export const apiBase = (import.meta.env.VITE_API_URL ?? '').replace(/\/$/, '');
export class ApiError extends Error {
  constructor(public status: number, message: string, public errors: Record<string, string[]> = {}) { super(message); }
}
let csrf = '';
let refreshing: Promise<void> | undefined;
let sessionVersion = 0;
export async function renewCsrf(): Promise<void> {
  const response = await fetch(`${apiBase}/api/session/antiforgery`, { credentials: 'include' });
  if (!response.ok) throw new ApiError(response.status, 'Could not initialize the session.');
  const data: { token: string } = await response.json();
  csrf = data.token;
}
async function send(path: string, method = 'GET', body?: unknown, retry = true): Promise<Response> {
  const version = sessionVersion;
  if (method !== 'GET' && !csrf) await renewCsrf();
  const headers: Record<string, string> = {};
  if (method !== 'GET') headers.RequestVerificationToken = csrf;
  if (body !== undefined && !(body instanceof FormData)) headers['Content-Type'] = 'application/json';
  const response = await fetch(`${apiBase}${path}`, { method, credentials: 'include', headers,
    body: body instanceof FormData ? body : body === undefined ? undefined : JSON.stringify(body) });
  if (response.status === 401 && retry && !path.startsWith('/api/session')) {
    if (version === sessionVersion) refreshing ??= (async () => {
      await renewCsrf();
      await request('/api/session/refresh', 'POST', undefined, false);
      await renewCsrf();
      sessionVersion++;
    })().finally(() => { refreshing = undefined; });
    await refreshing;
    return send(path, method, body, false);
  }
  if (!response.ok) {
    const error = await response.json().catch(() => ({})) as { error?: string; title?: string; errors?: Record<string, string[]> };
    throw new ApiError(response.status, error.error ?? error.title ?? 'Request failed.', error.errors);
  }
  const token = response.headers.get('RequestVerificationToken');
  if (token) csrf = token;
  return response;
}

export async function request<T>(path: string, method = 'GET', body?: unknown, retry = true): Promise<T> {
  const response = await send(path, method, body, retry);
  return response.status === 204 ? undefined as T : response.json() as Promise<T>;
}

export async function download(path: string): Promise<Blob> {
  return (await send(path)).blob();
}

