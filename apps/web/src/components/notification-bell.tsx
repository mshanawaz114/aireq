"use client";

// NotificationBell — header bell with an unread count + dropdown feed.
//
// Polls /api/notifications every 30s. We poll rather than hold a SignalR socket
// because worker-raised notifications (replies, escalations, follow-ups) can't be
// pushed live without a backplane (see docs/BACKLOG.md) — polling catches them
// all uniformly. The SignalR hub stays available for API-process events later.
//
// Refs: AIRMVP1-403 (client)

import { useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { Bell } from "lucide-react";
import { api, type NotificationItem } from "@/lib/api";

const POLL_MS = 30_000;

// Route to the section that owns this notification type. (The server stores a
// /matches/{id} deep-link for a future match-detail page; until that exists we
// send the user to the relevant queue.)
function hrefFor(type: string): string | null {
  switch (type) {
    case "escalation":
      return "/escalations";
    case "reply":
      return "/inbox";
    case "followup":
      return "/followups";
    default:
      return null;
  }
}

export function NotificationBell() {
  const [items, setItems] = useState<NotificationItem[]>([]);
  const [unread, setUnread] = useState(0);
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  const load = useCallback(async () => {
    try {
      const feed = await api.notifications.feed();
      setItems(feed.items);
      setUnread(feed.unreadCount);
    } catch {
      /* transient — keep last good state */
    }
  }, []);

  useEffect(() => {
    load();
    const id = setInterval(load, POLL_MS);
    return () => clearInterval(id);
  }, [load]);

  // Close on outside click.
  useEffect(() => {
    function onClick(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    }
    document.addEventListener("mousedown", onClick);
    return () => document.removeEventListener("mousedown", onClick);
  }, []);

  async function markAll() {
    setItems((cur) => cur.map((n) => ({ ...n, read: true })));
    setUnread(0);
    try {
      await api.notifications.readAll();
    } catch {
      load();
    }
  }

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-label={`Notifications${unread ? ` (${unread} unread)` : ""}`}
        className="relative rounded-md p-1.5 text-slate-400 hover:bg-ink-800 hover:text-slate-200"
      >
        <Bell className="h-4 w-4" aria-hidden />
        {unread > 0 && (
          <span className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-brand-500 px-1 text-[10px] font-semibold text-white">
            {unread > 9 ? "9+" : unread}
          </span>
        )}
      </button>

      {open && (
        <div className="absolute right-0 z-20 mt-2 w-80 overflow-hidden rounded-xl border border-ink-700 bg-ink-900 shadow-xl">
          <div className="flex items-center justify-between border-b border-ink-800 px-4 py-2.5">
            <span className="text-sm font-medium">Notifications</span>
            {unread > 0 && (
              <button onClick={markAll} className="text-xs text-brand-400 hover:underline">
                Mark all read
              </button>
            )}
          </div>
          <div className="max-h-96 overflow-y-auto">
            {items.length === 0 ? (
              <p className="px-4 py-6 text-center text-sm text-slate-500">You&rsquo;re all caught up.</p>
            ) : (
              <ul>
                {items.map((n) => {
                  const inner = (
                    <div
                      className={`border-b border-ink-800 px-4 py-3 ${n.read ? "" : "bg-ink-800/40"}`}
                    >
                      <div className="flex items-start gap-2">
                        {!n.read && <span className="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-brand-500" aria-hidden />}
                        <div className="min-w-0">
                          <div className="text-sm text-slate-100">{n.title}</div>
                          {n.body && <div className="mt-0.5 truncate text-xs text-slate-400">{n.body}</div>}
                          <div className="mt-1 text-[10px] text-slate-500">
                            {new Date(n.createdAt).toLocaleString()}
                          </div>
                        </div>
                      </div>
                    </div>
                  );
                  const href = hrefFor(n.type);
                  return (
                    <li key={n.id}>
                      {href ? (
                        <Link href={href} onClick={() => setOpen(false)}>
                          {inner}
                        </Link>
                      ) : (
                        inner
                      )}
                    </li>
                  );
                })}
              </ul>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
