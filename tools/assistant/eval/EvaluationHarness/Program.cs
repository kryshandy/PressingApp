using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pressing.Core.Assistant;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length >= 2 && args[0].Equals("--md-from-resume", StringComparison.OrdinalIgnoreCase))
{
    string resumePath = Path.GetFullPath(args[1]);
    string mdPath = args.Length > 2
        ? Path.GetFullPath(args[2])
        : Path.ChangeExtension(resumePath, ".resume.md");
    if (!File.Exists(resumePath)) throw new FileNotFoundException("Résumé absent", resumePath);
    using JsonDocument existing = JsonDocument.Parse(File.ReadAllText(resumePath, Encoding.UTF8));
    File.WriteAllText(mdPath, BuildMarkdown(existing.RootElement.Clone()), new UTF8Encoding(false));
    Console.WriteLine($"Markdown régénéré : {mdPath}");
    return 0;
}

if (args.Length >= 2 && args[0].Equals("--recompute-from-jsonl", StringComparison.OrdinalIgnoreCase))
{
    return await RecomputeFromJsonlAsync(args[1], args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null);
}

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage : EvaluationHarness <dataset.jsonl> <prefixe-sortie> [label]");
    Console.Error.WriteLine("    ou EvaluationHarness --md-from-resume <resume.json> [sortie.md]");
    Console.Error.WriteLine("    ou EvaluationHarness --recompute-from-jsonl <resultats.jsonl> [prefixe-sortie] [label]");
    return 2;
}

string datasetPath = Path.GetFullPath(args[0]);
string outputPrefix = Path.GetFullPath(args[1]);
string label = args.Length > 2 ? args[2] : Path.GetFileNameWithoutExtension(outputPrefix);
string outputDir = Path.GetDirectoryName(outputPrefix)!;
Directory.CreateDirectory(outputDir);

string aiRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pressing", "ai");
string cataloguePath = Path.Combine(aiRoot, "catalogue.json");
if (!File.Exists(datasetPath)) throw new FileNotFoundException("Jeu d'évaluation absent", datasetPath);
if (!File.Exists(cataloguePath)) throw new FileNotFoundException("Catalogue local absent", cataloguePath);

var swCatalog = Stopwatch.StartNew();
CatalogHints catalog = LoadCatalog(cataloguePath);
swCatalog.Stop();

var swPrompt = Stopwatch.StartNew();
_ = AssistantPrompt.BuildSystemPrompt(catalog);
swPrompt.Stop();

var httpTimes = new List<double>();
var totalTimes = new List<double>();
var validationTimes = new List<double>();
var postTimes = new List<double>();
var rawMetrics = new MetricSet();
var finalMetrics = new MetricSet();
var outputLines = new List<string>();
var errors = new List<string>();
int rowCount = 0;
int sourceSynthetique = 0;

using var http = new HttpClient
{
    BaseAddress = new Uri("http://127.0.0.1:8081"),
    Timeout = TimeSpan.FromSeconds(90)
};
var options = new AssistantOptions
{
    LlmUrl = "http://127.0.0.1:8081",
    SttUrl = "http://127.0.0.1:8082",
    LlmTimeoutSeconds = 90
};
var extractor = new LlmIntentExtractor(http, options);

