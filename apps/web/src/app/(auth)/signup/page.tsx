"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { api, ApiError } from "@/lib/api";
import { writeSession } from "@/lib/auth";

export default function SignupPage() {
  const router = useRouter();
  const [tenantName, setTenantName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function onSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      const res = await api.auth.signup({
        tenantName,
        email,
        password,
        displayName: displayName || null,
      });
      writeSession({ accessToken: res.accessToken, expiresAt: res.expiresAt, user: res.user });
      router.push("/dashboard");
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Couldn't create your account. Please try again.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main aria-labelledby="signup-heading">
      <div className="mb-6 flex items-center gap-2">
        <div
          aria-hidden="true"
          className="flex h-9 w-9 items-center justify-center rounded-lg bg-gradient-to-br from-brand-500 to-purple-500 text-base font-bold"
        >
          A
        </div>
        <div>
          <div className="font-semibold">Aireq</div>
          <div className="-mt-0.5 text-[10px] text-slate-400">AI Operations Copilot</div>
        </div>
      </div>

      <h1 id="signup-heading" className="text-xl font-semibold">
        Create your workspace
      </h1>
      <p className="mt-1 text-sm text-slate-400">
        Already have an account?{" "}
        <Link href="/login" className="text-brand-400 hover:underline">
          Sign in
        </Link>
        .
      </p>

      <form onSubmit={onSubmit} className="mt-6 space-y-4" noValidate>
        <div>
          <label htmlFor="tenantName" className="block text-xs font-medium text-slate-300">
            Workspace name
          </label>
          <input
            id="tenantName"
            name="tenantName"
            type="text"
            required
            autoComplete="organization"
            value={tenantName}
            onChange={(e) => setTenantName(e.target.value)}
            className="mt-1.5 w-full rounded-md border border-ink-700 bg-ink-900 px-3 py-2 text-sm text-slate-100 placeholder:text-slate-500 focus:border-brand-500"
            placeholder="e.g. Acme Staffing"
          />
        </div>

        <div>
          <label htmlFor="displayName" className="block text-xs font-medium text-slate-300">
            Your name <span className="text-slate-500">(optional)</span>
          </label>
          <input
            id="displayName"
            name="displayName"
            type="text"
            autoComplete="name"
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            className="mt-1.5 w-full rounded-md border border-ink-700 bg-ink-900 px-3 py-2 text-sm text-slate-100 placeholder:text-slate-500 focus:border-brand-500"
          />
        </div>

        <div>
          <label htmlFor="email" className="block text-xs font-medium text-slate-300">
            Email
          </label>
          <input
            id="email"
            name="email"
            type="email"
            autoComplete="email"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            className="mt-1.5 w-full rounded-md border border-ink-700 bg-ink-900 px-3 py-2 text-sm text-slate-100 placeholder:text-slate-500 focus:border-brand-500"
          />
        </div>

        <div>
          <label htmlFor="password" className="block text-xs font-medium text-slate-300">
            Password
          </label>
          <input
            id="password"
            name="password"
            type="password"
            autoComplete="new-password"
            required
            minLength={12}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            className="mt-1.5 w-full rounded-md border border-ink-700 bg-ink-900 px-3 py-2 text-sm text-slate-100 placeholder:text-slate-500 focus:border-brand-500"
            aria-describedby="password-hint"
          />
          <p id="password-hint" className="mt-1 text-[11px] text-slate-500">
            12+ characters.
          </p>
        </div>

        {error && (
          <div
            role="alert"
            aria-live="polite"
            className="rounded-md border border-bad-500/40 bg-bad-500/10 px-3 py-2 text-xs text-bad-500"
          >
            {error}
          </div>
        )}

        <button
          type="submit"
          disabled={submitting}
          className="w-full rounded-md bg-brand-500 px-4 py-2.5 text-sm font-medium text-white hover:bg-brand-600 disabled:opacity-60"
        >
          {submitting ? "Creating workspace…" : "Create workspace"}
        </button>
      </form>
    </main>
  );
}
