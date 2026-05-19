// Tiny API client. Centralizes base URL, timeouts, error shape, and auth
// header injection so every component reads the same surface. Replaced by
// a typed client (generated from OpenAPI) in a later story.

import { readSession, clearSession } from "@/lib/auth";

const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5180";

export interface HealthResponse {
  status: "ok" | "degraded" | "down";
  service: string;
  version: string;
  dependenciesHealthy: Record<string, boolean> | null;
  timestamp: string;
}

export interface DbStatusResponse {
  appliedMigrations: string[];
  pendingMigrations: string[];
  rowCounts: Record<string, number>;
  timestamp: string;
}

export class ApiError extends Error {
  constructor(public readonly status: number, message: string) {
    super(message);
    this.name = "ApiError";
  }
}

interface RequestOptions extends RequestInit {
  /** Set to `false` to skip Authorization header injection (e.g. login/signup). */
  withAuth?: boolean;
}

async function request<T>(path: string, init?: RequestOptions, timeoutMs = 5_000): Promise<T> {
  const controller = new AbortController();
  const t = setTimeout(() => controller.abort(), timeoutMs);

  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    ...(init?.headers as Record<string, string> | undefined),
  };

  // Auth header injection (default: on, opt-out for login/signup).
  if (init?.withAuth !== false) {
    const session = readSession();
    if (session?.accessToken) {
      headers["Authorization"] = `Bearer ${session.accessToken}`;
    }
  }

  try {
    const res = await fetch(`${API_URL}${path}`, {
      ...init,
      signal: controller.signal,
      headers,
    });

    // Auto-logout on 401: clear stale session so the auth gate can redirect.
    if (res.status === 401 && init?.withAuth !== false) {
      clearSession();
    }

    if (!res.ok) {
      let bodyMessage: string | undefined;
      try {
        const body = await res.clone().json();
        bodyMessage = (body as { error?: string })?.error;
      } catch {
        /* body was not JSON */
      }
      throw new ApiError(res.status, bodyMessage ?? `${res.status} ${res.statusText}`);
    }
    return (await res.json()) as T;
  } finally {
    clearTimeout(t);
  }
}

export interface SignupBody {
  tenantName: string;
  email: string;
  password: string;
  displayName?: string | null;
}

export interface LoginBody {
  email: string;
  password: string;
}

export interface AuthResponse {
  accessToken: string;
  expiresAt: string;
  user: {
    id: string;
    tenantId: string;
    email: string;
    displayName: string | null;
    role: string;
    tenantName: string;
    tenantPlan: string;
  };
}

export const api = {
  health: () => request<HealthResponse>("/health/ready", { withAuth: false }),
  dbStatus: () => request<DbStatusResponse>("/api/db/status", undefined, 10_000),

  auth: {
    signup: (body: SignupBody) =>
      request<AuthResponse>("/api/auth/signup", {
        method: "POST",
        body: JSON.stringify(body),
        withAuth: false,
      }),
    login: (body: LoginBody) =>
      request<AuthResponse>("/api/auth/login", {
        method: "POST",
        body: JSON.stringify(body),
        withAuth: false,
      }),
    me: () => request<AuthResponse["user"]>("/api/auth/me"),
  },
};
