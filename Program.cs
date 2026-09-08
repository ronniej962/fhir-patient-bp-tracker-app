using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

// Configure named HttpClient for the upstream FHIR server
var fhirBaseUrl = builder.Configuration["FhirSettings:ServerUrl"]?.TrimEnd('/') 
    ?? throw new InvalidOperationException("FhirSettings:ServerUrl missing.");
var fhirBearerToken = builder.Configuration["FhirSettings:BearerToken"] ?? string.Empty;

builder.Services.AddHttpClient("UpstreamFhirClient", client =>
{
    client.BaseAddress = new Uri(fhirBaseUrl + "/");
    client.DefaultRequestHeaders.Accept.Clear();
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/fhir+json"));
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

    if (!string.IsNullOrWhiteSpace(fhirBearerToken))
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fhirBearerToken);
    }
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// -------------------------------------------------------------
// FHIR PROXY ENDPOINT
// Forwards /api/fhir/{path} -> {UpstreamServerUrl}/{path}
// -------------------------------------------------------------
app.Map("/api/fhir/{**catchAll}", async (HttpContext context, IHttpClientFactory clientFactory, string catchAll) =>
{
    var httpClient = clientFactory.CreateClient("UpstreamFhirClient");

    var relativeUrl = catchAll + context.Request.QueryString.Value;

    using var proxyRequest = new HttpRequestMessage(new HttpMethod(context.Request.Method), relativeUrl);

    if (HttpMethods.IsPost(context.Request.Method) || 
        HttpMethods.IsPut(context.Request.Method) || 
        HttpMethods.IsPatch(context.Request.Method))
    {
        proxyRequest.Content = new StreamContent(context.Request.Body);
        if (context.Request.ContentType != null)
        {
            proxyRequest.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.ContentType);
        }
    }

    try
    {
        using var upstreamResponse = await httpClient.SendAsync(
            proxyRequest, 
            HttpCompletionOption.ResponseHeadersRead, 
            context.RequestAborted);

        context.Response.StatusCode = (int)upstreamResponse.StatusCode;
        if (upstreamResponse.Content.Headers.ContentType != null)
        {
            context.Response.ContentType = upstreamResponse.Content.Headers.ContentType.ToString();
        }

        await upstreamResponse.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }
    catch (Exception ex)
    {
        context.Response.StatusCode = StatusCodes.Status502BadGateway;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = "Proxy forwarding failed", details = ex.Message });
    }
});

app.MapRazorPages();
app.Run();