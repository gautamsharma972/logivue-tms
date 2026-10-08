namespace Tms.SharedKernel.India;

/// <summary>
/// The one place that says which commercial region a state (or a well-known city) belongs to: North, South, East, West, Central or North-East.
/// Providers and reports both use it so a "region" filter means the same everywhere. An organisation can override it in its report settings.
/// </summary>
public static class IndiaRegions
{
    public const string Unknown = "Unassigned";

    private static readonly Dictionary<string, string> States = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Delhi"] = "North", ["Haryana"] = "North", ["Punjab"] = "North", ["Himachal Pradesh"] = "North", ["Jammu and Kashmir"] = "North", ["Ladakh"] = "North",
        ["Uttarakhand"] = "North", ["Chandigarh"] = "North", ["Uttar Pradesh"] = "North", ["Rajasthan"] = "North",
        ["Maharashtra"] = "West", ["Gujarat"] = "West", ["Goa"] = "West", ["Dadra and Nagar Haveli and Daman and Diu"] = "West",
        ["Karnataka"] = "South", ["Tamil Nadu"] = "South", ["Kerala"] = "South", ["Andhra Pradesh"] = "South", ["Telangana"] = "South", ["Puducherry"] = "South", ["Lakshadweep"] = "South",
        ["West Bengal"] = "East", ["Odisha"] = "East", ["Bihar"] = "East", ["Jharkhand"] = "East", ["Andaman and Nicobar Islands"] = "East",
        ["Madhya Pradesh"] = "Central", ["Chhattisgarh"] = "Central",
        ["Assam"] = "North-East", ["Meghalaya"] = "North-East", ["Manipur"] = "North-East", ["Mizoram"] = "North-East", ["Nagaland"] = "North-East",
        ["Tripura"] = "North-East", ["Arunachal Pradesh"] = "North-East", ["Sikkim"] = "North-East",
    };

    private static readonly Dictionary<string, string> Cities = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Delhi"] = "Delhi", ["New Delhi"] = "Delhi", ["Gurgaon"] = "Haryana", ["Gurugram"] = "Haryana", ["Faridabad"] = "Haryana", ["Noida"] = "Uttar Pradesh", ["Ghaziabad"] = "Uttar Pradesh",
        ["Lucknow"] = "Uttar Pradesh", ["Kanpur"] = "Uttar Pradesh", ["Agra"] = "Uttar Pradesh", ["Jaipur"] = "Rajasthan", ["Jodhpur"] = "Rajasthan", ["Ludhiana"] = "Punjab", ["Amritsar"] = "Punjab",
        ["Chandigarh"] = "Chandigarh", ["Dehradun"] = "Uttarakhand",
        ["Mumbai"] = "Maharashtra", ["Pune"] = "Maharashtra", ["Nashik"] = "Maharashtra", ["Nagpur"] = "Maharashtra", ["Aurangabad"] = "Maharashtra", ["Thane"] = "Maharashtra", ["Bhiwandi"] = "Maharashtra",
        ["Ahmedabad"] = "Gujarat", ["Surat"] = "Gujarat", ["Vadodara"] = "Gujarat", ["Rajkot"] = "Gujarat", ["Goa"] = "Goa",
        ["Bengaluru"] = "Karnataka", ["Bangalore"] = "Karnataka", ["Mysuru"] = "Karnataka", ["Hubli"] = "Karnataka", ["Chennai"] = "Tamil Nadu", ["Coimbatore"] = "Tamil Nadu", ["Madurai"] = "Tamil Nadu",
        ["Hyderabad"] = "Telangana", ["Vijayawada"] = "Andhra Pradesh", ["Visakhapatnam"] = "Andhra Pradesh", ["Kochi"] = "Kerala", ["Thiruvananthapuram"] = "Kerala",
        ["Kolkata"] = "West Bengal", ["Bhubaneswar"] = "Odisha", ["Patna"] = "Bihar", ["Ranchi"] = "Jharkhand", ["Guwahati"] = "Assam",
        ["Indore"] = "Madhya Pradesh", ["Bhopal"] = "Madhya Pradesh", ["Raipur"] = "Chhattisgarh",
    };

    public static string OfState(string? state) => state is not null && States.TryGetValue(state.Trim(), out var region) ? region : Unknown;

    /// <summary>The region of a city whose state is not known (a lane written as "Mumbai → Pune"). Unlisted cities are <see cref="Unknown"/>.</summary>
    public static string OfCity(string? city) => city is not null && Cities.TryGetValue(city.Trim(), out var state) ? OfState(state) : Unknown;

    public static string? StateOfCity(string? city) => city is not null && Cities.TryGetValue(city.Trim(), out var state) ? state : null;
}
