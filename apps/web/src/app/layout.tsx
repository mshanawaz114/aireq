import type { Metadata, Viewport } from "next";
import Script from "next/script";
import "./globals.css";

export const metadata: Metadata = {
  title: {
    default: "Aireq — AI Operations Copilot",
    template: "%s · Aireq",
  },
  description:
    "AI Operations Copilot for staffing agencies and consultants. Discover real openings, tailor resumes per role to beat ATS, auto-apply, follow up — and only ping you when a human is needed.",
  metadataBase: new URL(process.env.NEXT_PUBLIC_SITE_URL ?? "http://localhost:3000"),
  openGraph: {
    title: "Aireq",
    description: "AI Operations Copilot for staffing agencies and consultants.",
    type: "website",
  },
  robots: { index: false, follow: false }, // not indexed until we ship
};

export const viewport: Viewport = {
  themeColor: "#0a0a0f",
  width: "device-width",
  initialScale: 1,
};

// Plausible analytics — privacy-friendly, cookieless. Only loaded when a domain
// is configured (so dev/CI stay analytics-free). Self-hosted instances can point
// the script host at their own URL. (AIRMVP1-405)
const plausibleDomain = process.env.NEXT_PUBLIC_PLAUSIBLE_DOMAIN;
const plausibleSrc =
  process.env.NEXT_PUBLIC_PLAUSIBLE_SRC ?? "https://plausible.io/js/script.js";

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" dir="ltr" suppressHydrationWarning>
      <body className="min-h-screen bg-ink-950 text-slate-100 antialiased">
        {/* Skip link — first focusable element, jumps over the sidebar. */}
        <a href="#main" className="skip-link">
          Skip to main content
        </a>
        {children}
        {plausibleDomain && (
          <Script
            defer
            data-domain={plausibleDomain}
            src={plausibleSrc}
            strategy="afterInteractive"
          />
        )}
      </body>
    </html>
  );
}
