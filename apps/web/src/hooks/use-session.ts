"use client";

import { useEffect, useState } from "react";
import { readSession, clearSession, type Session } from "@/lib/auth";

/**
 * Reactive accessor for the locally-stored session. Re-renders on cross-tab
 * storage events so logging out in one tab logs out all of them.
 */
export function useSession(): {
  session: Session | null;
  isLoading: boolean;
  signOut: () => void;
} {
  const [session, setSession] = useState<Session | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    setSession(readSession());
    setIsLoading(false);

    const onStorage = (e: StorageEvent) => {
      if (e.key === null || e.key === "aireq.session.v1") {
        setSession(readSession());
      }
    };
    window.addEventListener("storage", onStorage);
    return () => window.removeEventListener("storage", onStorage);
  }, []);

  return {
    session,
    isLoading,
    signOut: () => {
      clearSession();
      setSession(null);
    },
  };
}
