import { NavLink, useLocation } from "react-router";
import { SettingsIcon } from "lucide-react";
import type { Project } from "@/api/client";
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuBadge,
  SidebarMenuButton,
  SidebarMenuItem,
  useSidebar,
} from "@/components/ui/sidebar";
import { useSession } from "@/session/useSession";
import type { Attention } from "./attention";
import { useAttention } from "./useAttention";
import { viewPath, views } from "./views";

/**
 * The left navigation of ADR 0006: the views of the current project, in two
 * groups. On a phone the same component is the drawer the header button
 * opens — one application, not a reduced one.
 */
export function AppSidebar({ project }: { project: Project | undefined }) {
  const { me } = useSession();
  const { setOpenMobile } = useSidebar();
  const { pathname } = useLocation();
  const attention = useAttention();

  const groups = [
    { id: "views", label: "Views" },
    { id: "structure", label: "Structure" },
  ] as const;

  return (
    <Sidebar collapsible="offcanvas">
      <SidebarHeader className="px-3 pt-3">
        <div className="flex items-center gap-2 px-1 text-sm font-semibold">
          <span aria-hidden className="size-4.5 rounded-sm bg-brand" />
          hostingaffe
        </div>
      </SidebarHeader>

      <SidebarContent>
        <nav aria-label="Views of the project">
        {groups.map((group) => (
          <SidebarGroup key={group.id}>
            <SidebarGroupLabel>{group.label}</SidebarGroupLabel>
            <SidebarGroupContent>
              <SidebarMenu>
                {views
                  .filter((view) => view.group === group.id)
                  .map((view) => {
                    const path = project === undefined ? "" : viewPath(project.key, view);
                    const count = drawn(counted(view.id, attention));
                    return <SidebarMenuItem key={view.id}>
                      {project === undefined ? (
                        <SidebarMenuButton disabled>
                          <view.icon />
                          <span>{view.label}</span>
                        </SidebarMenuButton>
                      ) : (
                        <SidebarMenuButton
                          isActive={pathname === path || pathname.startsWith(`${path}/`)}
                          // The count belongs to the name of the link, not
                          // beside it: a screen reader says "Needs you, 3"
                          // rather than reading two fragments in a row.
                          aria-label={count === null ? undefined : `${view.label}, ${count}`}
                          render={
                            <NavLink
                              to={path}
                              onClick={() => setOpenMobile(false)}
                            />
                          }
                        >
                          <view.icon />
                          <span>{view.label}</span>
                        </SidebarMenuButton>
                      )}
                      {count !== null && project !== undefined && <SidebarMenuBadge aria-hidden>{count}</SidebarMenuBadge>}
                    </SidebarMenuItem>;
                  })}
                {group.id === "structure" && project !== undefined && <SidebarMenuItem><SidebarMenuButton isActive={pathname === `/${project.key}/settings`} render={<NavLink to={`/${project.key}/settings`} onClick={() => setOpenMobile(false)} />}><SettingsIcon /><span>Project settings</span></SidebarMenuButton></SidebarMenuItem>}
              </SidebarMenu>
            </SidebarGroupContent>
          </SidebarGroup>
        ))}
        </nav>
      </SidebarContent>

      <SidebarFooter className="px-3 pb-3">
        <div className="truncate px-1 text-xs text-muted-foreground">
          {me.name} · {me.kind}
        </div>
      </SidebarFooter>
    </Sidebar>
  );
}

/**
 * Which of the frame's numbers a link carries, if any. "Needs you" is the
 * request and "In progress" the observation; the other links carry none —
 * a number that is always there and barely moves is decoration, and it would
 * take the attention away from the two that mean something.
 */
function counted(id: string, attention: Attention): number | null {
  if (id === "needs-you") {
    return attention.needsYou;
  }

  return id === "in-progress" ? attention.inProgress : null;
}

/**
 * What the badge says, or nothing at all. Zero is not a signal, and an unknown
 * number is not a zero — in both cases the link carries no badge, and it
 * carries no placeholder while the first answer is on its way either. Past a
 * hundred the exact number stops mattering and the width starts to.
 */
function drawn(count: number | null): string | null {
  if (count === null || count <= 0) {
    return null;
  }

  return count > 99 ? "99+" : String(count);
}
