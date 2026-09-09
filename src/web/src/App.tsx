import { useEffect, useState } from "react";
import { api, whenSignedOut, type Me } from "@/api/client";
import { SignIn } from "@/session/SignIn";
import { SessionProvider } from "@/session/Session";
import { Activate } from "@/session/Activate";
import { Recover } from "@/session/Recover";
import { Device } from "@/session/Device";
import { Shell } from "@/shell/Shell";
import { useLocation, useNavigate } from "react-router";

/**
 * Whether this browser is somebody, which decides the first screen.
 *
 * Every load asks whether the browser's opaque session cookie admits a user.
 * The shell appears once that answers; an absent, expired or revoked session
 * returns to password sign-in.
 */
type Standing =
  | { at: "asking" }
  | { at: "unreachable" }
  | { at: "stranger" }
  | { at: "known"; me: Me };

export function App() {
  const [standing, setStanding] = useState<Standing>({ at: "asking" });
  const location = useLocation();
  const navigate = useNavigate();

  useEffect(() => {
    if (standing.at !== "asking") {
      return;
    }

    let current = true;

    void (async () => {
      try {
        const { data, response } = await api.GET("/api/me");

        if (!current) {
          return;
        }

        if (data !== undefined) {
          setStanding({ at: "known", me: data });
        } else if (response.status === 401) {
          setStanding({ at: "stranger" });
        } else {
          setStanding({ at: "unreachable" });
        }
      } catch {
        if (current) {
          setStanding({ at: "unreachable" });
        }
      }
    })();

    return () => {
      current = false;
    };
  }, [standing.at]);

  useEffect(
    () =>
      whenSignedOut(() => {
        setStanding({ at: "stranger" });
      }),
    [],
  );

  if (location.pathname === "/activate") return <Activate onActivated={(me) => { setStanding({ at: "known", me }); navigate("/"); }} />;
  if (location.pathname === "/recover") return <Recover />;

  switch (standing.at) {
    case "asking":
      // Not a blank page: the frame of the sign-in screen is already the
      // truth, and only the sentence under it is waiting.
      return (
        <main aria-busy className="flex min-h-svh flex-col items-center justify-center gap-3 p-6 text-center">
          <div className="flex items-center gap-2 text-base font-semibold">
            <span aria-hidden className="size-4.5 animate-pulse rounded-sm bg-brand" />
            hostingaffe
          </div>
          <p role="status" className="text-sm text-muted-foreground">
            Checking your session…
          </p>
        </main>
      );

    case "unreachable":
      return (
        <main className="flex min-h-svh flex-col items-center justify-center gap-3 p-6 text-center">
          <p className="font-medium">The instance did not answer.</p>
          <button
            type="button"
            className="text-brand text-sm underline-offset-4 hover:underline"
            onClick={() => setStanding({ at: "asking" })}
          >
            Try again
          </button>
        </main>
      );

    case "stranger":
      // Signing in lands where the browser already was, so that a `/device`
      // link a person followed out of a terminal is not lost by the sign-in
      // that page needed (ADR 0005).
      return <SignIn onSignedIn={(me) => { setStanding({ at: "known", me }); navigate(location.pathname === "/device" ? location.pathname + location.search : "/"); }} />;

    case "known":
      // The one screen a signed-in person reaches without the shell: it is the
      // other half of a `ha login` and is opened from a terminal, not from the
      // navigation.
      if (location.pathname === "/device") return <Device name={standing.me.name} />;

      return (
        <SessionProvider
          value={{
            me: standing.me,
            signOut: () => {
              void api.DELETE("/api/session").finally(() => setStanding({ at: "stranger" }));
            },
          }}
        >
          <Shell />
        </SessionProvider>
      );
  }
}
