// Layout for unauthenticated pages — no sidebar, no app chrome.
// Centers the form panel vertically + horizontally on a subtle gradient.

export default function AuthLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="grid min-h-screen place-items-center bg-ink-950 p-6">
      <div className="w-full max-w-sm">{children}</div>
    </div>
  );
}