string[] datasetLines = File.ReadAllLines(datasetPath, Encoding.UTF8);
foreach (string rawLine in datasetLines)
{
    if (string.IsNullOrWhiteSpace(rawLine)) continue;
    rowCount++;

    using JsonDocument rowDoc = JsonDocument.Parse(rawLine);
    JsonElement row = rowDoc.RootElement;
    string id = row.GetProperty("id").GetString() ?? $"S{rowCount:D3}";
    string source = row.TryGetProperty("source", out JsonElement src) ? src.GetString() ?? "inconnu" : "inconnu";
    if (source.Equals("synthetique", StringComparison.OrdinalIgnoreCase)) sourceSynthetique++;
    if (row.TryGetProperty("audio", out JsonElement audio) && audio.ValueKind != JsonValueKind.Null)
    {
        throw new InvalidOperationException(
            $"{id} : ce banc évalue le texte uniquement ; aucun fichier audio ne doit être référencé.");
    }

    string reference = row.GetProperty("reference").GetString() ?? "";
    JsonElement expected = row.GetProperty("expected");

    string rawJson = "";
    string? finalJson = null;
    string? error = null;
    double validationMs = 0;
    bool rawSchemaValid = false;
    bool extractionDone = false;
    var swTotal = Stopwatch.StartNew();
    var swHttp = Stopwatch.StartNew();

    try
    {
        IntentExtraction extraction = await extractor.ExtractAsync(reference, catalog);
        swHttp.Stop();
        rawJson = extraction.RawJson;
        finalJson = JsonSerializer.Serialize(extraction.Payload);
        extractionDone = true;

        var swValidation = Stopwatch.StartNew();
        rawSchemaValid = IsValidContract(rawJson, catalog);
        swValidation.Stop();
        validationMs = swValidation.Elapsed.TotalMilliseconds;
        validationTimes.Add(validationMs);
    }
    catch (Exception ex)
    {
        swHttp.Stop();
        error = ex.Message;
        errors.Add($"{id} : {error}");
    }
    swTotal.Stop();

    double httpSeconds = swHttp.Elapsed.TotalSeconds;
    double totalSeconds = swTotal.Elapsed.TotalSeconds;
    httpTimes.Add(httpSeconds);
    totalTimes.Add(totalSeconds);

    var rawMetricBefore = rawMetrics.Snapshot();
    var finalMetricBefore = finalMetrics.Snapshot();

    if (extractionDone && !string.IsNullOrWhiteSpace(rawJson))
    {
        try
        {
            using JsonDocument rawDoc = JsonDocument.Parse(rawJson);
            Compare(expected, rawDoc.RootElement, "", rawMetrics);
        }
        catch (JsonException)
        {
            rawSchemaValid = false;
            CountTotalsOnly(expected, rawMetrics);
        }
        using JsonDocument finalDoc = JsonDocument.Parse(finalJson!);
        Compare(expected, finalDoc.RootElement, "", finalMetrics);
    }
    else
    {
        CountTotalsOnly(expected, rawMetrics);
        CountTotalsOnly(expected, finalMetrics);
    }

    if (rawSchemaValid) rawMetrics.SchemaOk++;
    rawMetrics.SchemaTotal++;
    if (extractionDone) finalMetrics.SchemaOk++;
    finalMetrics.SchemaTotal++;

    if (extractionDone)
    {
        AuditFinal(
            LlmIntentExtractor.ApplyGuardrails(rawJson, reference, catalog),
            reference, catalog, finalMetrics);
        AuditFauxUnknown(expected, finalJson, finalMetrics);
    }

    var record = new Dictionary<string, object?>
    {
        ["id"] = id,
        ["source"] = source,
        ["reference"] = reference,
        ["error"] = error,
        ["httpSeconds"] = Math.Round(httpSeconds, 3),
        ["validationMs"] = Math.Round(validationMs, 3),
        ["totalSeconds"] = Math.Round(totalSeconds, 3),
        ["rawSchemaValid"] = rawSchemaValid,
        ["rawIntent"] = GetString(rawJson, "intent"),
        ["finalIntent"] = GetString(finalJson ?? "", "intent"),
        ["expectedIntent"] = expected.TryGetProperty("intent", out JsonElement ei) ? ei.GetString() : null,
        ["raw"] = TryNode(rawJson),
        ["final"] = TryNode(finalJson ?? ""),
        ["expected"] = expected.Clone(),
        ["rawDelta"] = rawMetrics.DeltaFrom(rawMetricBefore),
        ["finalDelta"] = finalMetrics.DeltaFrom(finalMetricBefore)
    };
    outputLines.Add(JsonSerializer.Serialize(record));

    if (rowCount % 10 == 0)
    {
        Console.WriteLine($"  {rowCount}/{datasetLines.Count(l => !string.IsNullOrWhiteSpace(l))} traitées");
    }
}

string linesPath = outputPrefix + ".jsonl";
File.WriteAllLines(linesPath, outputLines, new UTF8Encoding(false));

var summary = BuildSummary(
    label, datasetPath, cataloguePath, catalog, options, rowCount, sourceSynthetique,
    errors, swCatalog.Elapsed.TotalMilliseconds, swPrompt.Elapsed.TotalMilliseconds,
    validationTimes, postTimes, httpTimes, totalTimes, rawMetrics, finalMetrics, false, null);

WriteSummary(outputPrefix, linesPath, summary, rawMetrics, finalMetrics, errors.Count);
return 0;

