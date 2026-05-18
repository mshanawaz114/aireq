// Landing redirect — for AIRMVP1-101 we just bounce to the dashboard.
// AIRMVP1-405 replaces this with the real marketing landing page.

import { redirect } from "next/navigation";

export default function RootPage() {
  redirect("/dashboard");
}
