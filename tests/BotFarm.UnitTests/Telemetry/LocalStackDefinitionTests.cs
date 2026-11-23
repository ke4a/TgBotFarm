using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BotFarm.UnitTests.Telemetry;

/// <summary>
/// Reads the local monitoring stack in <c>monitoring/</c> and the app settings in the repository's <c>BotFarm</c>
/// project, and checks that the stack is pinned, reachable only from the developer's machine, loads the BotFarm
/// dashboard and needs no sign-in, that the development settings point the app at it, and that the production
/// settings leave export and the Metrics links off.
/// </summary>
/// <remarks>
/// The compose and provisioning files are read as text with line patterns, so no YAML parser is needed. Of
/// <c>appsettings.Development.json</c>, which can hold a developer's own secrets, only the <c>Telemetry</c> endpoint
/// and dashboard address are read, and their values never appear in a failure message.
/// </remarks>
[TestFixture]
public partial class LocalStackDefinitionTests
{
    private const int GrafanaContainerPort = 3000;
    private const int OtlpHttpContainerPort = 4318;
    private const string DashboardFolder = "dashboards";
    private const string DashboardFileName = "botfarm-dashboard.json";

    /// <summary>
    /// A provisioning file directly in the folder the image reads Grafana dashboard providers from.
    /// </summary>
    private const string ProvisioningFilePattern = @"^/otel-lgtm/grafana/conf/provisioning/dashboards/[^/]+\.ya?ml$";

    private static readonly JsonDocumentOptions SettingsOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private string[] _compose = null!;
    private string[] _provider = null!;

    [OneTimeSetUp]
    public void LoadStack()
    {
        _compose = File.ReadAllLines(PathOf("monitoring", "docker-compose.yml"));
        _provider = File.ReadAllLines(PathOf("monitoring", "grafana", "dashboards.yaml"));
    }

