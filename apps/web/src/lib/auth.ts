// Session storage + auth helpers for the web side.
//
// MVP strategy: access token in localStorage. Vulnerable to XSS in theory,
// but we ship with strict CSP + no user-generated HTML on the dashboard.
// Move to an httpOnly cookie when we open the marketing site in AIRMVP1-405.
//
// Refs: AIRMVP1-103

const STORAGE_KEY = "aireq.session.v1";

export interface SessionUser {
  id: string;
  tenantId: string;
  email: string;
  displayName: string | null;
  role: string;
  tenantName: string;
  tenantPlan: string;
}

export interface Session {
  accessToken: string;
  expiresAt: string;
  user: SessionUser;
}

export function readSession(): Session | null {
  if (typeof window === "undefined") return null;
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as Session;
    if (!parsed?.accessToken || !parsed?.expiresAt) return null;
    if (new Date(parsed.expiresAt).getTime() < Date.now()) {
      // expired — clear and return null so caller redirects to login
      window.localStorage.removeItem(STORAGE_KEY);
      return null;
    }
    return parsed;
  } catch {
    return null;
  }
}

export function writeSession(s: Session): void {
  if (typeof window === "undefined") return;
  window.localStorage.setItem(STORAGE_KEY, JSON.stringify(s));
}

export function clearSession(): void {
  if (typeof window === "undefined") return;
  window.localStorage.removeItem(STORAGE_KEY);
}
