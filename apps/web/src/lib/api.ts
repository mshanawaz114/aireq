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

export interface Consultant {
  id: string;
  fullName: string;
  headline: string | null;
  location: string | null;
  workAuth: string | null;
  rateTargetUsdHourly: number | null;
  resumeCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface UpsertConsultantBody {
  fullName: string;
  headline?: string | null;
  location?: string | null;
  workAuth?: string | null;
  rateTargetUsdHourly?: number | null;
}

export interface ResumeResponse {
  id: string;
  consultantId: string;
  version: number;
  sourceBlobUrl: string;
  originalFilename: string | null;
  parsedJson: string | null;
  createdAt: string;
}

export interface Match {
  id: string;
  jobId: string;
  jobTitle: string;
  company: string;
  location: string | null;
  postedAt: string;
  score: number;
  status: string;
  reasoned: boolean;
  summary: string | null;
  rationale: string[];
  missingKeywords: string[];
}

export interface Submission {
  id: string;
  matchId: string;
  jobTitle: string;
  company: string;
  channel: string;
  responseStatus: string | null;
  submittedAt: string;
  responsePayloadJson: string | null;
}

export interface AtsAnalysis {
  matchId: string;
  coveragePercent: number;
  jobKeywordCount: number;
  presentKeywords: string[];
  missingKeywords: string[];
}

export interface Metrics {
  jobs: { total: number; active: number; embedded: number; bySource: Record<string, number> };
  matches: { total: number; new: number; reasoned: number; avgScore: number };
  resumes: { total: number; parsed: number; embedded: number };
  llm: { calls: number; costUsd: number; byPurpose: Record<string, number> };
  generatedAt: string;
}

/**
 * Multipart upload — separate from `request()` because we must NOT set a JSON
 * Content-Type; the browser sets multipart/form-data with the right boundary
 * when the body is a FormData. Auth header still injected by hand.
 */
async function uploadFile<T>(path: string, file: File, timeoutMs = 30_000): Promise<T> {
  const controller = new AbortController();
  const t = setTimeout(() => controller.abort(), timeoutMs);
  const form = new FormData();
  form.append("file", file);

  const headers: Record<string, string> = {};
  const session = readSession();
  if (session?.accessToken) headers["Authorization"] = `Bearer ${session.accessToken}`;

  try {
    const res = await fetch(`${API_URL}${path}`, {
      method: "POST",
      body: form,
      signal: controller.signal,
      headers,
    });
    if (res.status === 401) clearSession();
    if (!res.ok) {
      let msg: string | undefined;
      try {
        msg = ((await res.clone().json()) as { error?: string })?.error;
      } catch {
        /* not json */
      }
      throw new ApiError(res.status, msg ?? `${res.status} ${res.statusText}`);
    }
    return (await res.json()) as T;
  } finally {
    clearTimeout(t);
  }
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

  consultants: {
    list: () => request<Consultant[]>("/api/consultants"),
    get: (id: string) => request<Consultant>(`/api/consultants/${id}`),
    create: (body: UpsertConsultantBody) =>
      request<Consultant>("/api/consultants", {
        method: "POST",
        body: JSON.stringify(body),
      }),
    update: (id: string, body: UpsertConsultantBody) =>
      request<Consultant>(`/api/consultants/${id}`, {
        method: "PUT",
        body: JSON.stringify(body),
      }),
  },

  resumes: {
    upload: (consultantId: string, file: File) =>
      uploadFile<ResumeResponse>(`/api/consultants/${consultantId}/resumes`, file),
  },

  matches: {
    list: (params?: { minScore?: number; status?: string }) => {
      const qs = new URLSearchParams();
      if (params?.minScore != null) qs.set("minScore", String(params.minScore));
      if (params?.status) qs.set("status", params.status);
      const suffix = qs.toString() ? `?${qs}` : "";
      return request<Match[]>(`/api/matches${suffix}`);
    },
  },

  ats: (matchId: string) => request<AtsAnalysis>(`/api/matches/${matchId}/ats`),

  tailor: (matchId: string) =>
    request<{ enqueued: string }>(`/api/matches/${matchId}/tailor`, { method: "POST" }),

  submit: (matchId: string) =>
    request<{ enqueued: string }>(`/api/matches/${matchId}/submit`, { method: "POST" }),

  submissions: {
    list: () => request<Submission[]>("/api/submissions"),
  },

  waitlist: {
    join: (body: { email: string; persona?: string | null; source?: string | null }) =>
      request<{ joined: boolean; alreadyJoined: boolean }>("/api/waitlist", {
        method: "POST",
        body: JSON.stringify(body),
        withAuth: false,
      }),
  },

  adminMetrics: () => request<Metrics>("/api/admin/metrics", undefined, 10_000),
};
