using Microsoft.EntityFrameworkCore;
using Tms.Modules.Contracts.Application.Contracts;
using Tms.Modules.Contracts.Application.Rates;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Contracts.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Contracts.Application.Import;

public sealed record ImportApplyResult(ContractDto Contract, ImportBatchDto Batch, int Imported, int Skipped);

/// <summary>
/// Bulk rate maintenance by spreadsheet: download the template, upload it, see every row checked, correct the bad ones, then apply. Applying never activates anything: the
/// rows go into a draft contract (a new revision when the contract is already approved) that is submitted and approved like any other change.
/// </summary>
internal sealed class ImportHandler(
    ContractsDbContext db, ContractAccess access, ContractLoader loader, ContractRateChecker checker, IVehicleTypeDirectory vehicleTypes, ITransporterDirectory transporters,
    NumberSequence sequence, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<Result<SheetFile>> TemplateAsync(string? format, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var rows = new List<Dictionary<string, object?>>();
        void Add(params (string Column, object? Value)[] cells)
        {
            var row = RateSheet.Columns.ToDictionary(c => c, _ => (object?)null);
            foreach (var (column, value) in cells)
            {
                row[column] = value;
            }

            rows.Add(row);
        }

        Add(("Contract Number", "CN-00001"), ("Service Type", "FTL"), ("Origin", "Mumbai, Maharashtra"), ("Destination", "Pune, Maharashtra"), ("Vehicle Type", "32 FT"), ("Weight From", 10000), ("Weight To", 15000),
            ("Distance From", 150), ("Distance To", 200), ("Rate", 38000), ("Rate Type", "FIXED"), ("DPH Rule", "DPH"), ("Priority", 100), ("Effective From", "2026-04-01"), ("Effective To", "2027-03-31"));
        Add(("Contract Number", "CN-00001"), ("Service Type", "PTL"), ("Origin", "Maharashtra"), ("Destination", "Gujarat"), ("Weight From", 0), ("Weight To", 500), ("Rate", 10), ("Rate Type", "PER_KG"),
            ("Minimum Charge", 1500), ("Priority", 100), ("Effective From", "2026-04-01"), ("Effective To", "2027-03-31"));
        Add(("Contract Number", "CN-00001"), ("Service Type", "PTL"), ("Origin", "Maharashtra"), ("Destination", "Gujarat"), ("Weight From", 500), ("Weight To", 1000), ("Rate", 9), ("Rate Type", "PER_KG"),
            ("Priority", 100), ("Effective From", "2026-04-01"), ("Effective To", "2027-03-31"));
        return await Sheets.WriteAsync(format ?? "xlsx", "freight-rate-template", rows);
    }

    public async Task<Result<ImportBatchDto>> UploadAsync(Stream file, string fileName, ImportMode mode, CancellationToken cancellationToken)
    {
        if (!access.CanManage || currentUser.TenantId is not { } tenant)
        {
            return ContractAccess.Forbidden;
        }

        if (file.Length is 0 or > Sheets.MaxBytes)
        {
            return Error.Validation("rate_import.file_size", "Upload a spreadsheet of up to 6 MB.");
        }

        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows;
        try
        {
            rows = await Sheets.ReadAsync(file, cancellationToken);
        }
        catch (InvalidDataException ex) when (ex.Message.StartsWith("A rate sheet may", StringComparison.Ordinal))
        {
            return Error.Validation("rate_import.too_large", ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Error.Validation("rate_import.unreadable", "The file could not be read. Use the template: an .xlsx or .csv file with its header row.");
        }

        var number = await sequence.NextAsync(tenant, "import", cancellationToken);
        var created = RateImportBatch.Create(tenant, $"IMP-{number:D5}", string.IsNullOrWhiteSpace(fileName) ? "rates.xlsx" : Path.GetFileName(fileName), mode, rows);
        if (created.IsFailure)
        {
            return created.Error;
        }

        db.ImportBatches.Add(created.Value);
        await JudgeAsync(created.Value, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(created.Value, includeRows: true);
    }

    public async Task<Result<IReadOnlyList<ImportBatchDto>>> ListAsync(CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var batches = await db.ImportBatches.AsNoTracking().OrderByDescending(b => b.CreatedAt).Take(100).ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<ImportBatchDto>>(batches.Select(b => ToDto(b, includeRows: false)).ToList());
    }

    public async Task<Result<ImportBatchDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var batch = await db.ImportBatches.AsNoTracking().Include(b => b.Rows).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        return batch is null ? Error.NotFound("rate_import.not_found", "Import not found.") : ToDto(batch, includeRows: true);
    }

    public async Task<Result<ImportBatchDto>> CorrectRowAsync(Guid id, int rowNumber, CorrectImportRowRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManage)
        {
            return ContractAccess.Forbidden;
        }

        var batch = await db.ImportBatches.Include(b => b.Rows).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (batch is null)
        {
            return Error.NotFound("rate_import.not_found", "Import not found.");
        }

        var values = request.Values.ToDictionary(kv => Sheets.Normalise(kv.Key), kv => string.IsNullOrWhiteSpace(kv.Value) ? null : kv.Value.Trim(), StringComparer.OrdinalIgnoreCase);
        var corrected = batch.CorrectRow(rowNumber, values);
        if (corrected.IsFailure)
        {
            return corrected.Error;
        }

        // One row can change what is wrong with another (a duplicate, an overlap), so the whole sheet is checked again.
        await JudgeAsync(batch, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(batch, includeRows: true);
    }

    public async Task<Result<ImportBatchDto>> DiscardAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!access.CanManage)
        {
            return ContractAccess.Forbidden;
        }

        var batch = await db.ImportBatches.Include(b => b.Rows).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (batch is null)
        {
            return Error.NotFound("rate_import.not_found", "Import not found.");
        }

        var discarded = batch.Discard();
        if (discarded.IsFailure)
        {
            return discarded.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToDto(batch, includeRows: false);
    }

    public async Task<Result<ImportApplyResult>> ApplyAsync(Guid id, ApplyImportRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManage)
        {
            return ContractAccess.Forbidden;
        }

        var batch = await db.ImportBatches.Include(b => b.Rows).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (batch is null)
        {
            return Error.NotFound("rate_import.not_found", "Import not found.");
        }

        if (batch.Status != ImportStatus.Previewed)
        {
            return Error.Conflict("rate_import.closed", "This import is finished.");
        }

        if (batch.ContractId is not { } contractId)
        {
            return Error.Validation("rate_import.no_contract", "The sheet does not name a contract that exists, so there is nowhere to put its rates.");
        }

        var rows = batch.Rows.OrderBy(r => r.RowNumber).ToList();
        if (rows.Any(r => r.Status == ImportRowStatus.Error) && !request.SkipInvalidRows)
        {
            return Error.Validation("rate_import.has_errors", $"{batch.ErrorRows} row(s) have errors. Correct them, or choose to skip the invalid rows.") with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["rows"] = [.. rows.Where(r => r.Status == ImportRowStatus.Error).Take(20).Select(r => $"Row {r.RowNumber}: {r.Issues.FirstOrDefault(i => i.Severity == IssueSeverity.Error)?.Message}")] },
            };
        }

        var lookups = await LookupsAsync(cancellationToken);
        var specs = rows.Where(r => r.Status != ImportRowStatus.Error).Select(r => RateSheet.Parse(r.Values, r.RowNumber, lookups).Spec).Where(s => s is not null).Select(s => s!).ToList();
        if (specs.Count == 0)
        {
            return Error.Validation("rate_import.nothing", "There are no valid rows to import.");
        }

        var found = await loader.FindAsync(contractId, write: true, withRates: true, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var target = found.Value;
        foreach (var navigation in new[] { "DphRules", "Accessorials", "Capacities", "Slas" })
        {
            await db.Entry(target).Collection(navigation).LoadAsync(cancellationToken);
        }

        if (target.Status is not (ContractStatus.Draft or ContractStatus.Rejected))
        {
            // An approved contract is never edited: the sheet goes into the revision being prepared, or a new one.
            var open = await db.Contracts.FirstOrDefaultAsync(c => c.RevisionOfId == target.Id && (c.Status == ContractStatus.Draft || c.Status == ContractStatus.Rejected), cancellationToken);
            if (open is not null)
            {
                await db.Entry(open).Collection(c => c.RateCards).LoadAsync(cancellationToken);
                await db.Entry(open).Collection(c => c.DphRules).LoadAsync(cancellationToken);
                target = open;
            }
            else
            {
                var today = clock.TodayInIndia();
                var from = request.EffectiveFrom ?? today.AddDays(1);
                var to = request.EffectiveTo ?? (target.EffectiveTo > from ? target.EffectiveTo : from.AddYears(1));
                var revision = target.CreateRevision(from, to, currentUser.UserId, RevisionKind.Amendment);
                if (revision.IsFailure)
                {
                    return revision.Error;
                }

                db.Contracts.Add(revision.Value);
                target = revision.Value;
            }
        }

        var previous = target.RevisionOfId is { } prev ? await db.RateCards.AsNoTracking().Where(r => r.ContractId == prev).ToDictionaryAsync(r => r.Code, cancellationToken) : null;
        var combined = batch.Mode == ImportMode.Replace ? specs : [.. target.RateCards.Select(c => c.ToSpec()), .. specs];
        var replaced = target.ReplaceRates(combined, previous);
        if (replaced.IsFailure)
        {
            return replaced.Error;
        }

        batch.MarkApplied(target.Id, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return new ImportApplyResult(await loader.ToDtoAsync(target, cancellationToken), ToDto(batch, includeRows: false), specs.Count, rows.Count - specs.Count);
    }

    public async Task<Result<SheetFile>> ExportAsync(ListRatesQuery query, string? format, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var today = clock.TodayInIndia();
        var rows = db.RateCards.AsNoTracking().Join(db.Contracts.AsNoTracking(), r => r.ContractId, c => c.Id, (r, c) => new { Card = r, Contract = c });
        if (query.ContractId is { } cid)
        {
            rows = rows.Where(x => x.Contract.Id == cid);
        }

        if (query.TransporterId is { } t)
        {
            rows = rows.Where(x => x.Contract.TransporterId == t);
        }

        if (query.InForceOnly == true)
        {
            rows = rows.Where(x => x.Contract.Status == ContractStatus.Active && x.Contract.EffectiveFrom <= today && x.Contract.EffectiveTo >= today);
        }

        var list = await rows.OrderBy(x => x.Contract.Number).ThenBy(x => x.Contract.Revision).ThenBy(x => x.Card.Code).Take(50_000).ToListAsync(cancellationToken);
        var names = await transporters.GetAsync(list.Select(x => x.Contract.TransporterId), cancellationToken);
        var types = await vehicleTypes.GetAsync(list.Where(x => x.Card.VehicleTypeId.HasValue).Select(x => x.Card.VehicleTypeId!.Value), cancellationToken);
        var output = list.SelectMany(x => RateSheet.ToRows(x.Contract, x.Card, names.TryGetValue(x.Contract.TransporterId, out var n) ? n.LegalName : "Unknown", x.Card.VehicleTypeId is { } v && types.TryGetValue(v, out var vt) ? vt.Name : null)).ToList();
        if (output.Count == 0)
        {
            output.Add(RateSheet.Columns.ToDictionary(c => c, _ => (object?)null));
        }

        return await Sheets.WriteAsync(format ?? "xlsx", $"freight-rates-{today:yyyyMMdd}", output);
    }

    // ---- checking a sheet

    private async Task<RateSheet.Lookups> LookupsAsync(CancellationToken cancellationToken) =>
        new(await vehicleTypes.ListActiveAsync(cancellationToken), (await db.Zones.AsNoTracking().Select(z => z.Code).ToListAsync(cancellationToken)).ToHashSet());

    private async Task JudgeAsync(RateImportBatch batch, CancellationToken cancellationToken)
    {
        var lookups = await LookupsAsync(cancellationToken);
        var rows = batch.Rows.OrderBy(r => r.RowNumber).ToList();
        var parsed = rows.Select(r => (Row: r, Parsed: RateSheet.Parse(r.Values, r.RowNumber, lookups))).ToList();
        var issues = parsed.ToDictionary(p => p.Row.RowNumber, p => p.Parsed.Issues);

        var number = parsed.Select(p => p.Parsed.ContractNumber).FirstOrDefault(n => n is not null);
        var revision = parsed.Select(p => p.Parsed.ContractRevision).FirstOrDefault(r => r is not null);
        Contract? target = null;
        if (number is null)
        {
            foreach (var p in parsed)
            {
                issues[p.Row.RowNumber].Add(new RateIssue(IssueSeverity.Error, p.Row.RowNumber, "Contract Number", "ROW_CONTRACT_MISSING", "Contract Number is required."));
            }
        }
        else
        {
            var candidates = db.Contracts.AsNoTracking().Include(c => c.RateCards).Include(c => c.DphRules).Where(c => c.Number == number);
            target = revision is { } rev ? await candidates.FirstOrDefaultAsync(c => c.Revision == rev, cancellationToken) : (await candidates.ToListAsync(cancellationToken)).MaxBy(c => c.Revision);
            if (target is null)
            {
                foreach (var p in parsed)
                {
                    issues[p.Row.RowNumber].Add(new RateIssue(IssueSeverity.Error, p.Row.RowNumber, "Contract Number", "ROW_CONTRACT_UNKNOWN", $"Contract {number}{(revision is { } r ? $" version {r}" : string.Empty)} was not found."));
                }
            }
            else
            {
                var transporter = (await transporters.GetAsync([target.TransporterId], cancellationToken)).GetValueOrDefault(target.TransporterId)?.LegalName;
                foreach (var p in parsed)
                {
                    var rowNumber = p.Row.RowNumber;
                    if (p.Parsed.ContractNumber is { } n && !string.Equals(n, target.Number, StringComparison.OrdinalIgnoreCase))
                    {
                        issues[rowNumber].Add(new RateIssue(IssueSeverity.Error, rowNumber, "Contract Number", "ROW_CONTRACT_MIXED", $"This sheet is for {target.Number}, but this row names {n}. Use one sheet per contract."));
                    }

                    if (p.Parsed.Transporter is { } named && transporter is not null && !string.Equals(named, transporter, StringComparison.OrdinalIgnoreCase))
                    {
                        issues[rowNumber].Add(new RateIssue(IssueSeverity.Error, rowNumber, "Transporter", "ROW_TRANSPORTER_MISMATCH", $"{target.Number} is with {transporter}, not {named}."));
                    }
                }
            }
        }

        // Rows that are individually sound are then checked against each other and against what is already in force.
        var sound = parsed.Where(p => p.Parsed.Spec is not null && !issues[p.Row.RowNumber].Any(i => i.Severity == IssueSeverity.Error)).ToList();
        var today = clock.TodayInIndia();
        var from = target?.EffectiveFrom ?? today;
        var to = target?.EffectiveTo ?? today.AddYears(1);
        var checkRows = sound.Select((p, i) =>
        {
            var x = p.Parsed.Spec!.Extras ?? RateExtras.None;
            return new RateToCheck(i + 1, p.Parsed.Spec, x.ValidFrom ?? from, x.ValidTo ?? to, "this sheet");
        }).ToList();
        var existing = batch.Mode == ImportMode.Append && target is not null
            ? target.RateCards.Select(c => new RateToCheck(0, c.ToSpec(), c.ValidFrom ?? target.EffectiveFrom, c.ValidTo ?? target.EffectiveTo, target.Reference)).ToList()
            : [];
        var result = await checker.CheckRowsAsync(
            checkRows, target?.TransporterId, target?.Number, from, to, target?.EffectiveServices ?? [ContractType.Ftl, ContractType.Ptl, ContractType.Dedicated],
            target?.DphRules.Select(r => r.Code.ToUpperInvariant()).ToHashSet() ?? [], cancellationToken, existing);
        foreach (var issue in result.Issues)
        {
            if (issue.Row is { } index && index >= 1 && index <= sound.Count)
            {
                var sheetRow = sound[index - 1].Row.RowNumber;
                issues[sheetRow].Add(issue with { Row = sheetRow, Message = issue.Message.Replace($"row {index}", "another row of this sheet", StringComparison.OrdinalIgnoreCase) });
            }
        }

        batch.Judged(issues.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<RateIssue>)kv.Value), target?.Id, target?.Number);
    }

    private static ImportBatchDto ToDto(RateImportBatch b, bool includeRows) =>
        new(b.Id, b.Reference, b.FileName, b.Mode, b.Status, b.ContractNumber, b.ContractId, b.AppliedContractId, b.RowCount, b.ErrorRows, b.WarningRows, b.CreatedAt, b.AppliedAt,
            includeRows
                ? b.Rows.OrderBy(r => r.RowNumber).Select(r => new ImportRowDto(r.RowNumber, r.Values, r.Status.ToString(), r.Issues.Select(RateHandler.ToDto).ToList())).ToList()
                : null);
}
