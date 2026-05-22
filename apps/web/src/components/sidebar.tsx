"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import {
  LayoutDashboard,
  UserCircle2,
  Search,
  Wand2,
  FileText,
  Inbox,
  AlertTriangle,
  Send,
  Settings,
  type LucideIcon,
} from "lucide-react";
import { cn } from "@/lib/cn";

interface NavItem {
  href: string;
  label: string;
  Icon: LucideIcon;
  /** Optional pill rendered on the right (e.g. unread count). */
  badge?: string;
}

const nav: readonly NavItem[] = [
  { href: "/dashboard",   label: "Dashboard",          Icon: LayoutDashboard },
  { href: "/profile",     label: "Consultant Profile", Icon: UserCircle2 },
  { href: "/matches",     label: "Job Matches",        Icon: Search, badge: "—" },
  { href: "/tailor",      label: "Tailor & Apply",     Icon: Wand2 },
  { href: "/submissions", label: "Submissions",        Icon: FileText },
  { href: "/inbox",       label: "Recruiter Inbox",    Icon: Inbox },
  { href: "/escalations", label: "Escalations",        Icon: AlertTriangle },
  { href: "/followups",   label: "Follow-ups",         Icon: Send },
];

export function Sidebar() {
  const pathname = usePathname();
  return (
    <aside
      aria-label="Primary navigation"
      className="flex w-60 flex-col border-r border-ink-800 bg-ink-900"
    >
      <Link
        href="/dashboard"
        className="flex items-center gap-2 border-b border-ink-800 px-5 py-5"
        aria-label="Aireq home"
      >
        <div
          aria-hidden="true"
          className="flex h-8 w-8 items-center justify-center rounded-lg bg-gradient-to-br from-brand-500 to-purple-500 font-bold"
        >
          A
        </div>
        <div>
          <div className="text-sm font-semibold">Aireq</div>
          <div className="-mt-0.5 text-[10px] text-slate-400">AI Operations Copilot</div>
        </div>
      </Link>

      <nav className="flex-1 space-y-0.5 px-2 py-3 text-sm">
        <div className="px-3 pt-2 pb-1 text-[10px] uppercase tracking-wide text-slate-500">
          Workspace
        </div>
        {nav.map(({ href, label, Icon, badge }) => {
          const active = pathname === href || pathname.startsWith(href + "/");
          return (
            <Link
              key={href}
              href={href}
              aria-current={active ? "page" : undefined}
              className={cn(
                "flex items-center gap-2 rounded-md px-3 py-2",
                active
                  ? "bg-ink-800 text-white"
                  : "text-slate-300 hover:bg-ink-800/60 hover:text-white",
              )}
            >
              <Icon className="h-4 w-4" aria-hidden="true" />
              <span>{label}</span>
              {badge && (
                <span className="ml-auto rounded bg-brand-500/20 px-1.5 py-0.5 text-[10px] text-brand-200">
                  {badge}
                </span>
              )}
            </Link>
          );
        })}
        <div className="px-3 pt-4 pb-1 text-[10px] uppercase tracking-wide text-slate-500">
          Operate
        </div>
        <Link
          href="/settings"
          className="flex items-center gap-2 rounded-md px-3 py-2 text-slate-300 hover:bg-ink-800/60 hover:text-white"
        >
          <Settings className="h-4 w-4" aria-hidden="true" />
          Settings
        </Link>
      </nav>

      <div className="border-t border-ink-800 p-3 text-xs text-slate-400">
        <div className="flex items-center gap-2">
          <div
            aria-hidden="true"
            className="flex h-7 w-7 items-center justify-center rounded-full bg-slate-700 text-[11px]"
          >
            SM
          </div>
          <div>
            <div className="text-slate-200">shahnawaz</div>
            <div className="text-[10px]">Free plan · 14 days</div>
          </div>
        </div>
      </div>
    </aside>
  );
}
