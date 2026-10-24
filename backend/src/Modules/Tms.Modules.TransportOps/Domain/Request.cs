using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.TransportOps.Domain;

public sealed class RequestLine : Entity, ITenantScoped
{
    private RequestLine()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid RequestId { get; private set; }

    public string? MaterialCode { get; private set; }

    public string MaterialName { get; private set; } = null!;

    public decimal Quantity { get; private set; }

    public string Unit { get; private set; } = "PKG";

    public decimal WeightKg { get; private set; }

    public decimal? VolumeCbm { get; private set; }

    public string? Dimensions { get; private set; }

    public int Packages { get; private set; }

    public string? Remarks { get; private set; }

    public static RequestLine Create(Guid tenantId, Guid requestId, string? code, string name, decimal quantity, string? unit, decimal weightKg, decimal? volumeCbm, string? dimensions, int packages, string? remarks) => new()
    {
        TenantId = tenantId, RequestId = requestId, MaterialCode = Customer.Clean(code), MaterialName = name.Trim(), Quantity = quantity, Unit = Customer.Clean(unit)?.ToUpperInvariant() ?? "PKG",
        WeightKg = weightKg, VolumeCbm = volumeCbm, Dimensions = Customer.Clean(dimensions), Packages = packages, Remarks = Customer.Clean(remarks),
    };
}

/// <summary>An attachment kept against a request or LR (the original email, a PO, a delivery note). The file itself is in the file store.</summary>
public sealed class StoredFile : Entity, ITenantScoped
{
    private StoredFile()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid OwnerId { get; private set; }

    public string FileKey { get; private set; } = null!;

    public string FileName { get; private set; } = null!;

    public string ContentType { get; private set; } = null!;

    public long SizeBytes { get; private set; }

    public string Sha256 { get; private set; } = null!;

    public DateTimeOffset UploadedAt { get; private set; }

    public Guid? UploadedBy { get; private set; }

    public static StoredFile Create(Guid tenantId, Guid ownerId, string key, string name, string contentType, long size, string sha256, DateTimeOffset at, Guid? by) =>
        new() { TenantId = tenantId, OwnerId = ownerId, FileKey = key, FileName = name, ContentType = contentType, SizeBytes = size, Sha256 = sha256, UploadedAt = at, UploadedBy = by };
}

/// <summary>
/// A customer's request to move goods from a CFA to a distributor, registered by hand from an email. It cannot become dispatch-ready until it has been checked against the
/// customer's required details.
/// </summary>
public sealed class DeliveryRequest : AggregateRoot, ITenantScoped
{
    private DeliveryRequest()
    {
    }

    public Guid TenantId { get; private set; }

    public string Number { get; private set; } = null!;

    public Guid CustomerId { get; private set; }

