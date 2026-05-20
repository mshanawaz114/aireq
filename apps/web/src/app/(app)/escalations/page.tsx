import type { Metadata } from "next";
import { ComingSoon } from "@/components/coming-soon";

export const metadata: Metadata = { title: "Escalations" };

export default function EscalationsPage() {
  return (
    <ComingSoon
      title="Escalations"
      story="AIRMVP1-402"
      blurb="The only things that need your attention — interview requests and meaningful recruiter replies the system can't safely handle on its own."
    />
  );
}
