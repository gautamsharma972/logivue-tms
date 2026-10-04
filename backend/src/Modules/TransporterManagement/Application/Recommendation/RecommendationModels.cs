using LogiVue.Tms.TransporterManagement.Application.Eligibility;

namespace LogiVue.Tms.TransporterManagement.Application.Recommendation;

/// <summary>One weighted factor in a recommendation. A null score means the factor was scored neutrally for lack of data.</summary>
public sealed record ScoreComponent(
    string Factor,
    decimal Score,
    decimal Weight,
    decimal Contribution,
    string Basis,
    bool Sufficient);

public sealed record RankedCandidate(
    int Rank,
    CandidateEvaluation Candidate,
    decimal RecommendationScore,
    IReadOnlyList<ScoreComponent> Components,
    IReadOnlyList<string> Explanations,
    IReadOnlyList<string> Comparisons);

public sealed record RecommendationResult(
    RankedCandidate? Recommended,
    IReadOnlyList<RankedCandidate> Ranked,
    IReadOnlyList<CandidateEvaluation> Candidates);
