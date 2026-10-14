using DATA_EXCHANGE_HUB.Services;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Server.IISIntegration;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile(
        "appsettings.Local.json",
        optional: true,
        reloadOnChange: true);
}

var dataExchangeOptions = builder.Configuration
    .GetSection(DataExchangeOptions.SectionName)
    .Get<DataExchangeOptions>() ?? new DataExchangeOptions();
var fileUploadOptions = builder.Configuration
    .GetSection(FileUploadOptions.SectionName)
    .Get<FileUploadOptions>() ?? new FileUploadOptions();

// Add services to the container.
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services
    .AddRazorPages()
    .AddViewLocalization();
builder.Services.AddAuthentication(IISDefaults.AuthenticationScheme);
builder.Services.Configure<DataExchangeOptions>(
    builder.Configuration.GetSection(DataExchangeOptions.SectionName));
builder.Services.Configure<OraculOptions>(
    builder.Configuration.GetSection(OraculOptions.SectionName));
builder.Services.Configure<KairosOptions>(
    builder.Configuration.GetSection(KairosOptions.SectionName));
builder.Services.Configure<AtlasOptions>(
    builder.Configuration.GetSection(AtlasOptions.SectionName));
builder.Services.Configure<FileUploadOptions>(
    builder.Configuration.GetSection(FileUploadOptions.SectionName));
builder.Services.Configure<ClientInfoOptions>(
    builder.Configuration.GetSection(ClientInfoOptions.SectionName));
builder.Services.Configure<HydraOptions>(
    builder.Configuration.GetSection(HydraOptions.SectionName));
builder.Services.Configure<DikeOptions>(
    builder.Configuration.GetSection(DikeOptions.SectionName));
builder.Services.Configure<AreteOptions>(
    builder.Configuration.GetSection(AreteOptions.SectionName));
builder.Services.Configure<ApisOptions>(
    builder.Configuration.GetSection(ApisOptions.SectionName));
builder.Services.Configure<ArgusOptions>(
    builder.Configuration.GetSection(ArgusOptions.SectionName));
builder.Services.Configure<SendMailOptions>(
    builder.Configuration.GetSection(SendMailOptions.SectionName));
builder.Services.Configure<IrisOptions>(
    builder.Configuration.GetSection(IrisOptions.SectionName));
builder.Services.Configure<SendSmsOptions>(
    builder.Configuration.GetSection(SendSmsOptions.SectionName));
builder.Services.Configure<SeshatReminderOptions>(
    builder.Configuration.GetSection(SeshatReminderOptions.SectionName));
builder.Services.Configure<WorkHubOptions>(
    builder.Configuration.GetSection(WorkHubOptions.SectionName));
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = Math.Max(
        dataExchangeOptions.MaxFileSizeBytes,
        fileUploadOptions.MaxFileSizeBytes);
});
builder.Services.Configure<List<HubApplication>>(
    builder.Configuration.GetSection("HubApplications"));
