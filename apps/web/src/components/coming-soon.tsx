// Shared placeholder for nav routes whose real content lands in a later story.
// Keeps the shell navigable (no 404s) without faking functionality.
//
// Refs: AIRMVP1-106

export function ComingSoon({
  title,
  story,
  blurb,
}: {
  title: string;
  story: string;
  blurb: string;
}) {
  return (
    <div className="p-8">
      <header className="mb-6">
        <p className="text-xs text-slate-500">Workspace</p>
        <h1 className="text-xl font-semibold">{title}</h1>
      </header>
      <section
        aria-labelledby="coming-soon-heading"
        className="rounded-xl border border-ink-700 bg-ink-900 p-6"
      >
        <h2 id="coming-soon-heading" className="text-lg font-medium">
          Coming soon
        </h2>
        <p className="mt-2 max-w-prose text-sm text-slate-400">{blurb}</p>
        <p className="mt-3 text-xs text-slate-500">
          Lands in{" "}
          <code className="rounded bg-ink-800 px-1.5 py-0.5">{story}</code>.
        </p>
      </section>
    </div>
  );
}
