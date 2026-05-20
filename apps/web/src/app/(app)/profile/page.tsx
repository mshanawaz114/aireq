"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import {
  api,
  ApiError,
  type Consultant,
  type ResumeResponse,
  type UpsertConsultantBody,
} from "@/lib/api";

// Consultant profile + resume upload. The owner manages the single consultant
// (solo plan) here; agencies will get a list/switcher in a later story.
//
// Refs: AIRMVP1-106
export default function ProfilePage() {
  const [loading, setLoading] = useState(true);
  const [consultant, setConsultant] = useState<Consultant | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const list = await api.consultants.list();
      setConsultant(list[0] ?? null);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Couldn't load your profile.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  if (loading) {
    return <div className="p-8 text-sm text-slate-400">Loading profile…</div>;
  }

  return (
    <div className="p-8">
      <header className="mb-6">
        <p className="text-xs text-slate-500">Workspace</p>
        <h1 className="text-xl font-semibold">Consultant Profile</h1>
      </header>

      {error && (
        <div
          role="alert"
          className="mb-6 rounded-md border border-bad-500/40 bg-bad-500/10 px-3 py-2 text-xs text-bad-500"
        >
          {error}
        </div>
      )}

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        <ConsultantForm consultant={consultant} onSaved={setConsultant} />
        <ResumePanel consultant={consultant} onUploaded={() => void load()} />
      </div>
    </div>
  );
}

function ConsultantForm({
  consultant,
  onSaved,
}: {
  consultant: Consultant | null;
  onSaved: (c: Consultant) => void;
}) {
  const [fullName, setFullName] = useState(consultant?.fullName ?? "");
  const [headline, setHeadline] = useState(consultant?.headline ?? "");
  const [location, setLocation] = useState(consultant?.location ?? "");
  const [workAuth, setWorkAuth] = useState(consultant?.workAuth ?? "");
  const [rate, setRate] = useState(
    consultant?.rateTargetUsdHourly != null ? String(consultant.rateTargetUsdHourly) : "",
  );
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [savedAt, setSavedAt] = useState<number | null>(null);

  useEffect(() => {
    setFullName(consultant?.fullName ?? "");
    setHeadline(consultant?.headline ?? "");
    setLocation(consultant?.location ?? "");
    setWorkAuth(consultant?.workAuth ?? "");
    setRate(consultant?.rateTargetUsdHourly != null ? String(consultant.rateTargetUsdHourly) : "");
  }, [consultant]);

  async function onSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setSaving(true);
    setError(null);
    const body: UpsertConsultantBody = {
      fullName,
      headline: headline || null,
      location: location || null,
      workAuth: workAuth || null,
      rateTargetUsdHourly: rate.trim() === "" ? null : Number(rate),
    };
    try {
      const saved = consultant
        ? await api.consultants.update(consultant.id, body)
        : await api.consultants.create(body);
      onSaved(saved);
      setSavedAt(Date.now());
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Couldn't save. Please try again.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <section
      aria-labelledby="profile-form-heading"
      className="rounded-xl border border-ink-700 bg-ink-900 p-6"
    >
      <h2 id="profile-form-heading" className="text-lg font-medium">
        {consultant ? "Edit profile" : "Create your consultant profile"}
      </h2>
      <p className="mt-1 text-sm text-slate-400">
        This is the person being marketed. On a solo plan, that&rsquo;s you.
      </p>

      <form onSubmit={onSubmit} className="mt-5 space-y-4" noValidate>
        <Field id="fullName" label="Full name" required value={fullName} onChange={setFullName} />
        <Field
          id="headline"
          label="Headline"
          hint="Short pitch line, e.g. “Sr. Salesforce Architect · 20 yrs”."
          value={headline}
          onChange={setHeadline}
        />
        <Field id="location" label="Location" value={location} onChange={setLocation} />
        <Field
          id="workAuth"
          label="Work authorization"
          hint="e.g. US Citizen, H1B, EAD, OPT, GC"
          value={workAuth}
          onChange={setWorkAuth}
        />
        <Field
          id="rate"
          label="Target rate (USD / hour)"
          type="number"
          value={rate}
          onChange={setRate}
        />

        {error && (
          <div
            role="alert"
            className="rounded-md border border-bad-500/40 bg-bad-500/10 px-3 py-2 text-xs text-bad-500"
          >
            {error}
          </div>
        )}

        <div className="flex items-center gap-3">
          <button
            type="submit"
            disabled={saving}
            className="rounded-md bg-brand-500 px-4 py-2.5 text-sm font-medium text-white hover:bg-brand-600 disabled:opacity-60"
          >
            {saving ? "Saving…" : consultant ? "Save changes" : "Create profile"}
          </button>
          {savedAt && (
            <span aria-live="polite" className="text-xs text-good-500">
              Saved.
            </span>
          )}
        </div>
      </form>
    </section>
  );
}