builder.Services.AddSingleton<IHubApplicationCatalog, HubApplicationCatalog>();
builder.Services.AddSingleton<IHubApplicationAcronymService, HubApplicationAcronymService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IUserNameResolver, WindowsUserNameResolver>();
builder.Services.AddSingleton<IWindowsCredentialValidator, WindowsCredentialValidator>();
builder.Services.AddScoped<IExchangeAccessService, ExchangeAccessService>();
builder.Services.AddScoped<IDataExchangeService, DataExchangeService>();
builder.Services.AddScoped<IDataExchangeTableService, DataExchangeTableService>();
builder.Services.AddScoped<IExcelWorkbookService, ExcelWorkbookService>();
builder.Services.AddScoped<IQueryCountryAccessService, QueryCountryAccessService>();
builder.Services.AddScoped<IOraculService, OraculService>();
builder.Services.AddScoped<IKairosService, KairosService>();
builder.Services.AddScoped<IKairosAnalysisQueryService, KairosAnalysisQueryService>();
builder.Services.AddScoped<IAtlasService, AtlasService>();
builder.Services.AddScoped<IHydraQueryService, HydraQueryService>();
builder.Services.AddScoped<IHydraAccessService, HydraAccessService>();
builder.Services.AddScoped<IDikeAccessService, DikeAccessService>();
builder.Services.AddScoped<IDikeService, DikeService>();
builder.Services.AddScoped<IAreteQueryService, AreteQueryService>();
builder.Services.AddScoped<IAreteAccessService, AreteAccessService>();
builder.Services.AddScoped<IApisQueryService, ApisQueryService>();
builder.Services.AddScoped<IApisAccessService, ApisAccessService>();
builder.Services.AddScoped<IApisQvdPreviewService, ApisQvdPreviewService>();
builder.Services.AddScoped<IArgusAccessService, ArgusAccessService>();
builder.Services.AddScoped<IArgusTaskStatusService, ArgusTaskStatusService>();
builder.Services.AddScoped<IArgusServerMetricsService, ArgusServerMetricsService>();
builder.Services.AddScoped<IArgusTaskKillService, ArgusTaskKillService>();
builder.Services.AddScoped<ISendMailService, SendMailService>();
builder.Services.AddScoped<IIrisService, IrisService>();
builder.Services.AddScoped<ISendSmsService, SendSmsService>();
builder.Services.AddScoped<IContactVerificationService, ContactVerificationService>();
builder.Services.AddScoped<IHubAccessRequestService, HubAccessRequestService>();
builder.Services.AddScoped<IHubSecurityAlertService, HubSecurityAlertService>();
builder.Services.AddScoped<IHubFeedbackService, HubFeedbackService>();
builder.Services.AddScoped<IFileUploadAccessService, FileUploadAccessService>();
builder.Services.AddScoped<IFileUploadNotificationService, FileUploadNotificationService>();
builder.Services.AddScoped<ISendMailFileService, SendMailFileService>();
builder.Services.AddScoped<ISeshatService, SeshatService>();
builder.Services.AddHostedService<SeshatReminderSchedulerService>();
builder.Services.AddScoped<IClientInfoService, ClientInfoService>();
builder.Services.AddSingleton<IGlossaryService, GlossaryService>();
builder.Services.Configure<ThemisOptions>(
    builder.Configuration.GetSection(ThemisOptions.SectionName));
builder.Services.AddScoped<IThemisService, ThemisService>();
builder.Services.Configure<SphinxOptions>(
    builder.Configuration.GetSection(SphinxOptions.SectionName));
builder.Services.AddScoped<ISphinxService, SphinxService>();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

var supportedCultures = new[]
{
    new CultureInfo("lt-LT"),
    new CultureInfo("en-GB")
};

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("lt-LT"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
});

app.UseRouting();

app.UseAuthentication();

app.UseSession();

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "/";
    var isPublicPath =
        path.Equals("/", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/Index", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/Culture", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/Hub/Login", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/ClientInfo", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/Spinkx", StringComparison.OrdinalIgnoreCase);

    var hasHubSession =
        context.Session.GetString(DATA_EXCHANGE_HUB.Pages.Hub.LoginModel.SessionKey) == "1" ||
        context.User.Identity?.IsAuthenticated == true;
    var isStaticAsset =
        context.Request.Path.StartsWithSegments("/css") ||
        context.Request.Path.StartsWithSegments("/js") ||
        context.Request.Path.StartsWithSegments("/lib") ||
        context.Request.Path.StartsWithSegments("/icons") ||
        context.Request.Path.StartsWithSegments("/favicon.ico");

    if (!hasHubSession &&
        (path.Equals("/Hub", StringComparison.OrdinalIgnoreCase) ||
         path.Equals("/Hub/Index", StringComparison.OrdinalIgnoreCase)))
    {
        context.Response.Redirect("/");
        return;
    }

    if (!isPublicPath &&
        !isStaticAsset &&
        !hasHubSession)
    {
        var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
        context.Response.Redirect($"/Hub/Login?ReturnUrl={Uri.EscapeDataString(returnUrl)}");
        return;
    }

    await next();
});

app.UseAuthorization();

app.MapRazorPages();

app.Run();