static async Task<int> RecomputeFromJsonlAsync(string inputArg, string? outputArg, string? labelArg)
{
    string inputPath = Path.GetFullPath(inputArg);
    if (!File.Exists(inputPath)) throw new FileNotFoundException("Résultats source absents", inputPath);
    string prefix = Path.GetFullPath(outputArg ?? Path.ChangeExtension(inputPath, ".recalcule"));
    string label = labelArg ?? Path.GetFileNameWithoutExtension(prefix);
    Directory.CreateDirectory(Path.GetDirectoryName(prefix)!);

    string aiRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pressing", "ai");
    string cataloguePath = Path.Combine(aiRoot, "catalogue.json");
    if (!File.Exists(cataloguePath)) throw new FileNotFoundException("Catalogue local absent", cataloguePath);
    CatalogHints catalog = LoadCatalog(cataloguePath);

    var rawMetrics = new MetricSet();
    var finalMetrics = new MetricSet();
    var errors = new List<string>();
    var httpTimes = new List<double>();
    var totalTimes = new List<double>();
    var validationTimes = new List<double>();
    var postTimes = new List<double>();
    var outputLines = new List<string>();
    int rowCount = 0;
    int sourceSynthetique = 0;

    foreach (string rawLine in File.ReadAllLines(inputPath, Encoding.UTF8))
    {
        if (string.IsNullOrWhiteSpace(rawLine)) continue;
        rowCount++;
        using JsonDocument rowDoc = JsonDocument.Parse(rawLine);
        JsonElement row = rowDoc.RootElement;
        string id = row.GetProperty("id").GetString() ?? $"R{rowCount:D3}";
        string source = row.TryGetProperty("source", out JsonElement src) ? src.GetString() ?? "inconnu" : "inconnu";
        if (source.Equals("synthetique", StringComparison.OrdinalIgnoreCase)) sourceSynthetique++;
        string reference = row.GetProperty("reference").GetString() ?? "";
        JsonElement expected = row.GetProperty("expected");
        JsonElement raw = Prop(row, "raw");
        string? error = GetStringValue(row, "error");
        if (error is not null) errors.Add($"{id} : {error}");

        if (TryDouble(row, "httpSeconds", out double http)) httpTimes.Add(http);
        if (TryDouble(row, "totalSeconds", out double total)) totalTimes.Add(total);
        if (TryDouble(row, "validationMs", out double validation)) validationTimes.Add(validation);

        string rawText = raw.ValueKind == JsonValueKind.Object ? raw.GetRawText() : "";
        bool rawSchemaValid = rawText.Length > 0 && IsValidContract(rawText, catalog);
        string? finalJson = null;
        IReadOnlyList<string> finalWarnings = [];

        string[] rowProofless = [];
        if (rawText.Length > 0)
        {
            try
            {
                Compare(expected, raw, "", rawMetrics);
                string[] prooflessBefore = finalMetrics.ProoflessFields.ToArray();
                var swPost = Stopwatch.StartNew();
                IntentExtraction recomputed = LlmIntentExtractor.ApplyGuardrails(rawText, reference, catalog);
                swPost.Stop();
                postTimes.Add(swPost.Elapsed.TotalMilliseconds);
                finalWarnings = recomputed.Warnings;
                finalJson = JsonSerializer.Serialize(recomputed.Payload);
                using JsonDocument finalDoc = JsonDocument.Parse(finalJson);
                Compare(expected, finalDoc.RootElement, "", finalMetrics);
                AuditFinal(recomputed, reference, catalog, finalMetrics);
                rowProofless = finalMetrics.ProoflessFields
                    .Where(field => !prooflessBefore.Contains(field, StringComparer.Ordinal))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                AuditFauxUnknown(expected, finalJson, finalMetrics);
            }
            catch (Exception ex)
            {
                error ??= ex.Message;
                errors.Add($"{id} (recalcul) : {ex.Message}");
                CountTotalsOnly(expected, finalMetrics);
            }
        }
        else
        {
            CountTotalsOnly(expected, rawMetrics);
            CountTotalsOnly(expected, finalMetrics);
        }

        if (rawSchemaValid) rawMetrics.SchemaOk++;
        rawMetrics.SchemaTotal++;
        if (finalJson is not null) finalMetrics.SchemaOk++;
        finalMetrics.SchemaTotal++;

        var record = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["source"] = source,
            ["reference"] = reference,
            ["error"] = null,
            ["recomputedError"] = error,
            ["rawSchemaValid"] = rawSchemaValid,
            ["rawIntent"] = GetString(rawText, "intent"),
            ["finalIntent"] = GetString(finalJson ?? "", "intent"),
            ["expectedIntent"] = expected.TryGetProperty("intent", out JsonElement ei) ? ei.GetString() : null,
            ["raw"] = raw.ValueKind == JsonValueKind.Object ? raw.Clone() : null,
            ["final"] = TryNode(finalJson ?? ""),
            ["expected"] = expected.Clone(),
            ["warnings"] = finalWarnings,
            ["prooflessFields"] = rowProofless
        };
        outputLines.Add(JsonSerializer.Serialize(record));
    }

    string linesPath = prefix + ".jsonl";
    File.WriteAllLines(linesPath, outputLines, new UTF8Encoding(false));
    var summary = BuildSummary(
        label, inputPath, cataloguePath, catalog,
        new AssistantOptions { LlmUrl = "http://127.0.0.1:8081", SttUrl = "http://127.0.0.1:8082" },
        rowCount, sourceSynthetique, errors, 0, 0, validationTimes, postTimes, httpTimes, totalTimes,
        rawMetrics, finalMetrics, true, inputPath);
    WriteSummary(prefix, linesPath, summary, rawMetrics, finalMetrics, errors.Count);
    Console.WriteLine($"Recalcul sans appel réseau : {linesPath}");
    return 0;
}

static object BuildSummary(
    string label, string datasetPath, string cataloguePath, CatalogHints catalog,
    AssistantOptions options, int rowCount, int sourceSynthetique, List<string> errors,
    double catalogueMs, double promptMs, List<double> validationTimes, List<double> postTimes,
    List<double> httpTimes, List<double> totalTimes,
    MetricSet rawMetrics, MetricSet finalMetrics, bool recompute, string? sourceJsonl)
{
    return new
    {
        label,
        generatedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"),
        dataset = datasetPath,
        sourceJsonl = recompute ? sourceJsonl : null,
        recomputeFromJsonl = recompute,
        catalog = cataloguePath,
        catalogReferenceDate = catalog.ReferenceDate.ToString("yyyy-MM-dd"),
        modelAlias = options.LlmModelAlias,
        scenarios = rowCount,
        scenariosSynthetiques = sourceSynthetique,
        notes = recompute
            ? new[]
            {
                "Recalcul hors ligne : aucun appel au serveur LLM, aucun audio, aucun WER.",
                "Les latences sont celles mesurées dans le fichier source, pas remesurées.",
                "Sorties finales = JSON brut enregistré, rejoué par LlmIntentExtractor.ApplyGuardrails.",
                "La confiance est informative et exclue des comparaisons exactes."
            }
            : new[]
            {
                "Jeu texte synthétique : aucun audio réel, aucun consentement, WER non mesuré.",
                "Sorties brutes = JSON du modèle avant garde-fous C#.",
                "Sorties finales = IntentPayload après validation et garde-fous de LlmIntentExtractor.",
                "validationMs mesure la validation supplémentaire du JSON brut par le banc ; la validation de production est déjà incluse dans httpSeconds.",
                "La confiance est informative et exclue des comparaisons exactes."
            },
        errors,
        phases = new
        {
            catalogueMs = Math.Round(catalogueMs, 3),
            promptBuildMs = Math.Round(promptMs, 3),
            validationMoyenneMs = Mean(validationTimes),
            postMoyenneMs = Mean(postTimes),
            httpMoyenne = Mean(httpTimes),
            httpP50 = Percentile(httpTimes, 0.50),
            httpP95 = Percentile(httpTimes, 0.95),
            totalMoyen = Mean(totalTimes),
            totalP50 = Percentile(totalTimes, 0.50),
            totalP95 = Percentile(totalTimes, 0.95)
        },
        brut = rawMetrics.ToReport(),
        final = finalMetrics.ToReport()
    };
}

