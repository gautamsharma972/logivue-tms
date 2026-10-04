using System.Text.RegularExpressions;
using Tms.SharedKernel.Domain;
using Tms.SharedKernel.India;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Transporters.Domain;

/// <summary>A person to call at a transporter, beyond the main contact on the master record: dispatch desk, accounts, escalation.</summary>
public sealed class TransporterContact : AggregateRoot, ITenantScoped
{
    private TransporterContact()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid TransporterId { get; private set; }

    public string Name { get; private set; } = null!;

    public string? Designation { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    /// <summary>What the person is for: Operations, Accounts, Escalation, Sales…</summary>
    public string ContactType { get; private set; } = null!;

    public bool IsPrimary { get; private set; }

    public bool IsActive { get; private set; } = true;

    public static Result<TransporterContact> Create(Guid tenantId, Guid transporterId, string name, string? designation, string? email, string? phone, string contactType, bool isPrimary)
    {
        var contact = new TransporterContact { TenantId = tenantId, TransporterId = transporterId };
        var set = contact.Set(name, designation, email, phone, contactType, isPrimary, true);
        return set.IsFailure ? set.Error : contact;
    }

    public Result Set(string name, string? designation, string? email, string? phone, string contactType, bool isPrimary, bool isActive)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 150)
        {
            errors["name"] = ["Enter the person's name (up to 150 characters)."];
        }

        if (string.IsNullOrWhiteSpace(contactType) || contactType.Trim().Length > 50)
        {
            errors["contactType"] = ["Say what this person is for (up to 50 characters)."];
        }

        if (!string.IsNullOrWhiteSpace(email) && (email.Trim().Length > 254 || !Regex.IsMatch(email.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.None, TimeSpan.FromMilliseconds(100))))
        {
            errors["email"] = ["Enter a valid email address."];
        }

        if (!string.IsNullOrWhiteSpace(phone) && !IndianIdentifiers.IsValidMobile(phone))
        {
            errors["phone"] = ["Enter a valid 10-digit mobile number."];
        }

        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(phone))
        {
            errors["phone"] = ["Give a phone number or an email so the person can be reached."];
        }

        if (isPrimary && !isActive)
        {
            errors["isPrimary"] = ["An inactive contact cannot be the primary one."];
        }

        if (errors.Count > 0)
        {
            return Error.Validation("validation.failed", "One or more fields are invalid.") with { ValidationErrors = errors };
        }

        Name = name.Trim();
        Designation = string.IsNullOrWhiteSpace(designation) ? null : designation.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : IndianIdentifiers.NormaliseMobile(phone);
        ContactType = contactType.Trim();
        IsPrimary = isPrimary;
        IsActive = isActive;
        return Result.Success();
    }

    public void ClearPrimary() => IsPrimary = false;
}

/// <summary>A depot, hub or office of a transporter, with its location. Where a transporter's vehicles are based.</summary>
public sealed class TransporterBranch : AggregateRoot, ITenantScoped
{
    private TransporterBranch()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid TransporterId { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? Address { get; private set; }

    public string? City { get; private set; }

    public string? State { get; private set; }

    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    public string? ContactName { get; private set; }

    public string? ContactPhone { get; private set; }

    public bool IsActive { get; private set; } = true;

    public static Result<TransporterBranch> Create(
        Guid tenantId, Guid transporterId, string code, string name, string? address, string? city, string? state, double? latitude, double? longitude, string? contactName, string? contactPhone)
    {
        var branch = new TransporterBranch { TenantId = tenantId, TransporterId = transporterId };
        var set = branch.Set(code, name, address, city, state, latitude, longitude, contactName, contactPhone, true);
        return set.IsFailure ? set.Error : branch;
    }

    public Result Set(string code, string name, string? address, string? city, string? state, double? latitude, double? longitude, string? contactName, string? contactPhone, bool isActive)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(code) || !Regex.IsMatch(code.Trim(), "^[A-Za-z0-9][A-Za-z0-9-]{1,29}$", RegexOptions.None, TimeSpan.FromMilliseconds(100)))
        {
            errors["code"] = ["Use 2–30 letters, digits or hyphens."];
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 150)
        {
            errors["name"] = ["Enter the branch name (up to 150 characters)."];
        }

        if ((latitude is null) != (longitude is null))
        {
            errors["latitude"] = ["Give both latitude and longitude, or neither."];
        }
        else if (latitude is { } lat && longitude is { } lon && (lat is < 6.0 or > 37.5 || lon is < 68.0 or > 98.0))
        {
            errors["latitude"] = ["That point is outside India. Check that latitude and longitude are not swapped."];
        }

        if (!string.IsNullOrWhiteSpace(contactPhone) && !IndianIdentifiers.IsValidMobile(contactPhone))
        {
            errors["contactPhone"] = ["Enter a valid 10-digit mobile number."];
        }

        if (errors.Count > 0)
        {
            return Error.Validation("validation.failed", "One or more fields are invalid.") with { ValidationErrors = errors };
        }

        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        Address = Clean(address);
        City = Clean(city);
        State = Clean(state);
        Latitude = latitude;
        Longitude = longitude;
        ContactName = Clean(contactName);
        ContactPhone = string.IsNullOrWhiteSpace(contactPhone) ? null : IndianIdentifiers.NormaliseMobile(contactPhone);
        IsActive = isActive;
        return Result.Success();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
