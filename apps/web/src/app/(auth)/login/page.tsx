"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { api, ApiError } from "@/lib/api";
import { writeSession } from "@/lib/auth";

export default function LoginPage() {
  const router = useRouter();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function onSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      const res = await api.auth.login({ email, password });
      writeSession({ accessToken: res.accessToken, expiresAt: res.expiresAt, user: res.user });
      router.push("/dashboard");
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Couldn't sign in. Please try again.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main aria-labelledby="login-heading">
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

      <h1 id="login-heading" className="text-xl font-semibold">
        Sign in
      </h1>
      <p className="mt-1 text-sm text-slate-400">
        Welcome back. Don&rsquo;t have an account?{" "}
        <Link href="/signup" className="text-brand-400 hover:underline">
          Sign up
        </Link>
        .
      </p>

      <form onSubmit={onSubmit} className="mt-6 space-y-4" noValidate>
        <Field
          id="email"
          label="Email"
          type="email"
          autoComplete="email"
          required
          value={email}
          onChange={setEmail}
        />
        <Field
          id="password"
          label="Password"
          type="password"
          autoComplete="current-password"
          required
          value={password}
          onChange={setPassword}
        />

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
          {submitting ? "Signing in…" : "Sign in"}
        </button>
      </form>
    </main>
  );
}

function Field(props: {
  id: string;
  label: string;
  type: string;
  autoComplete?: string;
  required?: boolean;
  minLength?: number;
  value: string;
  onChange: (v: string) => void;
  hint?: string;
}) {
  return (
    <div>
      <label htmlFor={props.id} className="block text-xs font-medium text-slate-300">
        {props.label}
      </label>
      <input
        id={props.id}
        name={props.id}
        type={props.type}
        autoComplete={props.autoComplete}
        required={props.required}
        minLength={props.minLength}
        value={props.value}
        onChange={(e) => props.onChange(e.target.value)}
        className="mt-1.5 w-full rounded-md border border-ink-700 bg-ink-900 px-3 py-2 text-sm text-slate-100 placeholder:text-slate-500 focus:border-brand-500"
      />
      {props.hint && <p className="mt-1 text-[11px] text-slate-500">{props.hint}</p>}
    </div>
  );
}