function ResumePanel({
  consultant,
  onUploaded,
}: {
  consultant: Consultant | null;
  onUploaded: (r: ResumeResponse) => void;
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [lastUploaded, setLastUploaded] = useState<ResumeResponse | null>(null);

  async function onPick(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file || !consultant) return;
    setUploading(true);
    setError(null);
    try {
      const res = await api.resumes.upload(consultant.id, file);
      setLastUploaded(res);
      onUploaded(res);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Upload failed. Please try again.");
    } finally {
      setUploading(false);
      if (inputRef.current) inputRef.current.value = "";
    }
  }

  return (
    <section
      aria-labelledby="resume-heading"
      className="rounded-xl border border-ink-700 bg-ink-900 p-6"
    >
      <h2 id="resume-heading" className="text-lg font-medium">
        Resume
      </h2>

      {!consultant ? (
        <p className="mt-2 text-sm text-slate-400">
          Create your profile first, then you can upload a resume here.
        </p>
      ) : (
        <>
          <p className="mt-1 text-sm text-slate-400">
            Upload a PDF, DOC, DOCX, or TXT (max 10&nbsp;MB). We parse it automatically to build
            your skill profile.
          </p>

          <div className="mt-4">
            <label
              htmlFor="resume-file"
              className="inline-flex cursor-pointer items-center rounded-md border border-ink-600 bg-ink-800 px-4 py-2 text-sm text-slate-200 hover:bg-ink-700"
            >
              {uploading ? "Uploading…" : "Choose a file"}
            </label>
            <input
              ref={inputRef}
              id="resume-file"
              type="file"
              accept=".pdf,.doc,.docx,.txt,application/pdf,application/msword,application/vnd.openxmlformats-officedocument.wordprocessingml.document,text/plain"
              disabled={uploading}
              onChange={onPick}
              className="sr-only"
            />
            <span className="ml-3 text-xs text-slate-500">
              {consultant.resumeCount > 0
                ? `${consultant.resumeCount} version${consultant.resumeCount > 1 ? "s" : ""} uploaded`
                : "No resume yet"}
            </span>
          </div>

          {error && (
            <div
              role="alert"
              className="mt-3 rounded-md border border-bad-500/40 bg-bad-500/10 px-3 py-2 text-xs text-bad-500"
            >
              {error}
            </div>
          )}

          {lastUploaded && (
            <div
              aria-live="polite"
              className="mt-3 rounded-md border border-good-500/40 bg-good-500/10 px-3 py-2 text-xs text-good-500"
            >
              Uploaded {lastUploaded.originalFilename ?? "resume"} (v{lastUploaded.version}). Parsing
              runs in the background — refresh in a moment to see extracted skills.
            </div>
          )}
        </>
      )}
    </section>
  );
}

function Field(props: {
  id: string;
  label: string;
  type?: string;
  required?: boolean;
  hint?: string;
  value: string;
  onChange: (v: string) => void;
}) {
  return (
    <div>
      <label htmlFor={props.id} className="block text-xs font-medium text-slate-300">
        {props.label}
        {props.required && <span className="ml-0.5 text-bad-500">*</span>}
      </label>
      <input
        id={props.id}
        name={props.id}
        type={props.type ?? "text"}
        required={props.required}
        value={props.value}
        onChange={(e) => props.onChange(e.target.value)}
        aria-describedby={props.hint ? `${props.id}-hint` : undefined}
        className="mt-1.5 w-full rounded-md border border-ink-700 bg-ink-900 px-3 py-2 text-sm text-slate-100 placeholder:text-slate-500 focus:border-brand-500"
      />
      {props.hint && (
        <p id={`${props.id}-hint`} className="mt-1 text-[11px] text-slate-500">
          {props.hint}
        </p>
      )}
    </div>
  );
}
