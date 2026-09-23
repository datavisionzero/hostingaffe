namespace Hostingaffe.Domain.Machines;

/// <summary>
/// The picture a machine is recognised by (<c>CONTEXT.md</c>, Machine;
/// ADR 0021). Closed: every value is a drawing the web application ships with,
/// and nothing is uploaded. A picture is not a kind — <see cref="Rack"/> on a
/// local machine says nothing about the machine.
/// </summary>
public enum Avatar
{
    Monkey,
    Gorilla,
    Sloth,
    Raccoon,
    Fox,
    Owl,
    Penguin,
    Octopus,
    Cat,
    Frog,
    Bear,
    Wolf,
    Lion,
    Puma,
    Robot,
    Rack,
    Turbo,
    Tower,
    Minipc,
    Minimac,
    Desktop,
    Laptop,
    Devbook,
    Aibox,
    Monitor,
    Router,
    Proxy,
    Signpost,
    Firewall,
    Cloud,
    Container,
    Database,
    Harddrive,
    Bucket,
    Floppy,
    Tape,
    Logbook,
    Gauge,
}
