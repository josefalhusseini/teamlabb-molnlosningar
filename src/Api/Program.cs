using System.Text.Json;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var storageAccountName = Environment.GetEnvironmentVariable("STORAGE_ACCOUNT_NAME") 
                         ?? builder.Configuration["AzureStorage:AccountName"];

var containerName = Environment.GetEnvironmentVariable("CONTAINER_NAME") 
                    ?? builder.Configuration["AzureStorage:ContainerName"] 
                    ?? "certificates";

var apiKey = Environment.GetEnvironmentVariable("ADMIN_API_KEY") 
             ?? builder.Configuration["AdminApiKey"];

BlobContainerClient? containerClient = null;

if (!string.IsNullOrEmpty(storageAccountName))
{
    var blobUri = new Uri($"https://{storageAccountName}.blob.core.windows.net");
    var blobServiceClient = new BlobServiceClient(blobUri, new DefaultAzureCredential());
    containerClient = blobServiceClient.GetBlobContainerClient(containerName);
}

var inMemoryCertificates = new Dictionary<string, Certificate>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Certify API v1");
    options.RoutePrefix = "swagger";
});

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", timestamp = DateTime.UtcNow }))
    .WithName("GetHealth")
    .WithTags("Health")
    .Produces(StatusCodes.Status200OK);

app.MapPost("/certificates", async (CertificateRequest request, HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(request.RecipientName) || string.IsNullOrWhiteSpace(request.CourseName))
    {
        return Results.BadRequest(new { error = "RecipientName och CourseName måste fyllas i." });
    }

    var id = Guid.NewGuid().ToString();
    var issueDate = request.IssueDate == default ? DateTime.UtcNow : request.IssueDate;
    var host = context.Request.Host.Value;
    var verificationUrl = $"https://{host}/verify/{id}";

    var certificate = new Certificate(id, request.RecipientName, request.CourseName, issueDate, verificationUrl);

    if (containerClient != null)
    {
        await containerClient.CreateIfNotExistsAsync();
        var blobClient = containerClient.GetBlobClient($"{id}.json");
        var json = JsonSerializer.Serialize(certificate);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = "application/json" });
    }
    else
    {
        inMemoryCertificates[id] = certificate;
    }

    return Results.Created($"/certificates/{id}", certificate);
})
.WithName("CreateCertificate")
.WithTags("Certificates")
.Produces<Certificate>(StatusCodes.Status201Created)
.Produces(StatusCodes.Status400BadRequest);

app.MapGet("/certificates/{id}", async (string id) =>
{
    if (containerClient != null)
    {
        var blobClient = containerClient.GetBlobClient($"{id}.json");
        if (!await blobClient.ExistsAsync())
        {
            return Results.NotFound(new { error = "Certifikatet hittades inte." });
        }

        var download = await blobClient.DownloadContentAsync();
        var certificate = JsonSerializer.Deserialize<Certificate>(download.Value.Content.ToString());
        return certificate != null ? Results.Ok(certificate) : Results.NotFound();
    }

    if (inMemoryCertificates.TryGetValue(id, out var cert))
    {
        return Results.Ok(cert);
    }

    return Results.NotFound(new { error = "Certifikatet hittades inte." });
})
.WithName("GetCertificateById")
.WithTags("Certificates")
.Produces<Certificate>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

app.MapGet("/verify/{uuid}", async (string uuid) =>
{
    Certificate? certificate = null;

    if (containerClient != null)
    {
        var blobClient = containerClient.GetBlobClient($"{uuid}.json");
        if (await blobClient.ExistsAsync())
        {
            var download = await blobClient.DownloadContentAsync();
            certificate = JsonSerializer.Deserialize<Certificate>(download.Value.Content.ToString());
        }
    }
    else if (inMemoryCertificates.TryGetValue(uuid, out var cert))
    {
        certificate = cert;
    }

    if (certificate == null)
    {
        return Results.NotFound(new { isValid = false, message = "Certifikatet kunde inte verifieras." });
    }

    var result = new VerificationResult(
        IsValid: true,
        CertificateId: certificate.Id,
        RecipientName: certificate.RecipientName,
        CourseName: certificate.CourseName,
        IssueDate: certificate.IssueDate,
        VerifiedAt: DateTime.UtcNow
    );

    return Results.Ok(result);
})
.WithName("VerifyCertificate")
.WithTags("Verification")
.Produces<VerificationResult>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

app.MapGet("/certificates", async (HttpRequest request) =>
{
    if (string.IsNullOrEmpty(apiKey) || !request.Headers.TryGetValue("X-Api-Key", out var providedKey) || providedKey != apiKey)
    {
        return Results.Unauthorized();
    }

    var certificates = new List<Certificate>();

    if (containerClient != null)
    {
        await containerClient.CreateIfNotExistsAsync();
        await foreach (var blobItem in containerClient.GetBlobsAsync())
        {
            var blobClient = containerClient.GetBlobClient(blobItem.Name);
            var download = await blobClient.DownloadContentAsync();
            var cert = JsonSerializer.Deserialize<Certificate>(download.Value.Content.ToString());
            if (cert != null)
            {
                certificates.Add(cert);
            }
        }
    }
    else
    {
        certificates.AddRange(inMemoryCertificates.Values);
    }

    return Results.Ok(certificates);
})
.WithName("ListCertificates")
.WithTags("Certificates")
.Produces<List<Certificate>>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized);

app.Run();

public record CertificateRequest(string RecipientName, string CourseName, DateTime IssueDate);
public record Certificate(string Id, string RecipientName, string CourseName, DateTime IssueDate, string VerificationUrl);
public record VerificationResult(bool IsValid, string CertificateId, string RecipientName, string CourseName, DateTime IssueDate, DateTime VerifiedAt);
