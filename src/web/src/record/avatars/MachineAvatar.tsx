import type { CSSProperties } from "react";
import { cn } from "@/lib/utils";
import { AVATAR_COLORS, drawingOf, pictureOf, useFaces, type Avatar, type AvatarColor } from "./avatars";

/**
 * A machine's picture: the one chosen for it, or the one its key derives
 * (ADR 0021). Drawn inline so that the colour reaches it, at whatever size the
 * place it stands in gives it.
 */
export function MachineAvatar({
  machine,
  size = 24,
  className,
}: {
  machine: { key: string; avatar?: Avatar | null; avatar_color?: AvatarColor | null };
  size?: number;
  className?: string;
}) {
  const { avatar, color } = pictureOf(machine);
  return <AvatarDrawing avatar={avatar} color={color} size={size} className={className} />;
}

/** One drawing in one colour — what the chooser shows for each of its cells. */
export function AvatarDrawing({
  avatar,
  color,
  size = 24,
  className,
}: {
  avatar: Avatar;
  color: AvatarColor;
  size?: number;
  className?: string;
}) {
  const [faces] = useFaces();
  return (
    <span
      role="img"
      aria-label={`${color} ${avatar}`}
      className={cn("inline-block shrink-0 [&>svg]:block [&>svg]:size-full", className)}
      style={{ width: size, height: size, "--avatar-color": AVATAR_COLORS[color] } as CSSProperties}
      // The repository's own drawings, bundled at build time: nothing a
      // person or the instance wrote ever reaches this.
      dangerouslySetInnerHTML={{ __html: drawingOf(avatar, faces) }}
    />
  );
}
