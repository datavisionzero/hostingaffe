# A Provider Has an Emblem From a Closed Set

[ADR 0021](0021-a-machine-has-a-picture-from-a-closed-set.md) gave the machine
a picture and said that providers get none. This one reverses that sentence for
providers, and only for them: installations and software still get no picture.

A provider stands at the head of a column of machines — on the hosting map, in
the grouped list beside it, on its own screen — and since machines have faces,
a provider drawn as a line of text is the one thing on the map the eye has to
read rather than recognise. It therefore gets an emblem: `emblem` names one of
sixteen geometric compositions and `emblem_palette` one of ten palettes. Both
are optional fields of the provider and both are closed sets, cleared like a
text field, exactly as a machine's `avatar` and `avatar_color` are.

**An emblem is not an avatar.** A machine's picture is a figure — an animal or
a device — drawn in one colour. A provider's emblem is a composition of two or
three plain shapes on a tile of its own, in three colours at once, so that a
provider never looks like one of its machines. The two are separate sets with
separate words, because a contract with one `Avatar` schema holding both would
let a machine be drawn as a composition and a provider as a fox.

A palette carries its own background, and that is why it may be three colours
where a machine's avatar is one: the emblem is a tile, not a figure on the
screen's background, so every combination is legible in the light and the dark
theme without either theme adjusting it. A free colour is refused for the same
reason as in ADR 0021.

Everything else follows ADR 0021 unchanged. Nothing is uploaded; the drawings
are SVG made for this product under its MIT licence and ship inside the web
application. A provider without an emblem is shown with one derived from its
key, which is never stored. The CLI sets and prints the two words and draws
nothing. There is no personal setting: an emblem has no face to switch off.