static void WriteSummary(
    string prefix, string linesPath, object summary, MetricSet raw, MetricSet final, int errorCount)
{
    string summaryJsonPath = prefix + ".resume.json";
    string summaryMdPath = prefix + ".resume.md";
    File.WriteAllText(summaryJsonPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    File.WriteAllText(summaryMdPath, BuildMarkdown(summary), new UTF8Encoding(false));
    Console.WriteLine($"Résultats : {linesPath}");
    Console.WriteLine($"Résumé   : {summaryJsonPath}");
    Console.WriteLine($"Intentions finales : {final.IntentOk}/{final.IntentTotal}");
    Console.WriteLine($"Articles finaux    : {final.ItemOk}/{final.ItemTotal}");
    Console.WriteLine($"Informations inventées : {final.Invented}");
    Console.WriteLine($"Champs sans preuve : {final.Proofless}");
    Console.WriteLine($"Hors catalogue : {final.OutOfCatalog}");
    Console.WriteLine($"Faux unknown : {final.FalseUnknown}");
    Console.WriteLine($"Erreurs            : {errorCount}");
}

static bool IsValidContract(string json, CatalogHints catalog)
{
    try
    {
        _ = IntentValidator.Validate(json, catalog);
        return true;
    }
    catch (AssistantContractException)
    {
        return false;
    }
}

static CatalogHints LoadCatalog(string path)
{
    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
    JsonElement root = doc.RootElement;
    string dateText = root.GetProperty("referenceDate").GetString()
        ?? throw new InvalidOperationException("referenceDate absente du catalogue.");

    CatalogTerm[] ReadTerms(string name)
    {
        return root.GetProperty(name).EnumerateArray()
            .Select(item => new CatalogTerm(
                item.GetProperty("label").GetString() ?? "",
                item.TryGetProperty("aliases", out JsonElement aliases) && aliases.ValueKind == JsonValueKind.Array
                    ? aliases.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray()
                    : Array.Empty<string>()))
            .ToArray();
    }

    return new CatalogHints(ReadTerms("categories"), ReadTerms("services"), DateOnly.Parse(dateText));
}

static bool IsMissing(JsonElement element) => element.ValueKind == JsonValueKind.Undefined;

static bool IsNull(JsonElement element) => IsMissing(element) || element.ValueKind == JsonValueKind.Null;

static JsonElement Prop(JsonElement parent, string name)
{
    return parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value)
        ? value
        : default;
}

static string? GetStringValue(JsonElement parent, string name)
{
    JsonElement value = Prop(parent, name);
    return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

static bool TryDouble(JsonElement parent, string name, out double value)
{
    value = 0;
    JsonElement element = Prop(parent, name);
    return element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out value);
}

static bool Equal(JsonElement expected, JsonElement actual)
{
    if (IsNull(expected)) return IsNull(actual);
    if (IsMissing(actual)) return false;
    if (expected.ValueKind == JsonValueKind.String && actual.ValueKind == JsonValueKind.String)
    {
        return string.Equals(expected.GetString(), actual.GetString(), StringComparison.Ordinal);
    }
    if (expected.ValueKind == JsonValueKind.True || expected.ValueKind == JsonValueKind.False ||
        actual.ValueKind == JsonValueKind.True || actual.ValueKind == JsonValueKind.False)
    {
        return expected.ValueKind == actual.ValueKind;
    }
    if (TryDecimal(expected, out decimal left) && TryDecimal(actual, out decimal right))
    {
        return left == right;
    }
    return false;
}

static bool TryDecimal(JsonElement element, out decimal value)
{
    value = 0;
    return element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out value);
}

static void CountField(MetricSet metrics, string name, JsonElement expected, JsonElement actual)
{
    metrics.FieldTotal++;
    bool ok = Equal(expected, actual);
    if (ok) metrics.FieldOk++;

    // Définition historique conservée : attendu null et obtenu non null.
    if (IsNull(expected))
    {
        metrics.NullExpected++;
        if (IsNull(actual)) metrics.NullOk++;
        else metrics.Invented++;
    }

    switch (name)
    {
        case "intent":
            metrics.IntentTotal++;
            if (ok) metrics.IntentOk++;
            AddConfusion(metrics.ConfusionIntent, AsLabel(expected), AsLabel(actual));
            if (expected.ValueKind == JsonValueKind.String &&
                string.Equals(expected.GetString(), AssistantIntentions.Unknown, StringComparison.Ordinal))
            {
                metrics.UnknownTotal++;
                if (ok) metrics.UnknownOk++;
            }
            break;
        case "transcript":
            metrics.TranscriptTotal++;
            if (ok) metrics.TranscriptOk++;
            break;
        case "category":
            metrics.CategoryTotal++;
            if (ok) metrics.CategoryOk++;
            AddConfusion(metrics.ConfusionCategory, AsLabel(expected), AsLabel(actual));
            break;
        case "service":
            metrics.ServiceTotal++;
            if (ok) metrics.ServiceOk++;
            break;
        case "quantity":
            metrics.QuantityTotal++;
            if (ok) metrics.QuantityOk++;
            break;
        case "express":
            metrics.ExpressTotal++;
            if (ok) metrics.ExpressOk++;
            break;
    }
}

