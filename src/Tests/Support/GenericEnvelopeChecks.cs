using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace EgRoomTracker.Tests.Support;

/// <summary>
/// Contrôles de l'enveloppe commune : le schéma <c>docs/schema/generic.schema.json</c> (forme) et les règles croisées du validateur de
/// la plateforme (<c>validateEnvelope</c> de system-filter-backend) : ids uniques (un id répété doit être le même objet partagé, identique), ref résolues, clé unique par objet hors ref,
/// <c>viewerId</c> texte, profondeur 64 au plus ; plus, pour cette sortie, <c>modelKey</c> connue de <c>models[]</c> et boîtes min ≤ max.
/// </summary>
internal static class GenericEnvelopeChecks
{
    private static readonly Lazy<JsonSchema> Schema = new(() =>
        JsonSchema.FromText(File.ReadAllText(ResultSchema.DataPath("generic.schema.json"))));

    private static readonly EvaluationOptions Options = new() { OutputFormat = OutputFormat.List, RequireFormatValidation = true };

    public const int MaxDepth = 64;

    /// <summary>Erreurs de forme (schéma) ; vide si conforme.</summary>
    public static IReadOnlyList<string> SchemaErrors(JsonNode envelope)
    {
        using var document = System.Text.Json.JsonDocument.Parse(envelope.ToJsonString());
        var results = Schema.Value.Evaluate(document.RootElement, Options);
        if (results.IsValid)
        {
            return [];
        }

        var errors = (results.Details ?? [])
            .Where(d => d.Errors is { Count: > 0 })
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation} : {e.Key} : {e.Value}"))
            .ToList();
        return errors.Count > 0 ? errors : ["<racine> : instance non conforme"];
    }

    /// <summary>Erreurs des règles croisées ; vide si conforme.</summary>
    public static IReadOnlyList<string> StructureErrors(JsonNode envelope)
    {
        var errors = new List<string>();
        var modelKeys = (envelope["models"] as JsonArray ?? []).Select(m => m!["key"]!.GetValue<string>()).ToList();
        if (modelKeys.Count != modelKeys.Distinct().Count())
        {
            errors.Add("models[] : clés en double.");
        }

        var ids = new Dictionary<string, string>(StringComparer.Ordinal); // id -> contenu canonique (un objet partagé doit être identique)
        var refs = new List<(string From, string To)>();

        void Walk(JsonNode node, string path, int depth)
        {
            if (depth > MaxDepth)
            {
                errors.Add($"{path} : profondeur maximale dépassée.");
                return;
            }

            var id = node["id"]!.GetValue<string>();
            var shape = Canonical(node);
            if (ids.TryGetValue(id, out var known))
            {
                if (known != shape)
                {
                    errors.Add($"{path} : id en double « {id} » avec un contenu différent (un objet partagé doit être identique).");
                }

                return; // objet partagé déjà parcouru : sous-arbre identique
            }

            ids[id] = shape;

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in (JsonArray)node["properties"]!)
            {
                var key = property!["key"]!.GetValue<string>();
                var type = property["type"]!.GetValue<string>();
                if (type != "ref" && !keys.Add(key))
                {
                    errors.Add($"{path} : clé en double « {key} ».");
                }

                switch (type)
                {
                    case "ref":
                        refs.Add((path, property["value"]!.GetValue<string>()));
                        break;
                    case "box":
                        var box = property["value"]!;
                        for (var axis = 0; axis < 3; axis++)
                        {
                            if (Number(box["min"]![axis]!) > Number(box["max"]![axis]!))
                            {
                                errors.Add($"{path} : box min > max sur l'axe {axis}.");
                            }
                        }

                        break;
                }

                if (key == "viewerId" && type != "text")
                {
                    errors.Add($"{path} : viewerId doit être un texte.");
                }

                if (key == "modelKey" && !modelKeys.Contains(property["value"]!.GetValue<string>()))
                {
                    errors.Add($"{path} : modelKey « {property["value"]} » absente de models[].");
                }
            }

            foreach (var child in (JsonArray)node["children"]!)
            {
                Walk(child!, $"{path}/{id}", depth + 1);
            }
        }

        foreach (var node in (JsonArray)envelope["objects"]!)
        {
            Walk(node!, "objects", 0);
        }

        errors.AddRange(refs.Where(r => !ids.ContainsKey(r.To)).Select(r => $"{r.From} : la référence « {r.To} » ne désigne aucun objet."));
        return errors;
    }

    /// <summary>Forme canonique d'un nœud (clés triées), comme <c>canonical</c> de la plateforme : sert à comparer deux objets de même id.</summary>
    private static string Canonical(JsonNode? node) => node switch
    {
        JsonObject o => "{" + string.Join(",", o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{JsonSerializer.Serialize(p.Key)}:{Canonical(p.Value)}")) + "}",
        JsonArray a => "[" + string.Join(",", a.Select(Canonical)) + "]",
        null => "null",
        _ => node.ToJsonString(),
    };

    /// <summary>Valeur numérique d'un nœud JSON, entier ou décimal.</summary>
    private static double Number(JsonNode node) =>
        double.Parse(node.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);

    public static void AssertValid(JsonNode envelope)
    {
        var errors = SchemaErrors(envelope).Concat(StructureErrors(envelope)).ToList();
        Assert.True(errors.Count == 0, "Enveloppe non conforme :\n" + string.Join("\n", errors));
    }

    public static void AssertValid(string json) => AssertValid(JsonNode.Parse(json)!);

    public static IEnumerable<JsonNode> AllObjects(JsonNode envelope)
    {
        IEnumerable<JsonNode> Walk(JsonNode node)
        {
            yield return node;
            foreach (var child in (JsonArray)node["children"]!)
            {
                foreach (var nested in Walk(child!))
                {
                    yield return nested;
                }
            }
        }

        return ((JsonArray)envelope["objects"]!).SelectMany(o => Walk(o!));
    }
}
