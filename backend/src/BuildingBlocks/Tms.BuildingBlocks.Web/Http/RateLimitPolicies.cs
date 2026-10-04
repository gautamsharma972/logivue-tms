namespace Tms.BuildingBlocks.Web.Http;

public static class RateLimitPolicies
{
    /// <summary>Strict per-IP limit for credential endpoints (login, refresh).</summary>
    public const string Auth = "auth";
}
