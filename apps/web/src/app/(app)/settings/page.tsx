import type { Metadata } from "next";
import { ComingSoon } from "@/components/coming-soon";

export const metadata: Metadata = { title: "Settings" };

export default function SettingsPage() {
  return (
    <ComingSoon
      title="Settings"
      story="AIRMVP1-406"
      blurb="Account, billing, sending-domain, and automation-approval preferences."
    />
  );
}
