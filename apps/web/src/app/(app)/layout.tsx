"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { Sidebar } from "@/components/sidebar";
import { HealthBadge } from "@/components/health-badge";
import { useSession } from "@/hooks/use-session";

export default function AppLayout({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const { session, isLoading, signOut } = useSession();

  // Auth gate: any /(app)/* route requires a session. We redirect from the
  // client (no SSR rendering before the check) to keep the UX simple.
  useEffect(() => {
    if (!isLoading && !session) router.replace("/login");
  }, [isLoading, session, router]);

  if (isLoading) {
    return <div className="grid min-h-screen place-items-center text-slate-400">Loading…</div>;
  }

  if (!session) return null; // redirect in flight

  return (
    <div className="flex min-h-screen">
      <Sidebar />
      <div className="flex flex-1 flex-col">
        <header
          role="banner"
          className="flex items-center justify-between border-b border-ink-800 bg-ink-900/40 px-8 py-3"
        >
          <div className="text-xs text-slate-500">
            Aireq · pre-alpha — workspace{" "}
            <span className="text-slate-300">{session.user.tenantName}</span>
          </div>
          <div className="flex items-center gap-4">
            <HealthBadge />
            <button
              type="button"
              onClick={() => {
                signOut();
                router.replace("/login");
              }}
              className="text-xs text-slate-400 hover:text-slate-200"
            >
              Sign out
            </button>
          </div>
        </header>
        <main id="main" tabIndex={-1} className="flex-1 outline-none">
          {children}
        </main>
      </div>
    </div>
  );
}
