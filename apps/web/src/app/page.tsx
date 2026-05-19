// Root — bounces to /dashboard. The (app) layout's auth gate redirects
// unauthenticated visitors to /login from there.
//
// AIRMVP1-405 replaces this with the real marketing landing page (which is
// itself unauthenticated and lives in (marketing)/).

import { redirect } from "next/navigation";

export default function RootPage() {
  redirect("/dashboard");
}
