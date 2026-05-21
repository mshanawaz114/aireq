// robots.txt — allow the marketing landing, keep the authenticated app + auth
// routes out of the index. (AIRMVP1-405)

import type { MetadataRoute } from "next";

export default function robots(): MetadataRoute.Robots {
  const base = process.env.NEXT_PUBLIC_SITE_URL ?? "http://localhost:3000";
  return {
    rules: [
      {
        userAgent: "*",
        allow: "/",
        disallow: [
          "/dashboard",
          "/matches",
          "/submissions",
          "/escalations",
          "/inbox",
          "/profile",
          "/settings",
          "/tailor",
          "/login",
          "/signup",
        ],
      },
    ],
    sitemap: `${base}/sitemap.xml`,
  };
}
