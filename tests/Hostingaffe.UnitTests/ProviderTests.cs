using Hostingaffe.Domain;
using Hostingaffe.Domain.Machines;
using Hostingaffe.Domain.Providers;

namespace Hostingaffe.UnitTests;

public sealed class ProviderTests
{
    private static readonly Guid Actor = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_provider_has_a_stable_key_and_editable_description()
    {
        var provider = Provider.Create("example-host", null, Actor, Now);
        Assert.Equal("example-host", provider.Key);
        Assert.Equal("example-host", provider.Name);

        var changes = provider.Apply(new ProviderEdit("Example Host", "Hosting notes."), Actor, Now.AddMinutes(1));
        Assert.Equal(["name", "description"], changes.Select(change => change.Field));
        Assert.Equal("Example Host", provider.Name);
        Assert.Equal("Hosting notes.", provider.Description);
        Assert.Equal(Now.AddMinutes(1), provider.UpdatedAt);
    }

    [Fact]
    public void A_vm_cannot_have_an_independent_provider()
    {
        var vm = Machine.Create("guest", null, MachineKind.Vm, Actor, Now);
        var error = Assert.Throws<ArgumentException>(() => vm.Apply(
            new MachineEdit { Provider = "example-host" }, Actor, Now));
        Assert.Equal("provider", error.ParamName);
    }

    [Fact]
    public void Changing_a_machine_to_a_vm_clears_its_direct_assignment()
    {
        var machine = Machine.Create("guest", null, MachineKind.Vps, Actor, Now);
        machine.Apply(new MachineEdit { Provider = "example-host" }, Actor, Now);

        var changes = machine.Apply(new MachineEdit { Kind = MachineKind.Vm }, Actor, Now.AddMinutes(1));
        Assert.Null(machine.Provider);
        Assert.Contains(changes, change => change is { Field: "provider", OldValue: "example-host", NewValue: null });
    }

    [Fact]
    public void The_emblem_sets_are_spelled_as_the_contract_spells_them()
    {
        Assert.Equal(
            ["orbit", "arch", "peak", "split", "quarter", "stack", "wave", "grid",
             "target", "bloom", "eclipse", "chevron", "bridge", "tiles", "beam", "steps"],
            Enum.GetValues<Emblem>().Select(Spelling.Of));
        Assert.Equal(
            ["bauhaus", "ember", "meadow", "lagoon", "dusk", "citrus", "orchid", "granite", "coral", "glacier"],
            Enum.GetValues<EmblemPalette>().Select(Spelling.Of));
    }

    /// <summary>
    /// An emblem and an avatar share no word, so neither can be mistaken for
    /// the other in a record, a history entry or a flag (ADR 0022).
    /// </summary>
    [Fact]
    public void An_emblem_is_not_an_avatar()
    {
        Assert.Empty(Enum.GetValues<Emblem>().Select(Spelling.Of)
            .Intersect(Enum.GetValues<Avatar>().Select(Spelling.Of)));
        Assert.Empty(Enum.GetValues<EmblemPalette>().Select(Spelling.Of)
            .Intersect(Enum.GetValues<AvatarColor>().Select(Spelling.Of)));
    }

    [Fact]
    public void An_emblem_is_chosen_by_its_word_and_cleared_by_the_empty_string()
    {
        var provider = Provider.Create("example-host", null, Actor, Now);

        var chosen = provider.Apply(new ProviderEdit(Emblem: "orbit", EmblemPalette: "lagoon"), Actor, Now.AddMinutes(1));
        Assert.Equal(Emblem.Orbit, provider.Emblem);
        Assert.Equal(EmblemPalette.Lagoon, provider.EmblemPalette);
        Assert.Equal(
            [("emblem", null, "orbit"), ("emblem_palette", null, "lagoon")],
            chosen.Select(c => (c.Field, c.OldValue, c.NewValue)));

        Assert.Empty(provider.Apply(new ProviderEdit(), Actor, Now.AddMinutes(2)));
        var cleared = provider.Apply(new ProviderEdit(Emblem: string.Empty), Actor, Now.AddMinutes(3));

        Assert.Null(provider.Emblem);
        Assert.Equal(EmblemPalette.Lagoon, provider.EmblemPalette);
        Assert.Equal([("emblem", "orbit", null)], cleared.Select(c => (c.Field, c.OldValue, c.NewValue)));
    }

    [Theory]
    [InlineData("emblem", "fox")]
    [InlineData("emblem", "Orbit")]
    [InlineData("emblem_palette", "teal")]
    public void An_emblem_takes_no_word_outside_its_set(string field, string word)
    {
        var refused = Assert.Throws<ArgumentException>(() => Provider.Create("example-host", null, Actor, Now).Apply(
            field == "emblem" ? new ProviderEdit(Emblem: word) : new ProviderEdit(EmblemPalette: word),
            Actor,
            Now));

        Assert.Equal(field, refused.ParamName);
    }
}
