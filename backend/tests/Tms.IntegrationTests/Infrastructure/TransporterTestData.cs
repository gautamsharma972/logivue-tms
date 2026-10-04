using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Tms.Modules.Approvals.Application;
using Tms.Modules.Transporters.Application;
using Tms.Modules.Transporters.Domain;
using Tms.SharedKernel.India;

namespace Tms.IntegrationTests.Infrastructure;

internal static class TransporterTestData
{
    private static readonly Random Rng = new();

    /// <summary>A PAN and the GSTIN derived from it, both valid (including the GSTIN check character).</summary>
    public static (string Pan, string Gstin) NewIdentity(string stateCode = "27")
    {
        string Letters(int n) => new(Enumerable.Range(0, n).Select(_ => (char)('A' + Rng.Next(26))).ToArray());
        var pan = $"{Letters(5)}{Rng.Next(1000, 9999)}{Letters(1)}";
        var stem = $"{stateCode}{pan}1Z";
        var check = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".First(c => IndianIdentifiers.IsValidGstin(stem + c));
        return (pan, stem + check);
    }

    public static SaveTransporterRequest NewRequest(string? pan = null, string? gstin = null, string name = "Shree Roadlines") 
    {
        var identity = NewIdentity();
        return new SaveTransporterRequest(
            $"{name} {Guid.NewGuid().ToString("N")[..6]} Pvt Ltd", "Shree", pan ?? identity.Pan, gstin ?? (pan is null ? identity.Gstin : null),
            "R. Patil", "98765 43210", "ops@shree.example", "Plot 4, MIDC", null, "Pune", "Maharashtra", "411019", ["Ftl", "Ptl"], null);
    }

    public static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF\n");

    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    public static async Task<TransporterDto> CreateAsync(HttpClient client, SaveTransporterRequest? request = null)
    {
        var response = await client.PostJsonAsync("/api/v1/transporters", request ?? NewRequest());
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<TransporterDto>();
    }

    public static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client, Guid transporterId, OwnerKind ownerKind, Guid ownerId, DocumentKind kind,
        byte[]? content = null, string fileName = "scan.pdf", DateOnly? expiresOn = null, string? number = null)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(ownerKind.ToString()), "ownerKind" },
            { new StringContent(ownerId.ToString()), "ownerId" },
            { new StringContent(kind.ToString()), "kind" },
        };
        if (number is not null)
        {
            form.Add(new StringContent(number), "number");
        }

        if (expiresOn is { } expires)
        {
            form.Add(new StringContent(expires.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)), "expiresOn");
        }

        var file = new ByteArrayContent(content ?? Pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf"); // deliberately not trusted by the server
        form.Add(file, "file", fileName);
        return await client.PostAsync($"/api/v1/transporters/{transporterId}/documents", form);
    }

    public static async Task UploadOkAsync(HttpClient client, Guid transporterId, OwnerKind kind, Guid ownerId, DocumentKind doc, DateOnly? expiresOn = null)
    {
        var response = await UploadAsync(client, transporterId, kind, ownerId, doc, expiresOn: expiresOn);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    public static async Task SetBankAsync(HttpClient client, TransporterDto transporter)
    {
        var response = await client.PutJsonAsync($"/api/v1/transporters/{transporter.Id}/bank",
            new SaveBankRequest("Shree Roadlines", "123456789012", "HDFC0001234", "HDFC Bank", transporter.Version));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>A transporter with bank details and every required document, ready to submit.</summary>
    public static async Task<TransporterDto> CreateReadyAsync(HttpClient admin, SaveTransporterRequest? request = null)
    {
        var transporter = await CreateAsync(admin, request);
        await SetBankAsync(admin, transporter);
        await UploadOkAsync(admin, transporter.Id, OwnerKind.Transporter, transporter.Id, DocumentKind.PanCard);
        await UploadOkAsync(admin, transporter.Id, OwnerKind.Transporter, transporter.Id, DocumentKind.CancelledCheque);
        if (transporter.Gstin is not null)
        {
            await UploadOkAsync(admin, transporter.Id, OwnerKind.Transporter, transporter.Id, DocumentKind.GstCertificate);
        }

        return await GetAsync(admin, transporter.Id);
    }

    public static async Task<TransporterDto> GetAsync(HttpClient client, Guid id) =>
        await (await client.GetAsync($"/api/v1/transporters/{id}")).ReadAsync<TransporterDto>();

    public static async Task SetOnboardingPolicyAsync(HttpClient admin, params PolicyStepDto[] steps)
    {
        var all = await (await admin.GetAsync("/api/v1/approvals/policies")).ReadAsync<List<PolicyDto>>();
        var version = all.Single(p => p.DocumentType == "transporter_onboarding").Version;
        var response = await admin.PutJsonAsync("/api/v1/approvals/policies/transporter_onboarding", new SavePolicyRequest(true, steps, version));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public static Task NeedsOneApproverAsync(HttpClient admin) =>
        SetOnboardingPolicyAsync(admin, new PolicyStepDto("Procurement head", TransporterPermissions.Approve, null));

    public static async Task<HttpResponseMessage> ApproveAsync(HttpClient approver, Guid requestId, string? comment = null) =>
        await approver.PostJsonAsync($"/api/v1/approvals/requests/{requestId}/approve", new DecisionRequest(comment));
}