static string AsLabel(JsonElement element)
{
    if (IsNull(element)) return "null";
    return element.ValueKind == JsonValueKind.String ? element.GetString() ?? "?" : element.ToString();
}

static void AddConfusion(Dictionary<string, Dictionary<string, int>> matrix, string expected, string actual)
{
    if (!matrix.TryGetValue(expected, out Dictionary<string, int>? line))
    {
        line = new Dictionary<string, int>();
        matrix[expected] = line;
    }

    line.TryGetValue(actual, out int count);
    line[actual] = count + 1;
}

static void Compare(JsonElement expected, JsonElement actual, string path, MetricSet metrics)
{
    if (expected.ValueKind != JsonValueKind.Object) return;

    foreach (JsonProperty property in expected.EnumerateObject())
    {
        if (property.NameEquals("confidence")) continue;
        string childPath = path.Length == 0 ? property.Name : path + "." + property.Name;
        JsonElement actualValue = Prop(actual, property.Name);

        if (property.NameEquals("items") && property.Value.ValueKind == JsonValueKind.Array)
        {
            CompareItems(property.Value, actualValue, metrics);
            continue;
        }

        if (property.Value.ValueKind == JsonValueKind.Object && property.Value.EnumerateObject().Any())
        {
            Compare(property.Value, actualValue, childPath, metrics);
            continue;
        }

        CountField(metrics, property.Name, property.Value, actualValue);
    }
}

static void CompareItems(JsonElement expectedItems, JsonElement actualItems, MetricSet metrics)
{
    JsonElement[] expectedArray = expectedItems.EnumerateArray().ToArray();
    JsonElement[] actualArray = actualItems.ValueKind == JsonValueKind.Array
        ? actualItems.EnumerateArray().ToArray()
        : Array.Empty<JsonElement>();

    metrics.ItemCountTotal++;
    if (expectedArray.Length == actualArray.Length) metrics.ItemCountOk++;

    for (int i = 0; i < expectedArray.Length; i++)
    {
        metrics.ItemTotal++;
        JsonElement actualItem = i < actualArray.Length ? actualArray[i] : default;
        bool allFields = true;
        foreach (JsonProperty field in expectedArray[i].EnumerateObject())
        {
            int before = metrics.FieldOk;
            CountField(metrics, field.Name, field.Value, Prop(actualItem, field.Name));
            if (metrics.FieldOk == before) allFields = false;
        }
        if (allFields) metrics.ItemOk++;
    }
}

static void CountTotalsOnly(JsonElement expected, MetricSet metrics)
{
    // Une erreur d'exécution n'est pas une valeur null correcte : on comptabilise
    // uniquement les dénominateurs, jamais NullOk.
    foreach (JsonProperty property in expected.EnumerateObject())
    {
        if (property.NameEquals("confidence")) continue;
        if (property.NameEquals("items") && property.Value.ValueKind == JsonValueKind.Array)
        {
            JsonElement[] items = property.Value.EnumerateArray().ToArray();
            metrics.ItemCountTotal++;
            metrics.ItemTotal += items.Length;
            foreach (JsonElement item in items)
            {
                foreach (JsonProperty field in item.EnumerateObject())
                {
                    metrics.FieldTotal++;
                    metrics.NullExpected += field.Value.ValueKind == JsonValueKind.Null ? 1 : 0;
                    switch (field.Name)
                    {
                        case "category": metrics.CategoryTotal++; break;
                        case "service": metrics.ServiceTotal++; break;
                        case "quantity": metrics.QuantityTotal++; break;
                        case "express": metrics.ExpressTotal++; break;
                    }
                }
            }
            continue;
        }
        if (property.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty field in property.Value.EnumerateObject())
            {
                metrics.FieldTotal++;
                metrics.NullExpected += field.Value.ValueKind == JsonValueKind.Null ? 1 : 0;
            }
            continue;
        }

        metrics.FieldTotal++;
        metrics.NullExpected += property.Value.ValueKind == JsonValueKind.Null ? 1 : 0;
        switch (property.Name)
        {
            case "intent":
                metrics.IntentTotal++;
                if (property.Value.ValueKind == JsonValueKind.String &&
                    string.Equals(property.Value.GetString(), AssistantIntentions.Unknown, StringComparison.Ordinal))
                {
                    metrics.UnknownTotal++;
                }
                break;
            case "transcript": metrics.TranscriptTotal++; break;
        }
    }
}

