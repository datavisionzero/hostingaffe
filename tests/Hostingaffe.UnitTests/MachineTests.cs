using Hostingaffe.Domain;
using Hostingaffe.Domain.Machines;

namespace Hostingaffe.UnitTests;

/// <summary>
/// What a machine holds itself to without asking the database: its name, its
/// facts, its closed sets, and the rule that only a VM runs on something.
/// </summary>
public sealed class MachineTests
{
    private static readonly Guid Actor = Guid.CreateVersion7();

    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private static Machine A(MachineKind kind = MachineKind.Vps, string key = "ex44") =>
        Machine.Create(key, null, kind, Actor, Now);

    [Fact]
    public void A_machine_without_a_name_is_called_by_its_key()
    {
        var machine = A();

        Assert.Equal("ex44", machine.Key);
        Assert.Equal("ex44", machine.Name);
    }

    [Fact]
    public void A_new_machine_is_active_until_somebody_says_otherwise() => Assert.Equal(Status.Active, A().Status);

    [Fact]
    public void The_key_is_the_shape_of_a_key() =>
        Assert.Throws<ArgumentException>(() => Machine.Create("EX 44", null, MachineKind.Vps, Actor, Now));

    [Fact]
    public void What_changed_is_what_the_machine_reports()
    {
        var machine = A();

        var changes = machine.Apply(
            new MachineEdit { Provider = "hetzner", Plan = "EX44", Arch = Arch.Amd64 }, Actor, Now.AddMinutes(1));

        Assert.Equal(
            [("provider", null, "hetzner"), ("plan", null, "EX44"), ("arch", null, "amd64")],
            changes.Select(c => (c.Field, c.OldValue, c.NewValue)));
        Assert.Equal(Now.AddMinutes(1), machine.UpdatedAt);
    }

    [Fact]
    public void A_field_set_to_what_it_already_says_changed_nothing()
    {
        var machine = A();
        machine.Apply(new MachineEdit { Provider = "hetzner" }, Actor, Now.AddMinutes(1));

        Assert.Empty(machine.Apply(new MachineEdit { Provider = "hetzner" }, Actor, Now.AddMinutes(2)));
        Assert.Equal(Now.AddMinutes(1), machine.UpdatedAt);
    }

    [Fact]
    public void An_absent_field_is_left_alone_and_the_empty_string_clears_it()
    {
        var machine = A();
        machine.Apply(new MachineEdit { Provider = "hetzner", Location = "fsn1-dc14" }, Actor, Now);

        machine.Apply(new MachineEdit { Location = string.Empty }, Actor, Now.AddMinutes(1));

        Assert.Equal("hetzner", machine.Provider);
        Assert.Null(machine.Location);
    }

    [Fact]
    public void The_description_records_that_it_changed_and_not_how()
    {
        var machine = A();

        var change = Assert.Single(machine.Apply(new MachineEdit { Description = "The mail box." }, Actor, Now));

        Assert.Equal("description", change.Field);
        Assert.Null(change.OldValue);
        Assert.Null(change.NewValue);
        Assert.Equal("The mail box.", machine.Description);
    }

    [Theory]
    [InlineData("192.0.2.10", true)]
    [InlineData("2001:db8::1", false)]
    [InlineData("not an address", false)]
    public void An_ipv4_is_an_ipv4(string value, bool accepted)
    {
        var machine = A();
        var apply = () => machine.Apply(new MachineEdit { Ipv4 = value }, Actor, Now);

        if (accepted)
        {
            apply();
            Assert.Equal(value, machine.Ipv4);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => apply());
        }
    }

    [Fact]
    public void A_private_address_is_either_family()
    {
        var machine = A();
        machine.Apply(new MachineEdit { PrivateIp = "2001:db8::1" }, Actor, Now);
        Assert.Equal("2001:db8::1", machine.PrivateIp);

        machine.Apply(new MachineEdit { PrivateIp = "198.51.100.7" }, Actor, Now.AddMinutes(1));
        Assert.Equal("198.51.100.7", machine.PrivateIp);
    }

    [Fact]
    public void A_fact_is_one_line()
    {
        var machine = A();

        Assert.Throws<ArgumentException>(() =>
            machine.Apply(new MachineEdit { Disk = "2×512G\nNVMe" }, Actor, Now));
        Assert.Throws<ArgumentException>(() =>
            machine.Apply(new MachineEdit { Disk = new string('x', Machine.FactMaxLength + 1) }, Actor, Now));
    }

    [Fact]
    public void A_machine_that_stops_being_a_vm_stops_having_a_host()
    {
        var host = A(MachineKind.Dedicated, "ex44");
        var vm = A(MachineKind.Vm, "docker-prod-01");
        vm.Apply(new MachineEdit { HostGiven = true, Host = host }, Actor, Now);
        Assert.Equal(host.Id, vm.HostId);

        var changes = vm.Apply(new MachineEdit { Kind = MachineKind.Local }, Actor, Now.AddMinutes(1));

        Assert.Null(vm.HostId);
        Assert.Contains(changes, c => c is { Field: "host", NewValue: null });
    }

    [Fact]
    public void A_closed_set_takes_no_value_outside_it() =>
        Assert.Throws<ArgumentException>(() => Machine.Create("ex44", null, (MachineKind)42, Actor, Now));

    [Fact]
    public void The_closed_sets_are_spelled_the_way_the_contract_spells_them()
    {
        Assert.Equal(["vps", "dedicated", "vm", "local"], Enum.GetValues<MachineKind>().Select(Spelling.Of));
        Assert.Equal(["amd64", "arm64"], Enum.GetValues<Arch>().Select(Spelling.Of));
        Assert.Equal(["planned", "active", "retired"], Enum.GetValues<Status>().Select(Spelling.Of));
    }
}
