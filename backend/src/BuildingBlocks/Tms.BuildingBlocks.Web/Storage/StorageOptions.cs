namespace Tms.BuildingBlocks.Web.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Folder for uploaded files. A relative path is resolved against the application content root.</summary>
    public string RootPath { get; init; } = "storage";
}
