// Tiny API client. Centralizes base URL, timeouts, and error shape so every
// component reads the same surface. Replaced by a typed client (generated
// from OpenAPI) in AIRMVP1-102.

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

async function request<T>(path: string, init?: RequestInit, timeoutMs = 5_000): Promise<T> {
  const controller = new AbortController();
  const t = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const res = await fetch(`${API_URL}${path}`, {
      ...init,
      signal: controller.signal,
      headers: { "Content-Type": "application/json", ...(init?.headers ?? {}) },
    });
    if (!res.ok) {
      throw new ApiError(res.status, `${res.status} ${res.statusText}`);
    }
    return (await res.json()) as T;
  } finally {
    clearTimeout(t);
  }
}

export const api = {
  health: () => request<HealthResponse>("/health/ready"),
  dbStatus: () => request<DbStatusResponse>("/api/db/status", undefined, 10_000),
};