    public string? CustomerReference { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>Subject or message id of the original email.</summary>
    public string? EmailReference { get; private set; }

    public Guid? CfaLocationId { get; private set; }

    public Guid? DestinationLocationId { get; private set; }

    public DateOnly? RequestedDate { get; private set; }

    public string Priority { get; private set; } = "Normal";

    public string? VehicleTypeRequested { get; private set; }

    public string? Instructions { get; private set; }

    public string? Remarks { get; private set; }

    public RequestStatus Status { get; private set; } = RequestStatus.Received;

    /// <summary>What is still missing, from the last check.</summary>
    public List<string> Issues { get; private set; } = [];

    public Guid? LrId { get; private set; }

    public List<RequestLine> Lines { get; private set; } = [];

    public bool IsOpen => Status is not (RequestStatus.Converted or RequestStatus.Cancelled);

    public static DeliveryRequest Create(Guid tenantId, string number, Guid customerId, DateTimeOffset receivedAt) =>
        new() { TenantId = tenantId, Number = number, CustomerId = customerId, ReceivedAt = receivedAt };

    public Result Edit(Guid customerId, string? customerReference, DateTimeOffset receivedAt, string? emailReference, Guid? cfa, Guid? destination, DateOnly? requestedDate, string priority,
        string? vehicleType, string? instructions, string? remarks)
    {
        if (!IsOpen)
        {
            return Error.Conflict("ops.request_closed", "A converted or cancelled request cannot be changed.");
        }

        CustomerId = customerId;
        CustomerReference = Customer.Clean(customerReference);
        ReceivedAt = receivedAt;
        EmailReference = Customer.Clean(emailReference);
        CfaLocationId = cfa;
        DestinationLocationId = destination;
        RequestedDate = requestedDate;
        Priority = Customer.Clean(priority) ?? "Normal";
        VehicleTypeRequested = Customer.Clean(vehicleType);
        Instructions = Customer.Clean(instructions);
        Remarks = Customer.Clean(remarks);
        // Anything changed after it was checked has to be checked again.
        if (Status is RequestStatus.Validated or RequestStatus.ReadyForDispatch or RequestStatus.Incomplete)
        {
            Status = RequestStatus.UnderReview;
            Issues = [];
        }

        return Result.Success();
    }

    public void SetLines(IEnumerable<RequestLine> lines)
    {
        Lines.Clear();
        Lines.AddRange(lines);
        if (Status is RequestStatus.Validated or RequestStatus.ReadyForDispatch or RequestStatus.Incomplete)
        {
            Status = RequestStatus.UnderReview;
            Issues = [];
        }
    }

    public Result StartReview()
    {
        if (Status != RequestStatus.Received)
        {
            return Error.Conflict("ops.request_state", "Only a newly received request can be taken into review.");
        }

        Status = RequestStatus.UnderReview;
        return Result.Success();
    }

    /// <summary>Checks the request is complete. Complete → Validated; otherwise Incomplete with the list of what is missing.</summary>
    public Result<IReadOnlyList<string>> Validate(IReadOnlyCollection<string> customerRequiredFields)
    {
        if (!IsOpen)
        {
            return Error.Conflict("ops.request_closed", "A converted or cancelled request cannot be checked.");
        }

        var issues = new List<string>();
        if (CfaLocationId is null)
        {
            issues.Add("Source CFA is missing.");
        }

        if (DestinationLocationId is null)
        {
            issues.Add("Destination distributor is missing.");
        }

        if (RequestedDate is null)
        {
            issues.Add("Requested dispatch/delivery date is missing.");
        }

        if (Lines.Count == 0)
        {
            issues.Add("At least one material line is required.");
        }

        if (Lines.Any(l => l.Quantity <= 0))
        {
            issues.Add("Every material line needs a quantity above zero.");
        }

        if (Lines.Any(l => l.WeightKg <= 0))
        {
            issues.Add("Every material line needs an actual weight above zero.");
        }

        foreach (var field in customerRequiredFields)
        {
            switch (field)
            {
                case "CustomerReference" when string.IsNullOrWhiteSpace(CustomerReference):
                    issues.Add("The customer's PO/reference is required for this customer.");
                    break;
                case "Dimensions" when Lines.Any(l => string.IsNullOrWhiteSpace(l.Dimensions)):
                    issues.Add("Dimensions are required on every line for this customer.");
                    break;
                case "Volume" when Lines.Any(l => l.VolumeCbm is null):
                    issues.Add("Volume is required on every line for this customer.");
                    break;
            }
        }

        Issues = issues;
        Status = issues.Count == 0 ? RequestStatus.Validated : RequestStatus.Incomplete;
        return issues;
    }

    /// <summary>Adds checks that need data from another table (for example a destination without coordinates) to the result of the last <see cref="Validate"/>.</summary>
    public void AddIssues(IReadOnlyList<string> issues)
    {
        Issues = [.. issues];
        if (Issues.Count > 0 && Status == RequestStatus.Validated)
        {
            Status = RequestStatus.Incomplete;
        }
    }

    public Result MarkReady()
    {
        if (Status != RequestStatus.Validated)
        {
            return Error.Conflict("ops.request_not_validated", "Only a validated request can be made ready for dispatch.");
        }

        Status = RequestStatus.ReadyForDispatch;
        return Result.Success();
    }

    public Result Convert(Guid lrId)
    {
        if (Status != RequestStatus.ReadyForDispatch)
        {
            return Error.Conflict("ops.request_not_ready", "Only a request that is ready for dispatch can become an LR.");
        }

        Status = RequestStatus.Converted;
        LrId = lrId;
        return Result.Success();
    }

    public Result Cancel()
    {
        if (!IsOpen)
        {
            return Error.Conflict("ops.request_closed", "A converted or cancelled request cannot be cancelled.");
        }

        Status = RequestStatus.Cancelled;
        return Result.Success();
    }
}
