import { NavLink, useLocation } from "react-router";
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  useSidebar,
} from "@/components/ui/sidebar";
import { useSession } from "@/session/useSession";
import { viewPath, views } from "./views";

/**
 * The left navigation of ADR 0006: the views of the instance, in groups. On a
 * phone the same component is the drawer the header button opens — one
 * application, not a reduced one.
 *
 * A group with nothing in it draws nothing. The foundation has one view, and a
 * heading over an empty list is a promise the application does not keep; the
 * groups are here because the entities of the product fill them, and they
 * appear as they do.
 */
export function AppSidebar() {
  const { me } = useSession();
  const { setOpenMobile } = useSidebar();
  const { pathname } = useLocation();

  const groups = (
    [
      { id: "views", label: "Views" },
      { id: "structure", label: "Structure" },
    ] as const
  ).map((group) => ({ ...group, views: views.filter((view) => view.group === group.id) }))
    .filter((group) => group.views.length > 0);

  return (
    <Sidebar collapsible="offcanvas">
      <SidebarHeader className="px-3 pt-3">
        <div className="flex items-center gap-2 px-1 text-sm font-semibold">
          <span aria-hidden className="size-4.5 rounded-sm bg-brand" />
          hostingaffe
        </div>
      </SidebarHeader>

      <SidebarContent>
        <nav aria-label="Views of the instance">
        {groups.map((group) => (
          <SidebarGroup key={group.id}>
            <SidebarGroupLabel>{group.label}</SidebarGroupLabel>
            <SidebarGroupContent>
              <SidebarMenu>
                {group.views
                  .map((view) => {
                    const path = viewPath(view);
                    return (
                      <SidebarMenuItem key={view.id}>
                        <SidebarMenuButton
                          isActive={pathname === path || pathname.startsWith(`${path}/`)}
                          render={<NavLink to={path} onClick={() => setOpenMobile(false)} />}
                        >
                          <view.icon />
                          <span>{view.label}</span>
                        </SidebarMenuButton>
                      </SidebarMenuItem>
                    );
                  })}
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
