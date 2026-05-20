import type { Metadata } from "next";
import { ComingSoon } from "@/components/coming-soon";

export const metadata: Metadata = { title: "Job Matches" };

export default function MatchesPage() {
  return (
    <ComingSoon
      title="Job Matches"
      story="AIRMVP1-206"
      blurb="Real openings discovered across Adzuna, USAJobs, and ATS boards, scored against your profile with vector matching and explained reasoning."
    />
  );
}