    [Test]
    public void Image_IsPinnedToAVersion()
    {
        var images = _compose.Select(line => ImageLine().Match(line)).Where(match => match.Success).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(images, Has.Count.EqualTo(1));
            Assert.That(images.Select(match => match.Groups["name"].Value), Is.All.EqualTo("grafana/otel-lgtm"));
            Assert.That(images.Select(match => match.Groups["tag"].Value), Is.All.Not.Empty);
            Assert.That(images.Select(match => match.Groups["tag"].Value), Is.All.Not.EqualTo("latest"));
        }
    }

    [Test]
    public void EveryPublishedPort_IsBoundToLocalhost()
    {
        var ports = PublishedPorts().ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ports.Select(port => port.ContainerPort),
                Is.SupersetOf(new[] { GrafanaContainerPort, OtlpHttpContainerPort }));
            Assert.That(ports.Select(port => port.Address), Is.All.EqualTo("127.0.0.1"));
        }
    }

    [Test]
    public void DashboardFolder_IsMountedAtTheProvidersPath()
    {
        var providerPaths = _provider.Select(line => ProviderPathLine().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["path"].Value.TrimEnd('/'))
            .ToList();
        var mounts = BindMounts().ToList();
        // The folder is mounted rather than the file, so Grafana sees edits that replace the file.
        var dashboardTargets = mounts.Where(mount => mount.Source.TrimEnd('/') == $"./grafana/{DashboardFolder}")
            .Select(mount => mount.Target.TrimEnd('/'))
            .ToList();
        var providerTargets = mounts.Where(mount => mount.Source == "./grafana/dashboards.yaml")
            .Select(mount => mount.Target)
            .ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(providerPaths, Has.Count.EqualTo(1));
            Assert.That(dashboardTargets, Is.EqualTo(providerPaths));
            Assert.That(providerTargets, Has.Count.EqualTo(1));
            Assert.That(providerTargets, Is.All.Match(ProvisioningFilePattern));
            Assert.That(File.Exists(PathOf("monitoring", "grafana", DashboardFolder, DashboardFileName)), Is.True);
        }
    }

    [Test]
    public void AnonymousAccess_IsOn()
    {
        var values = _compose.Select(line => AnonymousEnabledLine().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["value"].Value)
            .ToList();

        Assert.That(values, Is.EqualTo(new[] { "true" }));
    }

    [Test]
    public void DevelopmentSettings_PointAtTheLocalStack()
    {
        var ports = PublishedPorts().ToList();
        var otlpPort = ports.Single(port => port.ContainerPort == OtlpHttpContainerPort).HostPort;
        var grafanaPort = ports.Single(port => port.ContainerPort == GrafanaContainerPort).HostPort;
        var telemetry = TelemetryOf("appsettings.Development.json");
        var endpoint = StringOf(telemetry?["OtlpEndpoint"]);
        var dashboardUrl = StringOf(telemetry?["DashboardUrl"]);
        var dashboard = Uri.TryCreate(dashboardUrl, UriKind.Absolute, out var uri) ? uri : null;

        // The values are compared without being passed to NUnit, so a failure message never shows them.
        using (Assert.EnterMultipleScope())
        {
            Assert.That(endpoint == $"http://localhost:{otlpPort}", Is.True,
                $"Telemetry:OtlpEndpoint in appsettings.Development.json is not http://localhost:{otlpPort}.");
            Assert.That(dashboard is { Scheme: "http", Host: "localhost" } && dashboard.Port == grafanaPort
                && dashboard.AbsolutePath.StartsWith("/d/botfarm/", StringComparison.Ordinal), Is.True,
                "Telemetry:DashboardUrl in appsettings.Development.json is not " +
                $"http://localhost:{grafanaPort}/d/botfarm/<slug>.");
        }
    }

    [Test]
    public void DevelopmentCompose_IncludesTheStack()
    {
        var overrideLines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "docker-compose.override.yml"));
        var include = overrideLines.SkipWhile(line => line.TrimEnd() != "include:")
            .Skip(1)
            .TakeWhile(line => line.StartsWith("  ", StringComparison.Ordinal))
            .Select(line => line.Trim());

        Assert.That(include, Does.Contain("- monitoring/docker-compose.yml"));
    }

    [Test]
    public void ProductionSettings_LeaveExportAndMetricsLinksOff()
    {
        var telemetry = TelemetryOf("appsettings.json");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(StringOf(telemetry?["OtlpEndpoint"]), Is.Empty);
            Assert.That(StringOf(telemetry?["DashboardUrl"]), Is.Empty);
        }
    }

    private static string PathOf(params string[] parts) =>
        Path.Combine([TestContext.CurrentContext.TestDirectory, .. parts]);

    /// <summary>
    /// Returns the <c>Telemetry</c> section of the app settings file <paramref name="fileName"/> in the repository's
    /// <c>BotFarm</c> project, and nothing else of it.
    /// </summary>
    private static JsonObject? TelemetryOf(string fileName)
    {
        var path = Path.Combine(RepositoryRoot(), "BotFarm", fileName);
        var settings = JsonNode.Parse(File.ReadAllText(path), documentOptions: SettingsOptions);
        return settings?["Telemetry"] as JsonObject;
    }

    /// <summary>
    /// Returns the nearest folder above the test output that contains <c>BotFarm.sln</c>.
    /// </summary>
    private static string RepositoryRoot()
    {
        for (var folder = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); folder is not null;
             folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "BotFarm.sln")))
            {
                return folder.FullName;
            }
        }

        Assert.Fail("No folder above the test output contains BotFarm.sln.");
        return string.Empty;
    }

    private static string? StringOf(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>
    /// Returns the port mappings listed under every <c>ports:</c> key of the compose file.
    /// </summary>
    private IEnumerable<(string? Address, int HostPort, int ContainerPort)> PublishedPorts()
    {
        for (var i = 0; i < _compose.Length; i++)
        {
            if (_compose[i].Trim() != "ports:")
            {
                continue;
            }

            var indent = IndentOf(_compose[i]);
            for (var j = i + 1; j < _compose.Length && IndentOf(_compose[j]) > indent; j++)
            {
                var item = ListItem().Match(_compose[j]);
                if (!item.Success)
                {
                    continue;
                }

                var mapping = PortMapping().Match(item.Groups["value"].Value);
                Assert.That(mapping.Success, Is.True, $"Line {j + 1} of the compose file is not a port mapping.");
                yield return (mapping.Groups["address"].Success ? mapping.Groups["address"].Value : null,
                    int.Parse(mapping.Groups["host"].Value), int.Parse(mapping.Groups["container"].Value));
            }
        }
    }

    /// <summary>
    /// Returns the compose file's bind mounts of files under <c>monitoring/</c>, as source and container path.
    /// </summary>
    private IEnumerable<(string Source, string Target)> BindMounts() =>
        _compose.Select(line => BindMount().Match(line))
            .Where(match => match.Success)
            .Select(match => (match.Groups["source"].Value, match.Groups["target"].Value));

    private static int IndentOf(string line) =>
        string.IsNullOrWhiteSpace(line) ? int.MaxValue : line.Length - line.TrimStart().Length;

    [GeneratedRegex(@"^\s*image:\s*""?(?<name>[^:""\s]+)(?::(?<tag>[^""\s#]*))?")]
    private static partial Regex ImageLine();

    [GeneratedRegex(@"^\s*-\s*""?(?<value>[^""\s#]+)")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^(?:(?<address>[^:]+):)?(?<host>\d+):(?<container>\d+)(?:/(?:tcp|udp))?$")]
    private static partial Regex PortMapping();

    [GeneratedRegex(@"^\s*-\s*""?(?<source>\./[^:""\s]+):(?<target>/[^:""\s]+)(?::ro)?""?\s*(?:#.*)?$")]
    private static partial Regex BindMount();

    [GeneratedRegex(@"^\s*path:\s*""?(?<path>[^""\s#]+)")]
    private static partial Regex ProviderPathLine();

    [GeneratedRegex(@"^\s*-\s*""?GF_AUTH_ANONYMOUS_ENABLED=(?<value>[^""\s#]+)")]
    private static partial Regex AnonymousEnabledLine();
}
