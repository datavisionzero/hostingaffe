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
}