static void AuditFinal(IntentExtraction extraction, string reference, CatalogHints catalog, MetricSet metrics)
{
    IntentPayload p = extraction.Payload;
    metrics.AliasNormalized += extraction.Warnings.Count(warning =>
        warning.Contains("normalisé", StringComparison.OrdinalIgnoreCase) ||
        warning.Contains("retrouvé", StringComparison.OrdinalIgnoreCase) ||
        warning.Contains("désambigué", StringComparison.OrdinalIgnoreCase));

    CheckProof("client.name", p.Client.Name, () => TokenProof(p.Client.Name!, reference), metrics, reference);
    CheckProof("client.phone", p.Client.Phone, () => Digits(p.Client.Phone!).Length > 0 && Digits(reference).Contains(Digits(p.Client.Phone!), StringComparison.Ordinal), metrics, reference);
    CheckProof("ticket", p.Ticket, () => TicketProof(p.Ticket!, reference, catalog.ReferenceDate), metrics, reference);
    CheckProof("amount", p.Amount?.ToString(CultureInfo.InvariantCulture), () => NumberProof(p.Amount!.Value, reference), metrics, reference);
    CheckProof("deposit", p.Deposit?.ToString(CultureInfo.InvariantCulture), () => NumberProof(p.Deposit!.Value, reference), metrics, reference);
    CheckProof("payment_method", p.PaymentMethod, () => PaymentProof(p.PaymentMethod!, reference), metrics, reference);
    CheckProof("pickup_date", p.PickupDate, () => DateProof(p.PickupDate!, reference, catalog.ReferenceDate), metrics, reference);
    CheckProof("item_status", p.ItemStatus, () => StatusProof(p.ItemStatus!, reference), metrics, reference);
    CheckProof("transcript", p.Transcript, () => string.Equals(p.Transcript, reference, StringComparison.Ordinal), metrics, reference);

    foreach (IntentItem item in p.Items)
    {
        CheckProof("category", item.Category,
            () => string.Equals(CatalogMatcher.ResolveCategory(catalog, item.Category!, reference).Value, item.Category, StringComparison.Ordinal),
            metrics, reference);
        CheckProof("service", item.Service,
            () => string.Equals(CatalogMatcher.ResolveService(catalog, item.Service!, reference).Value, item.Service, StringComparison.Ordinal),
            metrics, reference);
        string? spokenCategory = CatalogMatcher.SpokenAlias(catalog, item.Category, reference);
        string? spokenService = CatalogMatcher.SpokenService(
            reference,
            CatalogMatcher.ResolveService(catalog, item.Service, reference).Value);

        CheckProof("quantity", item.Quantity?.ToString(CultureInfo.InvariantCulture),
            () => spokenCategory is not null && TranscriptNumbers.NearSafe(reference, spokenCategory, excludeNumberInsideTerm: true) is not null ||
                  spokenService is not null && TranscriptNumbers.NearSafe(reference, spokenService) is not null ||
                  item.Category is not null && TranscriptNumbers.NearSafe(reference, item.Category) is not null ||
                  item.Service is not null && TranscriptNumbers.NearSafe(reference, item.Service) is not null ||
                  item.Quantity == 1 && p.Intent == AssistantIntentions.UpdateItemStatus,
            metrics, reference);
        CheckProof("express", item.Express?.ToString().ToLowerInvariant(),
            () => CatalogHints.Normalize(reference).Contains("express", StringComparison.Ordinal), metrics, reference);

        if (item.Category is not null && !catalog.ContainsCategory(item.Category)) metrics.OutOfCatalog++;
        if (item.Service is not null && !catalog.ContainsService(item.Service)) metrics.OutOfCatalog++;
    }
}

static void CheckProof(string field, string? value, Func<bool> hasProof, MetricSet metrics, string reference)
{
    if (value is null) return;
    metrics.ProofChecked++;
    if (!hasProof())
    {
        metrics.Proofless++;
        metrics.ProoflessFields.Add(field);
    }
}

static bool TokenProof(string name, string reference)
{
    string[] tokens = CatalogHints.Normalize(name).Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(t => t is not ("mme" or "m" or "mr" or "monsieur" or "madame")).ToArray();
    string text = CatalogHints.NormalizeWords(reference);
    return tokens.Length > 0 && tokens.All(t => $" {text} ".Contains($" {t} ", StringComparison.Ordinal));
}

static string Digits(string value) => new(value.Where(char.IsDigit).ToArray());

