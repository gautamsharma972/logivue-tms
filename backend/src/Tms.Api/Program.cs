using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Serilog;
using Tms.Api;
using Tms.BuildingBlocks.Web;
using Tms.BuildingBlocks.Web.Http;
using Tms.BuildingBlocks.Web.Security;
using Tms.Modules.Approvals;
using Tms.Modules.Contracts;
using Tms.Modules.Contracts.Infrastructure.Persistence;
using Tms.Modules.Deliveries;
using Tms.Modules.Deliveries.Infrastructure.Persistence;
using Tms.Modules.Shipments;
using Tms.Modules.Shipments.Infrastructure.Persistence;
using Tms.Modules.Approvals.Infrastructure.Persistence;
using Tms.Modules.Platform;
using Tms.Modules.Transporters;
using Tms.Modules.Transporters.Infrastructure.Persistence;
using Tms.Modules.Platform.Infrastructure.Persistence;
using Tms.SharedKernel.Telemetry;

// Container health probe: `dotnet Tms.Api.dll --healthcheck` exits 0 when the app answers /health/live.
if (args.Contains("--healthcheck"))
{
    try
    {
        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var port = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")?.Split(':').LastOrDefault() ?? "8080";
        return (await probe.GetAsync($"http://localhost:{port}/health/live")).IsSuccessStatusCode ? 0 : 1;
    }
    catch (Exception)
    {
        return 1;
    }
}

Log.Logger = new LoggerConfiguration().WriteTo.Console(formatProvider: CultureInfo.InvariantCulture).CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, logger) => logger
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(
            formatProvider: CultureInfo.InvariantCulture,
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"));

    var services = builder.Services;
    services.AddWebBuildingBlocks(builder.Configuration);
    services.AddPlatformModule(builder.Configuration);
    services.AddApprovalsModule(builder.Configuration);
    services.AddTransportersModule(builder.Configuration);
    services.AddContractsModule(builder.Configuration);
    services.AddShipmentsModule(builder.Configuration);
    services.AddDeliveriesModule(builder.Configuration);

    // Traces and metrics are always collected in-process (cheap); they are exported only when an OTLP endpoint is configured
    // (OTEL_EXPORTER_OTLP_ENDPOINT, e.g. an OpenTelemetry Collector, Grafana, Azure Monitor, Datadog).
    var exportTelemetry = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
    services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("tms-api"))
        .WithTracing(tracing =>
        {
            tracing
                .AddAspNetCoreInstrumentation(o => o.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                .AddSource(TmsTelemetry.Name);
            if (exportTelemetry)
            {
                tracing.AddOtlpExporter();
            }
        })
        .WithMetrics(metrics =>
        {
            metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(TmsTelemetry.Name);
            if (exportTelemetry)
            {
                metrics.AddOtlpExporter();
            }
        });

    services.ConfigureHttpJsonOptions(options => {
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        // Polymorphic payloads (rate pricing) carry a "kind" discriminator; browsers may not emit it first.
        options.SerializerOptions.AllowOutOfOrderMetadataProperties = true;
    });
    services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // Only trust proxy headers from the configured proxies; clear the loopback-only defaults when deployed behind a gateway.
        if (builder.Configuration.GetValue<bool>("Proxy:TrustAll"))
        {
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        }
    });

    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    services.AddCors(options => options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithExposedHeaders(CorrelationIdMiddleware.HeaderName, "Content-Disposition"))); // the web app names downloads from this

    services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecurityTransformer>());
    services.AddHealthChecks().AddDbContextCheck<PlatformDbContext>("mysql", tags: ["ready"])
        .AddDbContextCheck<ApprovalsDbContext>("mysql-approvals", tags: ["ready"])
        .AddDbContextCheck<TransportersDbContext>("mysql-transporters", tags: ["ready"])
        .AddDbContextCheck<ContractsDbContext>("mysql-contracts", tags: ["ready"])
        .AddDbContextCheck<ShipmentsDbContext>("mysql-shipments", tags: ["ready"])
        .AddDbContextCheck<DeliveriesDbContext>("mysql-deliveries", tags: ["ready"]);

    var app = builder.Build();

    app.UseForwardedHeaders();
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseSerilogRequestLogging();

    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
        app.UseHttpsRedirection();
    }

    app.UseMiddleware<SecurityHeadersMiddleware>();
    app.UseCors();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseMiddleware<PasswordChangeRequiredMiddleware>();
    app.UseAuthorization();

    app.MapPlatformEndpoints();
    app.MapApprovalsEndpoints();
    app.MapTransportersEndpoints();
    app.MapContractsEndpoints();
    app.MapShipmentsEndpoints();
    app.MapDeliveriesEndpoints();

    if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
    {
        DevEndpoints.Map(app);
    }

    app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
    app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi().AllowAnonymous();
        app.MapScalarApiReference("/scalar").AllowAnonymous();
    }

    await app.Services.InitialisePlatformAsync(app.Configuration);
    await app.Services.InitialiseApprovalsAsync(app.Configuration);
    await app.Services.InitialiseTransportersAsync(app.Configuration);
    await app.Services.InitialiseContractsAsync(app.Configuration);
    await app.Services.InitialiseShipmentsAsync(app.Configuration);
    await app.Services.InitialiseDeliveriesAsync(app.Configuration);

    // Operator commands run against the configured database and exit instead of serving requests.
    if (args.Length > 0 && args[0] == "tenant:create")
    {
        return await TenantCommand.RunAsync(app.Services, args[1..]);
    }

    await app.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Exposes the entry point to the integration-test project.</summary>
public partial class Program;
