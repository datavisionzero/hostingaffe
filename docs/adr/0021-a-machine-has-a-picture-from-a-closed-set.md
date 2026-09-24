# A Machine Has a Picture From a Closed Set

[ADR 0022](0022-a-provider-has-an-emblem-from-a-closed-set.md) gives providers
an emblem of their own. The last paragraph here still holds for installations
and software.

A list of machines is read by eye, and a column of keys that all look alike
makes the eye work harder than it has to. A machine therefore gets a picture:
`avatar` names one of 38 drawings — animals and devices — and `avatar_color`
one of ten colours. Both are optional fields of the machine and both are closed
sets, spelled like `kind` and `arch`.

The pictures are not uploaded. An upload would make the instance store and
serve binary data it has no other reason to hold, would need limits, formats
and a second place to back up, and would let one person's picture become
unreadable in another person's theme. The drawings are SVG, made for this
product and published under its MIT licence, so no directory carries a second
licence. They ship inside the web application and are never fetched from a
third party. Each one takes its main colour from a CSS variable and keeps its
face, eyes and lights fixed, which is what makes a closed palette of ten enough
and keeps every combination legible in the light and the dark theme. A free
colour value is refused for the same reason.

A machine without a picture is shown with one derived from its key: a stable
hash picks the drawing and the colour. The derived picture is never stored.
The record holds what someone chose; the screen may guess, but its guess is not
a fact about the machine and would otherwise turn into history nobody wrote.

Whether a device is drawn with a face or as a plain symbol is a person's
setting in the web interface, kept in the browser like the theme. It is a
matter of taste, not of the machine, and one person's taste should not change
what everybody else sees. Animals and the robot exist only with a face and keep
it.

The CLI sets and prints the two words and draws nothing. Installations,
software and providers get no picture: the machine is the thing people point at
across the room, and one picture per record type would make none of them
stand out.
