using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Contracts.Domain;

public enum ImportStatus
{
    /// <summary>Read and checked; waiting for someone to look at the preview and decide.</summary>
    Previewed = 1,

    /// <summary>Its valid rows were put into a draft contract revision, which still needs approval.</summary>
    Applied = 2,

    Discarded = 3,
}

public enum ImportMode
{
    /// <summary>The rows are added to the contract's existing rates.</summary>
    Append = 1,

    /// <summary>The rows replace the contract's rates.</summary>
    Replace = 2,
}

public enum ImportRowStatus
{
    Valid = 1,
    Warning = 2,
    Error = 3,
}

/// <summary>One row of an uploaded sheet: what was written, and what is wrong with it.</summary>
[AuditIgnore]
public sealed class RateImportRow : Entity, ITenantScoped
{
    private RateImportRow()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid BatchId { get; private set; }

    /// <summary>The row's line in the sheet (the header is line 1).</summary>
    public int RowNumber { get; private set; }

    public IReadOnlyDictionary<string, string?> Values { get; private set; } = new Dictionary<string, string?>();

    public ImportRowStatus Status { get; private set; }

    public IReadOnlyList<RateIssue> Issues { get; private set; } = [];

    internal static RateImportRow Create(Guid tenantId, Guid batchId, int rowNumber, IReadOnlyDictionary<string, string?> values) =>
        new() { TenantId = tenantId, BatchId = batchId, RowNumber = rowNumber, Values = values };

    internal void Judge(IReadOnlyList<RateIssue> issues)
    {
        Issues = issues;
        Status = issues.Any(i => i.Severity == IssueSeverity.Error) ? ImportRowStatus.Error : issues.Count > 0 ? ImportRowStatus.Warning : ImportRowStatus.Valid;
    }

    internal void Correct(IReadOnlyDictionary<string, string?> values) => Values = values;
}

/// <summary>An uploaded rate sheet and what became of it. Importing never activates anything: valid rows go into a draft revision that is approved like any other.</summary>
public sealed class RateImportBatch : AggregateRoot, ITenantScoped
{
    public const int MaxRows = 5_000;

    private readonly List<RateImportRow> _rows = [];

    private RateImportBatch()
    {
    }

    public Guid TenantId { get; private set; }

    public string Reference { get; private set; } = null!;

    public string FileName { get; private set; } = null!;

    public ImportMode Mode { get; private set; }

    public ImportStatus Status { get; private set; }

    public Guid? ContractId { get; private set; }

    public string? ContractNumber { get; private set; }

    public Guid? AppliedContractId { get; private set; }

    public int RowCount { get; private set; }

    public int ErrorRows { get; private set; }

    public int WarningRows { get; private set; }

    public DateTimeOffset? AppliedAt { get; private set; }

    public IReadOnlyCollection<RateImportRow> Rows => _rows;

    public static Result<RateImportBatch> Create(Guid tenantId, string reference, string fileName, ImportMode mode, IReadOnlyList<IReadOnlyDictionary<string, string?>> rows)
    {
        if (rows.Count == 0)
        {
            return Error.Validation("rate_import.empty", "The sheet has no rates in it.");
        }

        if (rows.Count > MaxRows)
        {
            return Error.Validation("rate_import.too_many", $"A sheet can hold at most {MaxRows} rates. Split it into several.");
        }

        var batch = new RateImportBatch { TenantId = tenantId, Reference = reference, FileName = fileName.Length > 255 ? fileName[..255] : fileName, Mode = mode, Status = ImportStatus.Previewed, RowCount = rows.Count };
        for (var i = 0; i < rows.Count; i++)
        {
            batch._rows.Add(RateImportRow.Create(tenantId, batch.Id, i + 2, rows[i]));
        }

        return batch;
    }

    /// <summary>Records the outcome of checking each row, and which contract the sheet is for.</summary>
    public void Judged(IReadOnlyDictionary<int, IReadOnlyList<RateIssue>> issuesByRow, Guid? contractId, string? contractNumber)
    {
        foreach (var row in _rows)
        {
            row.Judge(issuesByRow.TryGetValue(row.RowNumber, out var issues) ? issues : []);
        }

        ContractId = contractId;
        ContractNumber = contractNumber;
        ErrorRows = _rows.Count(r => r.Status == ImportRowStatus.Error);
        WarningRows = _rows.Count(r => r.Status == ImportRowStatus.Warning);
    }

    public Result CorrectRow(int rowNumber, IReadOnlyDictionary<string, string?> values)
    {
        if (Status != ImportStatus.Previewed)
        {
            return Error.Conflict("rate_import.closed", "This import is finished.");
        }

        var row = _rows.FirstOrDefault(r => r.RowNumber == rowNumber);
        if (row is null)
        {
            return Error.NotFound("rate_import.row_not_found", "There is no such row.");
        }

        row.Correct(values);
        return Result.Success();
    }

    public Result MarkApplied(Guid contractId, DateTimeOffset now)
    {
        if (Status != ImportStatus.Previewed)
        {
            return Error.Conflict("rate_import.closed", "This import is finished.");
        }

        AppliedContractId = contractId;
        AppliedAt = now;
        Status = ImportStatus.Applied;
        return Result.Success();
    }

    public Result Discard()
    {
        if (Status != ImportStatus.Previewed)
        {
            return Error.Conflict("rate_import.closed", "This import is finished.");
        }

        Status = ImportStatus.Discarded;
        return Result.Success();
    }
}
