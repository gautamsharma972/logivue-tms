using Tms.Modules.Transporters.Domain;

namespace Tms.Modules.Transporters.Application;

internal static class Mapping
{
    public static IReadOnlyList<string> ToNames(this ServiceModes modes) =>
        Enum.GetValues<ServiceModes>().Where(m => m != ServiceModes.None && modes.HasFlag(m)).Select(m => m.ToString()).ToList();

    public static ServiceModes ParseModes(IEnumerable<string> names) =>
        names.Aggregate(ServiceModes.None, (all, name) => all | Enum.Parse<ServiceModes>(name, ignoreCase: true));

    public static TransporterSummaryDto ToSummary(this Transporter t) =>
        new(t.Id, t.Code, t.LegalName, t.TradeName, t.City, t.State, t.Status, t.ServiceModes.ToNames(), t.Phone);

    public static TransporterDto ToDto(this Transporter t, IReadOnlyList<string> missing) =>
        new(
            t.Id, t.Code, t.Status, t.LegalName, t.TradeName, t.Pan, t.Gstin, t.ContactPerson, t.Phone, t.Email,
            new AddressDto(t.AddressLine1, t.AddressLine2, t.City, t.State, t.Pincode),
            t.ServiceModes.ToNames(),
            t.HasBankDetails ? new BankDto(t.BankAccountHolder!, MaskAccount(t.BankAccountNumber!), t.BankIfsc!, t.BankName!) : null,
            t.ApprovalRequestId, t.SuspensionReason, t.ActivatedAt, t.CreatedAt, t.Version, missing);

    public static TransporterProfile ToProfile(this SaveTransporterRequest r) =>
        new(r.LegalName, r.TradeName, r.Pan, r.Gstin, r.ContactPerson, r.Phone, r.Email,
            new Address(r.AddressLine1, r.AddressLine2, r.City, r.State, r.Pincode), ParseModes(r.ServiceModes));

    public static VehicleTypeDto ToDto(this VehicleType t) => new(t.Id, t.Code, t.Name, t.PayloadKg, t.VolumeCbm, t.IsActive, t.Version, t.LengthM, t.WidthM, t.HeightM, t.AllowsHazardous, t.SupportsTemperatureControl);

    public static ComplianceDto ToDto(this ComplianceResult r) => new(r.Status, r.Issues);

    public static DocumentDto ToDto(this ComplianceDocument d, DateOnly today) =>
        new(d.Id, d.TransporterId, d.OwnerKind, d.OwnerId, d.Kind, ComplianceEvaluator.Label(d.Kind), d.Number, d.IssuedOn, d.ExpiresOn,
            d.FileName, d.ContentType, d.SizeBytes, d.StatusOn(today), d.IsCurrent, d.CreatedAt);

    /// <summary>Shows only the last four characters: enough to recognise an account, not enough to misuse it.</summary>
    public static string MaskAccount(string number) =>
        number.Length <= 4 ? new string('•', number.Length) : new string('•', number.Length - 4) + number[^4..];
}
