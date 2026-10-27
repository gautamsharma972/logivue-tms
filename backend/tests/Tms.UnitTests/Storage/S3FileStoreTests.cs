using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Tms.BuildingBlocks.Web.Storage;

namespace Tms.UnitTests.Storage;

public class S3FileStoreTests
{
    private readonly IAmazonS3 _s3 = Substitute.For<IAmazonS3>();

    private S3FileStore Store(string? prefix = null) => new(_s3, Options.Create(new StorageOptions
    {
        Provider = "S3",
        S3 = new S3StorageOptions { BucketName = "bucket", Region = "ap-south-1", KeyPrefix = prefix },
    }));

    [Fact]
    public async Task Save_puts_an_encrypted_object_under_the_prefixed_key()
    {
        PutObjectRequest? sent = null;
        _s3.PutObjectAsync(Arg.Do<PutObjectRequest>(r => sent = r), Arg.Any<CancellationToken>()).Returns(new PutObjectResponse());

        await Store("dev/").SaveAsync("ops/lrs/2026/10/a.png", new MemoryStream([1, 2, 3]));

        sent.ShouldNotBeNull();
        sent.BucketName.ShouldBe("bucket");
        sent.Key.ShouldBe("dev/ops/lrs/2026/10/a.png");
        sent.ServerSideEncryptionMethod.ShouldBe(ServerSideEncryptionMethod.AES256);
    }

    [Fact]
    public async Task Open_returns_null_when_the_object_does_not_exist()
    {
        _s3.GetObjectAsync("bucket", "missing.png", Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("not found") { StatusCode = HttpStatusCode.NotFound });

        (await Store().OpenReadAsync("missing.png")).ShouldBeNull();
    }

    [Fact]
    public async Task Open_streams_the_object_content()
    {
        _s3.GetObjectAsync("bucket", "a.png", Arg.Any<CancellationToken>())
            .Returns(new GetObjectResponse { ResponseStream = new MemoryStream([9, 8, 7]) });

        await using var stream = (await Store().OpenReadAsync("a.png"))!;
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);

        copy.ToArray().ShouldBe([9, 8, 7]);
    }

    [Fact]
    public async Task Delete_removes_the_prefixed_object()
    {
        await Store("dev").DeleteAsync("a.png");

        await _s3.Received(1).DeleteObjectAsync("bucket", "dev/a.png", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("../secret.png")]
    [InlineData("/abs.png")]
    [InlineData("a\\b.png")]
    [InlineData("a//b.png")]
    [InlineData("")]
    public async Task Unsafe_keys_are_refused(string key)
    {
        await Should.ThrowAsync<ArgumentException>(() => Store().DeleteAsync(key));
        await Should.ThrowAsync<ArgumentException>(() => Store().OpenReadAsync(key));
    }
}
