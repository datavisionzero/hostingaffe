import { createContext } from "react";
import type { Session } from "./Session";

export const SessionContext = createContext<Session | null>(null);
