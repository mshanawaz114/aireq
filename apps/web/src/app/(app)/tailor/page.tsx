import type { Metadata } from "next";
import { ComingSoon } from "@/components/coming-soon";

export const metadata: Metadata = { title: "Tailor & Apply" };

export default function TailorPage() {
  return (
    <ComingSoon
      title="Tailor & Apply"
      story="AIRMVP1-302"
      blurb="Per-job resume rewriting to pass ATS keyword filters, with one-click submit via portal APIs, Playwright, or email."
    />
  );
}
