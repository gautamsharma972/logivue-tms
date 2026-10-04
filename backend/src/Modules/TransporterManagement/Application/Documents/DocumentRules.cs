namespace LogiVue.Tms.TransporterManagement.Application.Documents;

/// <summary>Upload limits. Kept here so the validator and the API documentation agree.</summary>
public static class DocumentRules
{
    public const long MaxFileSizeBytes = 10 * 1024 * 1024;

    public static readonly string[] AllowedExtensions = [".pdf", ".jpg", ".jpeg", ".png"];

    public static readonly string[] AllowedContentTypes = ["application/pdf", "image/jpeg", "image/png"];
}
