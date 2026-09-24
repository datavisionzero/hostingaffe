import { useState } from "react";
import { api, describe, type Problem, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Popover, PopoverContent, PopoverTitle, PopoverTrigger } from "@/components/ui/popover";
import { Cell, Cells } from "../Choices";
import { EmblemDrawing, ProviderEmblem } from "./ProviderEmblem";
import { EMBLEMS, PALETTE_NAMES, emblemOf } from "./emblems";

type Provider = Schemas["Provider"];

/** How many compositions a row of the grid holds; the arrow keys move by it. */
const COLUMNS = 8;

/**
 * The provider's emblem in its header, and the chooser behind it — the
 * machine's avatar chooser for a provider. A choice is written the moment it
 * is made, without `If-Match`, and "Automatic" clears both fields so the
 * emblem the key derives comes back (ADR 0022).
 */
export function EmblemChooser({ provider, onWritten }: { provider: Provider; onWritten: () => void }) {
  const [why, setWhy] = useState<string>();
  const [saving, setSaving] = useState(false);
  const { emblem, palette } = emblemOf(provider);
  const chosen = provider.emblem != null || provider.emblem_palette != null;

  async function write(body: { emblem?: string; emblem_palette?: string }) {
    setSaving(true);
    setWhy(undefined);
    try {
      // The contract spells name and description out on every write; null
      // leaves them as they are.
      const answer = await api.PATCH("/api/providers/{key}", {
        params: { path: { key: provider.key } },
        body: { name: null, description: null, ...body },
      });
      if (answer.data === undefined) {
        setWhy(describe(answer.error as Problem | undefined, answer.response.status));
        return;
      }
      onWritten();
    } catch {
      setWhy("The instance did not answer.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Popover>
      <PopoverTrigger
        aria-label={`Change the emblem of ${provider.key}`}
        className="rounded-lg outline-none hover:bg-muted focus-visible:ring-2 focus-visible:ring-brand"
      >
        <ProviderEmblem provider={provider} size={40} />
      </PopoverTrigger>
      <PopoverContent className="w-[22rem]">
        <PopoverTitle>Emblem</PopoverTitle>
        <p className="mb-3 text-xs text-muted-foreground">
          {chosen ? "Chosen for this provider." : "Derived from the key until one is chosen."}
        </p>
        <Cells label="Composition" columns={COLUMNS}>
          {EMBLEMS.map((one) => (
            <Cell key={one} pressed={one === emblem} disabled={saving} label={one} onPick={() => void write({ emblem: one })}>
              <EmblemDrawing emblem={one} palette={palette} size={32} />
            </Cell>
          ))}
        </Cells>
        <Cells label="Palette" columns={PALETTE_NAMES.length} className="mt-3">
          {PALETTE_NAMES.map((one) => (
            <Cell key={one} pressed={one === palette} disabled={saving} label={one} onPick={() => void write({ emblem_palette: one })}>
              <EmblemDrawing emblem={emblem} palette={one} size={24} />
            </Cell>
          ))}
        </Cells>
        <div className="mt-3 flex items-center justify-between gap-2">
          {why !== undefined ? <p role="alert" className="text-xs text-destructive">{why}</p> : <span />}
          <Button variant="outline" size="sm" disabled={saving || !chosen} onClick={() => void write({ emblem: "", emblem_palette: "" })}>
            Automatic
          </Button>
        </div>
      </PopoverContent>
    </Popover>
  );
}
