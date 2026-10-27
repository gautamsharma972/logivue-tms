namespace Tms.BuildingBlocks.Web.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary><c>Local</c> (files under <see cref="RootPath"/>) or <c>S3</c> (see <see cref="S3"/>).</summary>
    public string Provider { get; init; } = "Local";

    /// <summary>Folder for uploaded files. A relative path is resolved against the application content root.</summary>
    public string RootPath { get; init; } = "storage";

    public S3StorageOptions S3 { get; init; } = new();

    public bool UsesS3 => Provider.Equals("S3", StringComparison.OrdinalIgnoreCase);
}

public sealed class S3StorageOptions
{
    public string BucketName { get; init; } = "";

    /// <summary>AWS region system name, e.g. <c>ap-south-1</c>.</summary>
    public string Region { get; init; } = "";

    /// <summary>Named profile in <c>~/.aws/config</c> (e.g. an SSO profile) for development. Leave empty in production to use the instance/task role.</summary>
    public string? Profile { get; init; }

    /// <summary>Optional folder inside the bucket (e.g. <c>dev/</c>). Applied by the store, never saved in the database.</summary>
    public string? KeyPrefix { get; init; }

    /// <summary>Custom endpoint for S3-compatible servers (MinIO, LocalStack). Empty for AWS.</summary>
    public string? ServiceUrl { get; init; }
}
