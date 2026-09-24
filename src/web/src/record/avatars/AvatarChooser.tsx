import { useState } from "react";
import { api, describe, type Problem, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Popover, PopoverContent, PopoverTitle, PopoverTrigger } from "@/components/ui/popover";
import { Cell, Cells } from "../Choices";
import { AvatarDrawing, MachineAvatar } from "./MachineAvatar";
import { AVATARS, COLOR_NAMES, pictureOf } from "./avatars";

type Machine = Schemas["Machine"];

/** How many drawings a row of the grid holds; the arrow keys move by it. */
const COLUMNS = 8;

/**
 * The machine's picture in its header, and the chooser behind it.
 *
 * A choice is written the moment it is made, without `If-Match`: a picture
 * is not worth a conflict, and the last choice is the one somebody meant.
 * "Automatic" clears both fields, and the picture the key derives comes back
 * (ADR 0021).
 */
export function AvatarChooser({ machine, onWritten }: { machine: Machine; onWritten: () => void }) {
  const [why, setWhy] = useState<string>();
  const [saving, setSaving] = useState(false);
  const { avatar, color } = pictureOf(machine);
  const chosen = machine.avatar != null || machine.avatar_color != null;

  async function write(body: { avatar?: string; avatar_color?: string }) {
    setSaving(true);
    setWhy(undefined);
    try {
      const answer = await api.PATCH("/api/machines/{key}", { params: { path: { key: machine.key } }, body });
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
        aria-label={`Change the avatar of ${machine.key}`}
        className="rounded-lg outline-none hover:bg-muted focus-visible:ring-2 focus-visible:ring-brand"
      >
        <MachineAvatar machine={machine} size={40} />
      </PopoverTrigger>
      <PopoverContent className="w-[22rem]">
        <PopoverTitle>Avatar</PopoverTitle>
        <p className="mb-3 text-xs text-muted-foreground">
          {chosen ? "Chosen for this machine." : "Derived from the key until one is chosen."}
        </p>
        <Cells label="Picture" columns={COLUMNS}>
          {AVATARS.map((one) => (
            <Cell key={one} pressed={one === avatar} disabled={saving} label={one} onPick={() => void write({ avatar: one })}>
              <AvatarDrawing avatar={one} color={color} size={32} />
            </Cell>
          ))}
        </Cells>
        <Cells label="Colour" columns={COLOR_NAMES.length} className="mt-3">
          {COLOR_NAMES.map((one) => (
            <Cell key={one} pressed={one === color} disabled={saving} label={one} onPick={() => void write({ avatar_color: one })}>
              <AvatarDrawing avatar={avatar} color={one} size={24} />
            </Cell>
          ))}
        </Cells>
        <div className="mt-3 flex items-center justify-between gap-2">
          {why !== undefined ? <p role="alert" className="text-xs text-destructive">{why}</p> : <span />}
          <Button variant="outline" size="sm" disabled={saving || !chosen} onClick={() => void write({ avatar: "", avatar_color: "" })}>
            Automatic
          </Button>
        </div>
      </PopoverContent>
    </Popover>
  );
}
