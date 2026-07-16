namespace Therion.Mcp.Evals;

/// <summary>
/// A deterministic, dependency-free token estimate (~4 characters per token, the common rule of thumb).
/// Chosen over a real BPE tokenizer on purpose: the harness stays offline and reproducible, and the
/// reference model's vocab isn't cl100k/o200k anyway, so any BPE would still be an approximation. The
/// absolute numbers are estimates — use them for <em>relative</em> comparison (which tool costs what,
/// full-vs-data catalogs, None/Card/Pack), which is exactly what CAP-02.3 needs.
/// </summary>
public static class TokenEstimator
{
    /// <summary>Approximate token count for arbitrary text (~4 chars/token, rounded up).</summary>
    public static int Estimate(string? text) => string.IsNullOrEmpty(text) ? 0 : (text.Length + 3) / 4;

    /// <summary>Approximate tokens a tool costs in the request's tool list: name + description + input schema.</summary>
    public static int EstimateTool(string name, string? description, string schemaJson) =>
        Estimate(name) + Estimate(description) + Estimate(schemaJson);
}