static bool TicketProof(string ticket, string reference, DateOnly baseDate)
{
    string text = CatalogHints.Normalize(reference);
    if (ticket.StartsWith("PRESS-", StringComparison.OrdinalIgnoreCase))
    {
        string[] parts = ticket.Split('-');
        if (parts.Length == 3 && int.TryParse(parts[2], out int number))
        {
            return text.Contains(number.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
    }
    return text.Contains(CatalogHints.Normalize(ticket), StringComparison.Ordinal);
}

static bool NumberProof(decimal value, string reference) =>
    TranscriptNumbers.Find(reference).Any(n => n.Value == value);

static bool PaymentProof(string method, string reference)
{
    string text = CatalogHints.Normalize(reference);
    return method switch
    {
        "cash" => text.Contains("espece", StringComparison.Ordinal) || text.Contains("cash", StringComparison.Ordinal),
        "mobile_money" => text.Contains("money", StringComparison.Ordinal),
        "card" => text.Contains("carte", StringComparison.Ordinal) || text.Contains("card", StringComparison.Ordinal),
        "transfer" => text.Contains("virement", StringComparison.Ordinal) || text.Contains("transfer", StringComparison.Ordinal),
        _ => false
    };
}

static bool DateProof(string date, string reference, DateOnly baseDate)
{
    string text = CatalogHints.Normalize(reference);
    if (text.Contains(date, StringComparison.Ordinal)) return true;
    if (text.Contains("aujourd hui", StringComparison.Ordinal)) return date == baseDate.ToString("yyyy-MM-dd");
    if (text.Contains("apres demain", StringComparison.Ordinal)) return date == baseDate.AddDays(2).ToString("yyyy-MM-dd");
    if (text.Contains("demain", StringComparison.Ordinal)) return date == baseDate.AddDays(1).ToString("yyyy-MM-dd");
    return false;
}

static bool StatusProof(string status, string reference)
{
    string text = CatalogHints.Normalize(reference);
    return status switch
    {
        "ready" => text.Contains("pret", StringComparison.Ordinal) || text.Contains("prete", StringComparison.Ordinal) || text.Contains("termine", StringComparison.Ordinal),
        "in_progress" => text.Contains("en cours", StringComparison.Ordinal),
        _ => false
    };
}

static void AuditFauxUnknown(JsonElement expected, string? finalJson, MetricSet metrics)
{
    JsonElement expectedIntent = Prop(expected, "intent");
    string expectedText = AsLabel(expectedIntent);
    string? finalIntent = GetString(finalJson ?? "", "intent");
    if (expectedText != AssistantIntentions.Unknown &&
        string.Equals(finalIntent, AssistantIntentions.Unknown, StringComparison.Ordinal))
    {
        metrics.FalseUnknown++;
    }
}

static string? GetString(string json, string name)
{
    if (string.IsNullOrWhiteSpace(json)) return null;
    try
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
    catch (JsonException)
    {
        return null;
    }
}

static JsonNode? TryNode(string json)
{
    try { return JsonNode.Parse(json); }
    catch (JsonException) { return JsonValue.Create(json); }
}

static double Mean(List<double> values) => values.Count == 0 ? 0 : values.Average();

static double Percentile(List<double> values, double fraction)
{
    if (values.Count == 0) return 0;
    double[] sorted = values.OrderBy(x => x).ToArray();
    int index = (int)Math.Min(sorted.Length - 1, Math.Ceiling(fraction * (sorted.Length - 1)));
    return sorted[index];
}

static string BuildMarkdown(object summary)
{
    using JsonDocument doc = JsonDocument.Parse(JsonSerializer.Serialize(summary));
    JsonElement root = doc.RootElement;
    var sb = new StringBuilder();
    sb.AppendLine("# Banc d'évaluation — " + (root.GetProperty("label").GetString() ?? ""));
    sb.AppendLine();
    sb.AppendLine($"- Généré le : {root.GetProperty("generatedAt").GetString()}");
    sb.AppendLine($"- Jeu : `{root.GetProperty("dataset").GetString()}`");
    if (root.TryGetProperty("sourceJsonl", out JsonElement source) && source.ValueKind == JsonValueKind.String)
    {
        sb.AppendLine($"- Recalcul depuis : `{source.GetString()}` (aucun appel réseau)");
    }
    sb.AppendLine($"- Scénarios : {root.GetProperty("scenarios").GetInt32()} (synthétiques : {root.GetProperty("scenariosSynthetiques").GetInt32()})");
    sb.AppendLine($"- Modèle : `{root.GetProperty("modelAlias").GetString()}` · date de référence catalogue : {root.GetProperty("catalogReferenceDate").GetString()}");
    sb.AppendLine($"- Erreurs d'exécution : {root.GetProperty("errors").GetArrayLength()}");
    sb.AppendLine();
    sb.AppendLine("| Mesure | Brut | Après garde-fous C# |");
    sb.AppendLine("|---|---:|---:|");

    string[] keys =
    [
        "intentionsExactes", "articlesExactes", "champsExact", "expressCorrect", "nullCorrect",
        "transcriptionExacte", "schemaValide", "informationsInventees", "champsSansPreuve",
        "horsCatalogue", "fauxUnknown", "valeursNormaliseesViaAlias", "categorieCorrect",
        "serviceCorrect", "quantiteCorrect", "intentionsUnknown"
    ];
    string[] labels =
    [
        "Intentions exactes", "Articles exacts", "Champs exacts", "Express correct", "Null corrects",
        "Transcription exacte", "Schéma valide", "Informations inventées", "Champs sans preuve",
        "Hors catalogue", "Faux unknown", "Valeurs normalisées via alias", "Catégorie correcte",
        "Service correct", "Quantité correcte", "Intentions unknown"
    ];
    for (int i = 0; i < keys.Length; i++)
    {
        JsonElement b = root.GetProperty("brut").GetProperty(keys[i]);
        JsonElement f = root.GetProperty("final").GetProperty(keys[i]);
        sb.AppendLine($"| {labels[i]} | {Format(b)} | {Format(f)} |");
    }

    AppendConfusion(sb, root.GetProperty("final"), "matriceIntentions", "Matrice de confusion — intentions (attendu → obtenu)", "intentions");
    AppendConfusion(sb, root.GetProperty("final"), "matriceCategories", "Matrice de confusion — catégories (attendu → obtenu)", "catégories");

    JsonElement phases = root.GetProperty("phases");
    sb.AppendLine();
    sb.AppendLine("## Phases de latence");
    sb.AppendLine();
    sb.AppendLine("| Phase | Résultat |");
    sb.AppendLine("|---|---:|");
    sb.AppendLine($"| Catalogue (une fois) | {phases.GetProperty("catalogueMs").GetDouble():F3} ms |");
    sb.AppendLine($"| Prompt (une fois) | {phases.GetProperty("promptBuildMs").GetDouble():F3} ms |");
    sb.AppendLine($"| Validation JSON (isolée) | {phases.GetProperty("validationMoyenneMs").GetDouble():F3} ms en moyenne |");
    sb.AppendLine($"| HTTP (moyenne / p50 / p95) | {phases.GetProperty("httpMoyenne").GetDouble():F3} / {phases.GetProperty("httpP50").GetDouble():F3} / {phases.GetProperty("httpP95").GetDouble():F3} s |");
    sb.AppendLine($"| Pipeline total (moyenne / p50 / p95) | {phases.GetProperty("totalMoyen").GetDouble():F3} / {phases.GetProperty("totalP50").GetDouble():F3} / {phases.GetProperty("totalP95").GetDouble():F3} s |");
    sb.AppendLine();
    foreach (JsonElement note in root.GetProperty("notes").EnumerateArray())
    {
        sb.AppendLine("- " + note.GetString());
    }
    return sb.ToString();

    static void AppendConfusion(StringBuilder builder, JsonElement report, string key, string title, string suffix)
    {
        if (!report.TryGetProperty(key, out JsonElement matrix) || matrix.ValueKind != JsonValueKind.Object) return;
        builder.AppendLine();
        builder.AppendLine($"## {title} ({suffix})");
        builder.AppendLine();
        builder.AppendLine("| Attendu | Obtenu | Nombre |");
        builder.AppendLine("|---|---|---:|");
        foreach (JsonProperty line in matrix.EnumerateObject())
        {
            foreach (JsonProperty cell in line.Value.EnumerateObject())
            {
                builder.AppendLine($"| {line.Name} | {cell.Name} | {cell.Value.GetInt32()} |");
            }
        }
    }

    static string Format(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? "";
        if (value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty("ok", out JsonElement ok) && value.TryGetProperty("total", out JsonElement total))
        {
            double percent = value.TryGetProperty("pourcent", out JsonElement p) ? p.GetDouble() : 0;
            return $"{ok.GetInt32()}/{total.GetInt32()} ({percent:F1} %)";
        }
        return value.ToString();
    }
}

sealed class MetricSet
{
    public int IntentTotal, IntentOk;
    public int ItemTotal, ItemOk, ItemCountTotal, ItemCountOk;
    public int FieldTotal, FieldOk;
    public int NullExpected, NullOk, Invented;
    public int TranscriptTotal, TranscriptOk;
    public int CategoryTotal, CategoryOk;
    public int ServiceTotal, ServiceOk;
    public int QuantityTotal, QuantityOk;
    public int ExpressTotal, ExpressOk;
    public int UnknownTotal, UnknownOk;
    public int SchemaTotal, SchemaOk;
    public int ProofChecked, Proofless, OutOfCatalog, FalseUnknown, AliasNormalized;
    public HashSet<string> ProoflessFields = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<string, int>> ConfusionIntent = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<string, int>> ConfusionCategory = new(StringComparer.Ordinal);

    public MetricSet Snapshot() => (MetricSet)MemberwiseClone();

    public Dictionary<string, int> DeltaFrom(MetricSet before) => new()
    {
        ["intentOk"] = IntentOk - before.IntentOk,
        ["intentTotal"] = IntentTotal - before.IntentTotal,
        ["itemOk"] = ItemOk - before.ItemOk,
        ["itemTotal"] = ItemTotal - before.ItemTotal,
        ["fieldOk"] = FieldOk - before.FieldOk,
        ["fieldTotal"] = FieldTotal - before.FieldTotal,
        ["invented"] = Invented - before.Invented,
        ["transcriptOk"] = TranscriptOk - before.TranscriptOk,
        ["expressOk"] = ExpressOk - before.ExpressOk,
        ["expressTotal"] = ExpressTotal - before.ExpressTotal,
        ["proofless"] = Proofless - before.Proofless,
        ["outOfCatalog"] = OutOfCatalog - before.OutOfCatalog,
        ["falseUnknown"] = FalseUnknown - before.FalseUnknown,
        ["aliasNormalized"] = AliasNormalized - before.AliasNormalized
    };

    public object ToReport() => new
    {
        intentionsExactes = Ratio(IntentOk, IntentTotal),
        articlesExactes = Ratio(ItemOk, ItemTotal),
        articlesNombreExact = Ratio(ItemCountOk, ItemCountTotal),
        champsExact = Ratio(FieldOk, FieldTotal),
        expressCorrect = Ratio(ExpressOk, ExpressTotal),
        categorieCorrect = Ratio(CategoryOk, CategoryTotal),
        serviceCorrect = Ratio(ServiceOk, ServiceTotal),
        quantiteCorrect = Ratio(QuantityOk, QuantityTotal),
        nullCorrect = Ratio(NullOk, NullExpected),
        transcriptionExacte = Ratio(TranscriptOk, TranscriptTotal),
        schemaValide = Ratio(SchemaOk, SchemaTotal),
        informationsInventees = Invented,
        intentionsUnknown = Ratio(UnknownOk, UnknownTotal),
        champsSansPreuve = Proofless,
        champsVerifies = ProofChecked,
        champsSansPreuveDetail = ProoflessFields.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
        horsCatalogue = OutOfCatalog,
        fauxUnknown = FalseUnknown,
        valeursNormaliseesViaAlias = AliasNormalized,
        matriceIntentions = ConfusionIntent,
        matriceCategories = ConfusionCategory
    };

    private static object Ratio(int ok, int total) => new
    {
        ok,
        total,
        pourcent = total == 0 ? 0.0 : Math.Round(100.0 * ok / total, 2)
    };
}
