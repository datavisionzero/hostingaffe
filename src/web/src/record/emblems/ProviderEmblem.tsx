import { cn } from "@/lib/utils";
import { drawingOf, emblemOf, paletteStyle, type Emblem, type EmblemPalette } from "./emblems";

/**
 * A provider's emblem: the one chosen for it, or the one its key derives
 * (ADR 0022). Drawn inline so that the palette reaches it, at whatever size
 * the place it stands in gives it.
 */
export function ProviderEmblem({
  provider,
  size = 24,
  className,
}: {
  provider: { key: string; emblem?: Emblem | null; emblem_palette?: EmblemPalette | null };
  size?: number;
  className?: string;
}) {
  const { emblem, palette } = emblemOf(provider);
  return <EmblemDrawing emblem={emblem} palette={palette} size={size} className={className} />;
}

/** One composition in one palette — what the chooser shows for each of its cells. */
export function EmblemDrawing({
  emblem,
  palette,
  size = 24,
  className,
}: {
  emblem: Emblem;
  palette: EmblemPalette;
  size?: number;
  className?: string;
}) {
  return (
    <span
      role="img"
      aria-label={`${palette} ${emblem}`}
      className={cn("inline-block shrink-0 [&>svg]:block [&>svg]:size-full", className)}
      style={{ width: size, height: size, ...paletteStyle(palette) }}
      // The repository's own drawings, bundled at build time: nothing a
      // person or the instance wrote ever reaches this.
      dangerouslySetInnerHTML={{ __html: drawingOf(emblem) }}
    />
  );
}
