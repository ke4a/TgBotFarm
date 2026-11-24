using BotFarm.Core;
using BotFarm.Core.Telemetry;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BotFarm.UnitTests.Telemetry;

/// <summary>
/// Reads the Grafana dashboard committed in <c>monitoring/grafana/dashboards/botfarm-dashboard.json</c> and checks that it
/// queries only the metrics the app exports and follows the dashboard's presentation rules.
/// </summary>
[TestFixture]
public partial class DashboardDefinitionTests
{
    private const string DatasourceUid = "${datasource}";

    /// <summary>
    /// The metric names as the monitoring service stores them, from the app's instruments.
    /// </summary>
    private static readonly string[] StoredMetricNames =
    [
        "botfarm_updates_total",
        "botfarm_update_outcomes_total",
        "botfarm_update_duration_seconds_bucket",
        "botfarm_update_duration_seconds_sum",
        "botfarm_update_duration_seconds_count",
        "botfarm_commands_total",
        "botfarm_memory_working_set_bytes",
        "botfarm_memory_gc_heap_bytes",
        "botfarm_memory_threshold_bytes",
        "botfarm_bot_events_total"
    ];

    /// <summary>
    /// The panels whose bars or lines show counts per interval, which need at least two exports in each interval.
    /// </summary>
    private static readonly string[] CountPerIntervalPanels = ["Throughput", "Errors"];

    private JsonObject _dashboard = null!;

