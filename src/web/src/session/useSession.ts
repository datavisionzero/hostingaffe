import { useContext } from "react";
import { SessionContext } from "./context";
import type { Session } from "./Session";

export function useSession(): Session {
  const session = useContext(SessionContext);

  if (session === null) {
    throw new Error("useSession is only for screens under the shell.");
  }

  return session;
}
