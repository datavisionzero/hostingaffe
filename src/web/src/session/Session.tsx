import type { ReactNode } from "react";
import type { Me } from "@/api/client";
import { SessionContext } from "./context";

/**
 * Who is signed in, for every screen under the shell. The value is what
 * `GET /me` answered on load; a screen that needs the identity reads it here
 * rather than asking again.
 */
export type Session = {
  me: Me;
  signOut: () => void;
};


export function SessionProvider({ value, children }: { value: Session; children: ReactNode }) {
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}