    [OneTimeSetUp]
    public void LoadDashboard()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "monitoring", "grafana",
            "dashboards", "botfarm-dashboard.json");
        _dashboard = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }

    private IEnumerable<JsonObject> Panels => _dashboard["panels"]!.AsArray().Select(panel => panel!.AsObject());

    private IEnumerable<JsonObject> QueryAnnotations =>
        _dashboard["annotations"]!["list"]!.AsArray().Select(annotation => annotation!.AsObject())
            .Where(annotation => annotation["datasource"]?["type"]?.GetValue<string>() == "prometheus");

    private IEnumerable<JsonObject> EventAnnotations =>
        QueryAnnotations.Where(annotation =>
            annotation["expr"]!.GetValue<string>().Contains("botfarm_bot_events_total", StringComparison.Ordinal));

    private IEnumerable<JsonObject> Variables =>
        _dashboard["templating"]!["list"]!.AsArray().Select(variable => variable!.AsObject());

    [Test]
    public void Uid_IsBotfarm()
    {
        Assert.That(_dashboard["uid"]?.GetValue<string>(), Is.EqualTo("botfarm"));
    }

    [Test]
    public void Variables_AreAPrometheusDatasourceAndTheBotFromTheThroughputMetric()
    {
        var datasource = Variables.Single(variable => Name(variable) == "datasource");
        var bot = Variables.Single(variable => Name(variable) == "bot");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(datasource["type"]?.GetValue<string>(), Is.EqualTo("datasource"));
            Assert.That(datasource["query"]?.GetValue<string>(), Is.EqualTo("prometheus"));
            Assert.That(bot["type"]?.GetValue<string>(), Is.EqualTo("query"));
            Assert.That(QueryOf(bot), Is.EqualTo("label_values(botfarm_updates_total, bot)"));
            Assert.That(DatasourceUidOf(bot), Is.EqualTo(DatasourceUid));
        }
    }

    [Test]
    public void EveryPanelAndQuery_UsesTheDatasourceVariable()
    {
        using (Assert.EnterMultipleScope())
        {
            foreach (var panel in Panels)
            {
                Assert.That(DatasourceUidOf(panel), Is.EqualTo(DatasourceUid), Title(panel));
                Assert.That(panel["datasource"]?["type"]?.GetValue<string>(), Is.EqualTo("prometheus"), Title(panel));
                foreach (var target in Targets(panel))
                {
                    Assert.That(DatasourceUidOf(target), Is.EqualTo(DatasourceUid), Title(panel));
                }
            }
        }
    }

    [Test]
    public void EveryQuery_UsesOnlyExportedMetricNames()
    {
        var queries = Panels.SelectMany(Targets).Select(target => target["expr"]!.GetValue<string>())
            .Concat(Variables.Select(QueryOf).OfType<string>())
            .Concat(QueryAnnotations.Select(annotation => annotation["expr"]!.GetValue<string>()))
            .ToList();
        var names = queries.SelectMany(query => MetricName().Matches(query).Select(match => match.Value)) .Distinct() .ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(names, Is.Not.Empty);
            Assert.That(names, Is.SubsetOf(StoredMetricNames));
        }
    }

    [Test]
    public void TimeRange_DefaultsTo24HoursAndOffers1Hour24HoursAnd7Days()
    {
        var timepicker = _dashboard["timepicker"]!;
        var quickRanges = timepicker["quick_ranges"]!.AsArray()
            .Select(range => (range!["from"]!.GetValue<string>(), range["to"]!.GetValue<string>()));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_dashboard["time"]?["from"]?.GetValue<string>(), Is.EqualTo("now-24h"));
            Assert.That(_dashboard["time"]?["to"]?.GetValue<string>(), Is.EqualTo("now"));
            Assert.That(quickRanges, Is.EqualTo(new[] { ("now-1h", "now"), ("now-24h", "now"), ("now-7d", "now") }));
            Assert.That(timepicker["time_options"]!.AsArray().Select(option => option!.GetValue<string>()),
                Is.EqualTo(new[] { "1h", "24h", "7d" }));
        }
    }

    [Test]
    public void Panels_AreInTheOrderThroughputErrorsHandlingTimeCommandUsageAppMemory()
    {
        Assert.That(Panels.Select(Title),
            Is.EqualTo(new[] { "Throughput", "Errors", "Handling time", "Command usage", "App memory" }));
    }

    [Test]
    public void Legend_IsHiddenForSingleSeriesTimeSeriesPanelsAndShownOtherwise()
    {
        var timeSeries = Panels.Where(panel => panel["type"]?.GetValue<string>() == "timeseries").ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(timeSeries, Is.Not.Empty);
            foreach (var panel in timeSeries)
            {
                var targets = Targets(panel).ToList();
                var singleSeries = targets.Count == 1 && !targets[0]["expr"]!.GetValue<string>().Contains(" by (");
                Assert.That(panel["options"]?["legend"]?["showLegend"]?.GetValue<bool>(), Is.EqualTo(!singleSeries),
                    Title(panel));
            }
        }
    }

    [Test]
    public void EveryTimeSeriesPanel_ShowsEverySeriesOnHover()
    {
        var timeSeries = Panels.Where(panel => panel["type"]?.GetValue<string>() == "timeseries").ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(timeSeries, Is.Not.Empty);
            foreach (var panel in timeSeries)
            {
                Assert.That(panel["options"]?["tooltip"]?["mode"]?.GetValue<string>(), Is.EqualTo("multi"),
                    Title(panel));
            }
        }
    }

    [Test]
    public void CountPerIntervalPanels_HaveAMinIntervalOfAtLeastTwoMinutes()
    {
        var panels = Panels.Where(panel => CountPerIntervalPanels.Contains(Title(panel))).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(panels.Select(Title), Does.Contain("Throughput"));
            foreach (var panel in panels)
            {
                Assert.That(ParseInterval(panel["interval"]?.GetValue<string>()),
                    Is.GreaterThanOrEqualTo(TimeSpan.FromMinutes(2)), Title(panel));
            }
        }
    }

    [Test]
    public void ThroughputPanel_IsFirstWithStackedBarsOfUpdatesByKind()
    {
        var panel = Panels.First();
        var defaults = panel["fieldConfig"]!["defaults"]!;
        var expr = Targets(panel).Single()["expr"]!.GetValue<string>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Title(panel), Is.EqualTo("Throughput"));
            Assert.That(panel["id"]?.GetValue<int>(), Is.EqualTo(1));
            Assert.That(panel["type"]?.GetValue<string>(), Is.EqualTo("timeseries"));
            Assert.That(defaults["custom"]?["drawStyle"]?.GetValue<string>(), Is.EqualTo("bars"));
            Assert.That(defaults["custom"]?["stacking"]?["mode"]?.GetValue<string>(), Is.EqualTo("normal"));
            Assert.That(defaults["unit"]?.GetValue<string>(), Is.EqualTo("short"));
            Assert.That(expr, Is.EqualTo(StepDifference("kind", "botfarm_updates_total{bot=\"$bot\"}")));
        }
    }

    [Test]
    public void ErrorsPanel_IsSecondWithLinesOfOutcomesByOutcome()
    {
        var panel = Panels.ElementAt(1);
        var defaults = panel["fieldConfig"]!["defaults"]!;
        var expr = Targets(panel).Single()["expr"]!.GetValue<string>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Title(panel), Is.EqualTo("Errors"));
            Assert.That(panel["id"]?.GetValue<int>(), Is.EqualTo(2));
            Assert.That(panel["type"]?.GetValue<string>(), Is.EqualTo("timeseries"));
            Assert.That(defaults["custom"]?["drawStyle"]?.GetValue<string>(), Is.EqualTo("line"));
            Assert.That(expr,
                Is.EqualTo(StepDifference("outcome", "botfarm_update_outcomes_total{bot=\"$bot\"}")));
            Assert.That(DisplayNames(panel), Is.EquivalentTo(new Dictionary<string, string>
            {
                ["failed"] = "Failed",
                ["retried"] = "Retried",
                ["dropped"] = "Dropped",
                ["duplicate"] = "Duplicates skipped",
                ["rejected"] = "Webhook rejected"
            }));
        }
    }

    [Test]
    public void HandlingTimePanel_IsThirdWithP50AndP95InSecondsAndTheWebhookDeadline()
    {
        var panel = Panels.ElementAt(2);
        var targets = Targets(panel).ToList();
        var quantiles = targets
            .Select(target => HistogramQuantile().Match(target["expr"]!.GetValue<string>()))
            .Where(match => match.Success)
            .ToList();
        var deadline = targets.Single(target => target["legendFormat"]?.GetValue<string>() == "Webhook deadline");
        var deadlineValue = VectorValue().Match(deadline["expr"]!.GetValue<string>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Title(panel), Is.EqualTo("Handling time"));
            Assert.That(panel["id"]?.GetValue<int>(), Is.EqualTo(3));
            Assert.That(panel["type"]?.GetValue<string>(), Is.EqualTo("timeseries"));
            Assert.That(panel["fieldConfig"]!["defaults"]!["unit"]?.GetValue<string>(), Is.EqualTo("s"));
            Assert.That(quantiles.Select(match => match.Groups["quantile"].Value), Is.EqualTo(new[] { "0.5", "0.95" }));
            Assert.That(quantiles.Select(match => match.Groups["inner"].Value),
                Is.All.Contain("sum by (le)").And.Contain("botfarm_update_duration_seconds_bucket{bot=\"$bot\"}"));
            Assert.That(deadlineValue.Success, Is.True, deadline["expr"]!.GetValue<string>());
            Assert.That(double.Parse(deadlineValue.Groups["value"].Value, CultureInfo.InvariantCulture),
                Is.EqualTo(Constants.TelegramWebhookTimeout.TotalSeconds));
            Assert.That(DashStyleOf(panel, deadline["refId"]!.GetValue<string>()), Is.EqualTo("dash"));
        }
    }

    [Test]
    public void CommandUsagePanel_IsFourthWithHorizontalBarsOfCommandsOverTheRangeSortedHighestFirst()
    {
        var panel = Panels.ElementAt(3);
        var options = panel["options"]!;
        var target = Targets(panel).Single();
        var expr = target["expr"]!.GetValue<string>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Title(panel), Is.EqualTo("Command usage"));
            Assert.That(panel["id"]?.GetValue<int>(), Is.EqualTo(4));
            Assert.That(panel["type"]?.GetValue<string>(), Is.EqualTo("bargauge"));
            Assert.That(panel["fieldConfig"]!["defaults"]!["unit"]?.GetValue<string>(), Is.EqualTo("short"));
            Assert.That(options["orientation"]?.GetValue<string>(), Is.EqualTo("horizontal"));
            Assert.That(options["reduceOptions"]?["values"]?.GetValue<bool>(), Is.True);
            Assert.That(expr, Does.StartWith("sort_desc(")
                .And.Contain("sum by (command)")
                .And.Contain("increase(botfarm_commands_total{bot=\"$bot\"}[$__range])"));
            Assert.That(target["instant"]?.GetValue<bool>(), Is.True);
            Assert.That(target["range"]?.GetValue<bool>(), Is.False);
            Assert.That(target["legendFormat"]?.GetValue<string>(), Is.EqualTo("/{{command}}"));
            Assert.That(DisplayNames(panel), Is.EquivalentTo(new Dictionary<string, string>
            {
                ["/" + Constants.Telemetry.UnknownCommand] = "Unknown command"
            }));
        }
    }

    [Test]
    public void AppMemoryPanel_IsLastWithWorkingSetGcHeapAndADashedThresholdInIecBytes()
    {
        var panel = Panels.Last();
        var targets = Targets(panel).ToList();
        var threshold = targets.Single(target =>
            target["expr"]!.GetValue<string>() == "max(botfarm_memory_threshold_bytes)");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Title(panel), Is.EqualTo("App memory"));
            Assert.That(panel["id"]?.GetValue<int>(), Is.EqualTo(Constants.Telemetry.MemoryPanelId));
            Assert.That(panel["type"]?.GetValue<string>(), Is.EqualTo("timeseries"));
            Assert.That(panel["fieldConfig"]!["defaults"]!["unit"]?.GetValue<string>(), Is.EqualTo("bytes"));
            Assert.That(targets.Select(target => target["expr"]!.GetValue<string>()), Is.EqualTo(new[]
            {
                "max(botfarm_memory_working_set_bytes)",
                "max(botfarm_memory_gc_heap_bytes)",
                "max(botfarm_memory_threshold_bytes)"
            }));
            Assert.That(targets.Select(target => target["legendFormat"]?.GetValue<string>()),
                Is.EqualTo(new[] { "Working set", "GC heap", "Threshold" }));
            Assert.That(DashStyleOf(panel, threshold["refId"]!.GetValue<string>()), Is.EqualTo("dash"));
        }
    }

    [Test]
    public void EveryBotEvent_IsMarkedByAnEnabledAnnotationForTheSelectedBot()
    {
        var annotations = EventAnnotations.ToList();
        var eventsMarked = annotations
            .Select(annotation => EventFilter().Match(annotation["expr"]!.GetValue<string>()))
            .Where(match => match.Success)
            .Select(match => match.Groups["event"].Value);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(eventsMarked,
                Is.EquivalentTo(Enum.GetValues<BotEvent>().Select(botEvent => botEvent.ToTagValue())));
            foreach (var annotation in annotations)
            {
                var name = annotation["name"]?.GetValue<string>();
                var expr = annotation["expr"]!.GetValue<string>();
                Assert.That(DatasourceUidOf(annotation), Is.EqualTo(DatasourceUid), name);
                Assert.That(annotation["enable"]?.GetValue<bool>(), Is.True, name);
                var botEvent = EventFilter().Match(expr).Groups["event"].Value;
                Assert.That(expr, Is.EqualTo(StepDifference(null,
                    $"botfarm_bot_events_total{{bot=\"$bot\", event=\"{botEvent}\"}}") + " > 0"), name);
                Assert.That(annotation["titleFormat"]?.GetValue<string>(), Is.EqualTo(name), name);
            }
        }
    }

    [Test]
    public void AppRestarts_AreMarkedWhenANewProcessInstanceStartsReporting()
    {
        var restart = QueryAnnotations.Single(annotation => annotation["name"]?.GetValue<string>() == "App restarted");
        var expr = restart["expr"]!.GetValue<string>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(DatasourceUidOf(restart), Is.EqualTo(DatasourceUid));
            Assert.That(restart["enable"]?.GetValue<bool>(), Is.True);
            Assert.That(expr, Is.EqualTo(
                "count by (instance) (botfarm_memory_working_set_bytes) unless " +
                "count by (instance) (botfarm_memory_working_set_bytes offset $__interval)"));
            Assert.That(restart["step"]?.GetValue<string>(), Is.EqualTo("2m"));
        }
    }

    [Test]
    public void NoPanel_HasASecondYAxis()
    {
        using (Assert.EnterMultipleScope())
        {
            foreach (var panel in Panels)
            {
                var placements = panel["fieldConfig"]!["overrides"]!.AsArray()
                    .SelectMany(o => o!["properties"]!.AsArray())
                    .Where(property => property!["id"]?.GetValue<string>() == "custom.axisPlacement")
                    .Select(property => property!["value"]?.GetValue<string>());
                Assert.That(placements, Is.Empty, Title(panel));
                Assert.That(panel["fieldConfig"]!["defaults"]!["custom"]?["axisPlacement"]?.GetValue<string>(),
                    Is.Not.EqualTo("right"), Title(panel));
            }
        }
    }

    private static IEnumerable<JsonObject> Targets(JsonObject panel) =>
        panel["targets"]!.AsArray().Select(target => target!.AsObject());

    private static string? Title(JsonObject panel) => panel["title"]?.GetValue<string>();

    private static string? Name(JsonObject variable) => variable["name"]?.GetValue<string>();

    private static string? DatasourceUidOf(JsonObject node) => node["datasource"]?["uid"]?.GetValue<string>();

    private static string? QueryOf(JsonObject variable) => variable["query"] switch
    {
        JsonObject query => query["query"]?.GetValue<string>(),
        JsonValue query => query.GetValue<string>(),
        _ => null
    };

    /// <summary>
    /// Returns the display names that <paramref name="panel"/>'s overrides give its series, by series name.
    /// </summary>
    private static Dictionary<string, string> DisplayNames(JsonObject panel) =>
        panel["fieldConfig"]!["overrides"]!.AsArray()
            .Where(o => o!["matcher"]?["id"]?.GetValue<string>() == "byName")
            .SelectMany(o => o!["properties"]!.AsArray()
                .Where(property => property!["id"]?.GetValue<string>() == "displayName")
                .Select(property => (Series: o["matcher"]!["options"]!.GetValue<string>(),
                    Name: property!["value"]!.GetValue<string>())))
            .ToDictionary(pair => pair.Series, pair => pair.Name);

    /// <summary>
    /// Returns the line style (<c>solid</c>, <c>dash</c> or <c>dot</c>) that <paramref name="panel"/>'s overrides give
    /// the series of query <paramref name="refId"/>, or null when they give it none.
    /// </summary>
    private static string? DashStyleOf(JsonObject panel, string refId) =>
        panel["fieldConfig"]!["overrides"]!.AsArray()
            .Where(o => o!["matcher"]?["id"]?.GetValue<string>() == "byFrameRefID"
                        && o["matcher"]?["options"]?.GetValue<string>() == refId)
            .SelectMany(o => o!["properties"]!.AsArray())
            .Where(property => property!["id"]?.GetValue<string>() == "custom.lineStyle")
            .Select(property => property!["value"]?["fill"]?.GetValue<string>())
            .SingleOrDefault();

    private static TimeSpan ParseInterval(string? interval)
    {
        var match = IntervalPattern().Match(interval ?? string.Empty);
        Assert.That(match.Success, Is.True, $"'{interval}' is not an interval such as 2m.");

        var value = int.Parse(match.Groups["value"].Value);
        return match.Groups["unit"].Value switch
        {
            "s" => TimeSpan.FromSeconds(value),
            "m" => TimeSpan.FromMinutes(value),
            _ => TimeSpan.FromHours(value)
        };
    }

    /// <summary>
    /// The query that counts what a counter added in each step: its value minus its value one step earlier, summed.
    /// Unlike <c>increase</c> over a window as long as the step, it never misses an increase that falls between two
    /// windows, since the differences of consecutive steps add up to the whole change.
    /// </summary>
    private static string StepDifference(string? groupBy, string selector)
    {
        var sum = groupBy is null ? "sum" : $"sum by ({groupBy}) ";
        return $"{sum}({selector} - {selector} offset $__interval)";
    }

    [GeneratedRegex(@"\bbotfarm_[a-z_]+")]
    private static partial Regex MetricName();

    [GeneratedRegex(@"^histogram_quantile\((?<quantile>[0-9.]+),\s*(?<inner>.+)\)$")]
    private static partial Regex HistogramQuantile();

    [GeneratedRegex(@"^vector\((?<value>[0-9.]+)\)$")]
    private static partial Regex VectorValue();

    [GeneratedRegex(@"^(?<value>\d+)(?<unit>[smh])$")]
    private static partial Regex IntervalPattern();

    [GeneratedRegex("event=\"(?<event>[a-z_]+)\"")]
    private static partial Regex EventFilter();
}
