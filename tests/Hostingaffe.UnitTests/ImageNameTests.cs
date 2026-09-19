using Hostingaffe.Application.Acts;

namespace Hostingaffe.UnitTests;

/// <summary>
/// The name a container and an installation are matched on. The Docker API
/// spells the same image two ways — <c>docker.io/acme/widget</c> for one
/// container and <c>acme/gadget</c> for the next — and a record keeps the short
/// form, because that is what stands in a Compose file. A comparison that misses
/// on the spelling makes no comparison at all, and silence looks exactly like
/// "in order".
/// </summary>
public sealed class ImageNameTests
{
    [Theory]
    [InlineData("caddy:2.11.4-alpine", "caddy")]
    [InlineData("ghcr.io/datavisionzero/planaffe:0.11.0", "ghcr.io/datavisionzero/planaffe")]
    [InlineData("registry.example.com:5000/team/app:1.2.3", "registry.example.com:5000/team/app")]
    [InlineData("nginx@sha256:abc", "nginx")]
    [InlineData("nginx", "nginx")]
    public void The_tag_is_cut_and_a_registry_port_is_not(string image, string expected) =>
        Assert.Equal(expected, DriftFinder.WithoutTag(image));

    /// <summary>The short form and the form a report carries are one name.</summary>
    [Theory]
    [InlineData("nginx")]
    [InlineData("library/nginx")]
    [InlineData("docker.io/nginx")]
    [InlineData("docker.io/library/nginx")]
    [InlineData("  nginx:1.27  ")]
    [InlineData("docker.io/library/nginx@sha256:abc")]
    public void A_docker_hub_image_of_the_library_is_one_name_however_it_is_written(string image) =>
        Assert.Equal("docker.io/library/nginx", DriftFinder.Normalised(image));

    /// <inheritdoc cref="A_docker_hub_image_of_the_library_is_one_name_however_it_is_written"/>
    [Theory]
    [InlineData("acme/widget")]
    [InlineData("docker.io/acme/widget")]
    [InlineData("docker.io/acme/widget:1.2.3")]
    public void A_docker_hub_image_under_a_user_is_one_name_however_it_is_written(string image) =>
        Assert.Equal("docker.io/acme/widget", DriftFinder.Normalised(image));

    /// <summary>
    /// A registry that is not Docker Hub is left as it stands: the first
    /// segment looks like a host, and <c>localhost</c> is the one host that
    /// looks like nothing.
    /// </summary>
    [Theory]
    [InlineData("ghcr.io/datavisionzero/planaffe:0.11.0", "ghcr.io/datavisionzero/planaffe")]
    [InlineData("registry.example.com:5000/team/app:1.2.3", "registry.example.com:5000/team/app")]
    [InlineData("localhost:5000/app:1.2.3", "localhost:5000/app")]
    [InlineData("localhost/app", "localhost/app")]
    [InlineData("quay.io/acme/widget", "quay.io/acme/widget")]
    public void Another_registry_keeps_the_name_it_carries(string image, string expected) =>
        Assert.Equal(expected, DriftFinder.Normalised(image));

    /// <summary>A name that is nothing stays nothing rather than becoming a library image.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_is_not_turned_into_a_name(string image) =>
        Assert.Equal(string.Empty, DriftFinder.Normalised(image));
}
