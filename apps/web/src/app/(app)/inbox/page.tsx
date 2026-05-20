import type { Metadata } from "next";
import { ComingSoon } from "@/components/coming-soon";

export const metadata: Metadata = { title: "Recruiter Inbox" };

export default function InboxPage() {
  return (
    <ComingSoon
      title="Recruiter Inbox"
      story="AIRMVP1-401"
      blurb="Recruiter replies ingested from Gmail, classified by intent (interview / rejection / info request), and threaded against the originating match."
    />
  );
}
