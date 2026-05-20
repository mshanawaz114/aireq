import type { Metadata } from "next";
import { ComingSoon } from "@/components/coming-soon";

export const metadata: Metadata = { title: "Submissions" };

export default function SubmissionsPage() {
  return (
    <ComingSoon
      title="Submissions"
      story="AIRMVP1-306"
      blurb="A live tracker of every application submitted on your behalf, with channel, status, and a full audit trail."
    />
  );
}
