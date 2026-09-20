# An Installation Key Can Be Released Explicitly

An ordinary delete remains reversible and keeps the key reserved, even after the automatic sweep removes the row. A separate `purge` releases an installation key. It requires an already deleted installation, refuses other records that still point at it, and requires the caller to repeat the key. This is a deliberate exception to the never-reuse rule adopted from planaffe ADR 0013; machine and software keys retain that rule.

The act removes the installation's files, revisions and deployments with the row. Pages attached to it, dependencies on it, and Markdown links to `installation:KEY` in pages or descriptions must be cleared first. Deleted records count too, because they may be restored. The old history remains under its old row id; a purge event names the released key, and the machine's history records the purge while the row still exists. If the automatic sweep already removed the row, the act releases its reserved key and writes an instance-wide history event without a machine, because that association is no longer stored.

If the installation was deleted with its machine, restore the machine before purging the installation. Otherwise restoring the machine would bring back only part of what its deletion took.

Purge can follow delete immediately. The grace period belongs to automatic cleanup and restore, not to this confirmed act. Reusing the key creates a new row with a new id and a separate history. Neither a normal delete nor the automatic sweep releases the key.
