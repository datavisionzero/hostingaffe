import type { ReactNode } from "react";
import { Skeleton } from "@/components/ui/skeleton";
import { PageHeader } from "@/shared/PageHeader";

/**
 * The pieces every detail screen is made of, so that a machine, an
 * installation, a software and a page are read the same way: a stack of
 * sections, each a heading over either a field list or a list of rows.
 *
 * They are here rather than in one screen because the record's screens are the
 * same screen six times over (VISION 6.2), and a heading that drifts on one of
 * them is a screen that reads like another product.
 */
export function Section({ title, meta, action, children }: {
  title: string;
  /** How many, or how long ago — a word beside the heading, never a sentence. */
  meta?: ReactNode;
  /** What can be done to this section, at the other end of its heading. */
  action?: ReactNode;
  children: ReactNode;
}) {
  return (
    <section className="mt-6 border-t py-5">
      <div className="mb-3 flex min-h-6 items-baseline gap-2">
        <h2 className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{title}</h2>
        {meta !== undefined && <span className="text-xs text-muted-foreground">{meta}</span>}
        {action !== undefined && <span className="ml-auto self-center">{action}</span>}
      </div>
      {children}
    </section>
  );
}

/**
 * A field list. A field the record does not hold is left out rather than shown
 * empty: a machine with no IPv6 has no IPv6, and a column of dashes is what
 * `docs/human-interface.md` refuses to make somebody read past.
 */
export function Fields({ of }: { of: [label: string, value: ReactNode][] }) {
  const held = of.filter(([, value]) => value !== null && value !== undefined && value !== "");

  if (held.length === 0) {
    return <p className="text-sm text-muted-foreground">Nothing is recorded here yet.</p>;
  }

  return (
    <dl className="grid gap-x-4 gap-y-1 text-sm sm:grid-cols-[10rem_1fr]">
      {held.map(([label, value]) => (
        <div key={label} className="contents">
          <dt className="text-muted-foreground">{label}</dt>
          <dd className="min-w-0 break-words">{value}</dd>
        </div>
      ))}
    </dl>
  );
}

/** What a section says when the record holds nothing of that kind yet. */
export function Nothing({ children }: { children: ReactNode }) {
  return <p className="text-sm text-muted-foreground">{children}</p>;
}

/** The frame of a detail screen while its subject is still on its way. */
export function Waiting({ title }: { title: string }) {
  return (
    <>
      <PageHeader title={<Skeleton className="h-4 w-64" />} meta={title} />
      <div className="space-y-3 p-4" aria-busy>
        <Skeleton className="h-3 w-full" />
        <Skeleton className="h-3 w-5/6" />
        <Skeleton className="h-3 w-2/3" />
      </div>
    </>
  );
}

/**
 * A screen whose subject did not arrive. The sentence is the instance's own —
 * `not-found`, `deleted` and an instance that did not answer read differently
 * and are different things — and the way back stands under it, because a
 * mistyped address is the usual reason to be here.
 */
export function Failed({ title, why, back }: { title: string; why: string; back: ReactNode }) {
  return (
    <>
      <PageHeader title={title} />
      <p className="p-4 text-sm text-destructive">{why}</p>
      <p className="px-4 text-sm">{back}</p>
    </>
  );
}
