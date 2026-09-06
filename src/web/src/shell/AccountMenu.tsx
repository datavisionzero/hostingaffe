import { CheckIcon, KeyboardIcon, LogOutIcon, MonitorIcon, MoonIcon, SettingsIcon, ShieldIcon, SunIcon } from "lucide-react";
import { useNavigate } from "react-router";
import { useTheme } from "@/components/theme-provider";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { useSession } from "@/session/useSession";
import { Keys } from "./ShortcutsDialog";

/**
 * Top right, where every reader of a web application looks for it (ADR 0006):
 * who is signed in, the theme, the keys, settings, sign out.
 *
 * The overview of the keys is here rather than in the sidebar because the
 * sidebar carries the views of one project and the keys belong to the whole
 * application — and because a list of shortcuts reachable only by a shortcut
 * helps nobody who has not found one yet.
 */
export function AccountMenu({ onShortcuts }: { onShortcuts: () => void }) {
  const { me, signOut } = useSession();
  const { theme, setTheme } = useTheme();
  const navigate = useNavigate();

  const themes = [
    { id: "light", label: "Light", icon: SunIcon },
    { id: "dark", label: "Dark", icon: MoonIcon },
    { id: "system", label: "System", icon: MonitorIcon },
  ] as const;

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={<Button variant="ghost" size="icon-sm" aria-label={`Account: ${me.name}`} />}
      >
        <span
          aria-hidden
          className="flex size-6 items-center justify-center rounded-full bg-secondary font-mono text-[11px] font-medium uppercase"
        >
          {me.name.slice(0, 2)}
        </span>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="min-w-52">
        <DropdownMenuGroup>
          <DropdownMenuLabel className="font-normal">
            <div className="font-medium">{me.name}</div>
            <div className="text-xs text-muted-foreground">
              {me.kind}
              {me.administrator ? " · administrator" : ""}
            </div>
          </DropdownMenuLabel>
        </DropdownMenuGroup>
        <DropdownMenuSeparator />
        <DropdownMenuGroup>
          {themes.map((candidate) => (
            <DropdownMenuItem key={candidate.id} onClick={() => setTheme(candidate.id)}>
              <candidate.icon />
              {candidate.label}
              {theme === candidate.id && <CheckIcon className="ml-auto size-3.5" />}
            </DropdownMenuItem>
          ))}
        </DropdownMenuGroup>
        <DropdownMenuSeparator />
        <DropdownMenuItem onClick={onShortcuts}>
          <KeyboardIcon />
          Keyboard shortcuts
          <Keys id="global:shortcuts" className="ml-auto" />
        </DropdownMenuItem>
        <DropdownMenuItem onClick={() => void navigate("/settings")}>
          <SettingsIcon />
          Settings
        </DropdownMenuItem>
        {me.administrator && <DropdownMenuItem onClick={() => void navigate("/admin")}>
          <ShieldIcon />
          Instance administration
        </DropdownMenuItem>}
        <DropdownMenuItem onClick={signOut}>
          <LogOutIcon />
          Sign out
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
