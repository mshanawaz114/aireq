// SkillsVocabulary — curated set of ATS-relevant skills/tools/technologies used
// to extract keywords from a job description deterministically.
//
// Why a vocabulary instead of an LLM: ATS analysis runs on-demand when the user
// opens a match, must be instant + free, and benefits from being explainable
// and reproducible. The LLM scorer (205) already supplies contextual "top gaps";
// this gives comprehensive, auditable coverage.
//
// Curation: a starter set across common staffing domains. Extend freely — it's
// just a set. Multi-word phrases ("machine learning") and special-char terms
// ("c#", "ci/cd") are supported by the matcher.
//
// Refs: AIRMVP1-301

using System.Text.RegularExpressions;

namespace Aireq.Api.Ats;

public static partial class SkillsVocabulary
{
    public static readonly IReadOnlyList<string> Terms = new[]
    {
        // Languages
        "c#", "java", "python", "javascript", "typescript", "go", "golang", "rust",
        "c++", "ruby", "php", "scala", "kotlin", "swift", "sql", "bash", "powershell",
        // .NET / web
        ".net", "asp.net", "entity framework", "blazor", "react", "angular", "vue",
        "next.js", "node.js", "express", "django", "flask", "spring", "spring boot",
        "rails", "laravel", "graphql", "rest", "grpc", "html", "css", "tailwind",
        // Cloud / infra
        "aws", "azure", "gcp", "google cloud", "kubernetes", "docker", "terraform",
        "ansible", "helm", "ci/cd", "jenkins", "github actions", "gitlab ci", "argocd",
        "lambda", "ec2", "s3", "cloudformation", "serverless", "microservices",
        // Data
        "postgres", "postgresql", "mysql", "sql server", "oracle", "mongodb", "redis",
        "elasticsearch", "kafka", "rabbitmq", "snowflake", "databricks", "spark",
        "hadoop", "airflow", "dbt", "etl", "data warehouse", "pgvector",
        // ML / AI
        "machine learning", "deep learning", "tensorflow", "pytorch", "nlp",
        "llm", "langchain", "scikit-learn", "pandas", "numpy", "computer vision",
        // Salesforce / enterprise
        "salesforce", "apex", "visualforce", "lwc", "lightning web components",
        "mulesoft", "cpq", "sap", "workday", "servicenow", "dynamics 365",
        // Practices / methodologies
        "agile", "scrum", "kanban", "tdd", "ddd", "devops", "sre",
        "observability", "prometheus", "grafana", "opentelemetry",
        // Mobile
        "ios", "android", "react native", "flutter", "xamarin",
        // Security
        "oauth", "jwt", "saml", "owasp", "penetration testing", "siem",
        // Certs
        "pmp", "aws certified", "cissp", "ckad", "cka", "azure administrator",
    };

    private static readonly HashSet<string> Normalized =
        Terms.Select(t => t.ToLowerInvariant()).ToHashSet();

    /// <summary>Vocabulary terms that appear in the given text (as whole words /
    /// phrases). Returns canonical lowercase terms, de-duplicated, in vocabulary
    /// order for stable output.</summary>
    public static IReadOnlyList<string> ExtractFrom(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        var haystack = text.ToLowerInvariant();
        return Terms
            .Where(term => Contains(haystack, term.ToLowerInvariant()))
            .Distinct()
            .ToList();
    }

    /// <summary>Whole-word/phrase presence test. Alphanumeric terms use word
    /// boundaries (so "java" doesn't match "javascript"); terms with special
    /// chars (c#, c++, .net, ci/cd) fall back to substring since \b is unreliable
    /// around punctuation.</summary>
    public static bool Contains(string lowerHaystack, string lowerTerm)
    {
        if (HasSpecialChars(lowerTerm))
            return lowerHaystack.Contains(lowerTerm, StringComparison.Ordinal);

        var pattern = $@"\b{Regex.Escape(lowerTerm)}\b";
        return Regex.IsMatch(lowerHaystack, pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
    }

    /// <summary>True for the current text if it's a known vocabulary term.</summary>
    public static bool IsKnown(string term) => Normalized.Contains(term.ToLowerInvariant());

    private static bool HasSpecialChars(string term) => SpecialChar().IsMatch(term);

    [GeneratedRegex(@"[^a-z0-9 ]")]
    private static partial Regex SpecialChar();
}
