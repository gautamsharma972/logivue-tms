using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tms.BuildingBlocks.Web.Storage;

internal static class S3Registration
{
    public static IAmazonS3 CreateClient(S3StorageOptions options)
    {
        var config = new AmazonS3Config();
        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            config.ServiceURL = options.ServiceUrl;
            config.ForcePathStyle = true;
        }
        else if (!string.IsNullOrWhiteSpace(options.Region))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }

        // A named profile (SSO in development); otherwise the SDK's default chain (environment, task/instance role).
        if (!string.IsNullOrWhiteSpace(options.Profile))
        {
            var chain = new CredentialProfileStoreChain();
            if (!chain.TryGetAWSCredentials(options.Profile, out var credentials))
            {
                throw new InvalidOperationException($"AWS profile '{options.Profile}' was not found in the shared config file.");
            }

            return new AmazonS3Client(credentials, config);
        }

        return new AmazonS3Client(config);
    }

    /// <summary>Fails the start-up with a clear message when the bucket cannot be reached, instead of on the first upload.</summary>
    public sealed class BucketCheck(IAmazonS3 s3, IOptions<StorageOptions> options, ILogger<BucketCheck> logger) : IHostedLifecycleService
    {
        public async Task StartingAsync(CancellationToken cancellationToken)
        {
            var bucket = options.Value.S3.BucketName;
            try
            {
                await s3.GetBucketLocationAsync(bucket, cancellationToken);
                logger.LogInformation("File storage: S3 bucket {Bucket} is reachable.", bucket);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"S3 bucket '{bucket}' is not reachable ({ex.Message}). For a profile, run 'aws sso login --profile {options.Value.S3.Profile}'.", ex);
            }
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
