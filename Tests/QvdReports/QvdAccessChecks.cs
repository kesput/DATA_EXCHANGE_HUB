using System.Security.Claims;
using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

internal static class QvdAccessChecks
{
    internal static async Task RunAsync(string fixture, Action<bool, string> check)
    {
        QvdReportAccessService Policy(HttpContext? context, Countries roles) => new(
            new HttpContextAccessor { HttpContext = context }, new UserNames(), roles,
            NullLogger<QvdReportAccessService>.Instance);
        var user = Context();
        var rights = new Countries();
        check(!await Policy(null, rights).CanReadAsync("APIS") && rights.AdminChecks == 0,
            "A QVD execution without an HTTP request cannot inherit server account permissions");
        check(!await Policy(new DefaultHttpContext(), rights).CanReadAsync("APIS") && rights.AdminChecks == 0,
            "An unidentified user cannot obtain QVD ADMIN access");
        rights.AdminModules.Add("APIS");
        var policy = Policy(user, rights);
        check(await policy.CanReadAsync("Apis") && await policy.CanReadAsync("APIS") && rights.AdminChecks == 1,
            "QVD checks the existing module ADMIN permission once per request");
        check(!await policy.CanReadAsync("HYDRA"), "APIS ADMIN does not grant HYDRA QVD access");
        check(!await policy.CanReadAsync("ARGUS"), "The report ADMIN policy rejects unsupported modules");
        var failing = new Countries { FailAdminCheck = true };
        check(!await Policy(user, failing).CanReadAsync("APIS"), "A failing ADMIN check denies QVD access");

        foreach (var module in new[] { "Apis", "Arete", "Hydra" })
        {
            var data = new Queries(module);
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var ordinary = new Countries();
            var sqlPage = Page(module, data, cache, Policy(Context(), ordinary), ordinary, data.SqlId);
            await sqlPage.Get();
            check(sqlPage.Ids().SequenceEqual([data.SqlId]), module + " USER sees SQL queries without QVD names or other countries");
            await sqlPage.Execute();
            check(data.Executions == 1 && sqlPage.HasResult(), module + " USER retains normal SQL execution");
            foreach (var action in new[] { "get", "execute", "page", "excel", "csv" })
            {
                var denied = Page(module, data, cache, Policy(Context(), ordinary), ordinary, data.QvdId.ToLowerInvariant());
                var before = data.Executions;
                var result = await denied.Run(action);
                check(denied.Context.Response.StatusCode == 403 && result is not FileResult &&
                    data.Executions == before && !denied.HasResult() && denied.Ids().Count == 0,
                    module + " USER is denied direct QVD " + action + " before execution or cache access");
            }
            var admin = new Countries();
            admin.AdminModules.Add(module.ToUpperInvariant());
            var adminPage = Page(module, data, cache, Policy(Context(), admin), admin, data.QvdId);
            await adminPage.Get();
            check(adminPage.Ids().SequenceEqual([data.SqlId, data.QvdId]),
                module + " ADMIN sees QVD queries only within the allowed country");
            await adminPage.Execute();
            check(adminPage.HasResult(), module + " ADMIN can execute a QVD query");
            var executions = data.Executions;
            foreach (var action in new[] { "page", "excel", "csv" })
            {
                var allowed = Page(module, data, cache, Policy(Context(), admin), admin, data.QvdId);
                var result = await allowed.Run(action);
                check(data.Executions == executions && (action == "page" ? allowed.HasResult() : result is FileResult),
                    module + " ADMIN can reuse cached QVD " + action);
                var revoked = Page(module, data, cache, Policy(Context(), ordinary), ordinary, data.QvdId);
                var denied = await revoked.Run(action);
                check(revoked.Context.Response.StatusCode == 403 && denied is not FileResult && !revoked.HasResult(),
                    module + " revoked ADMIN cannot retrieve the still-cached QVD " + action);
            }
        }

        var config = new ConfigurationBuilder().Build();
        var env = new HostEnvironment(fixture);
        using (var command = new SqlCommand("not valid SQL"))
        {
            try { await new QvdQueryService(config, env).ExecuteAsync("Apis", "invalid", "invalid", command); }
            catch (UnauthorizedAccessException)
            {
                check(true, "The QVD engine denies a missing access policy before parsing or reading any files");
                goto deniedDefault;
            }
            throw new Exception("A missing QVD policy must deny execution.");
        }
        deniedDefault:
        foreach (var module in new[] { "Apis", "Arete", "Hydra", "Kairos" })
        {
            var path = Path.Combine(fixture, "Sql", module, "LT", "QVD", "Security", "Denied.sql");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Deliberately no QVD_SOURCE: denial must precede source resolution / DuckDB setup.
            File.WriteAllText(path, "SELECT 1 AS VALUE;");
            var id = Path.GetRelativePath(fixture, path).Replace('\\', '/');
            var deniedPolicy = Policy(Context(), new Countries());
            async Task ExecuteDenied()
            {
                var countries = new HashSet<string>(["LT"]);
                switch (module)
                {
                    case "Apis": await new ApisQueryService(config, env, Options.Create(new ApisOptions()), deniedPolicy).ExecuteAsync(id, countries: countries); break;
                    case "Arete": await new AreteQueryService(config, env, deniedPolicy).ExecuteAsync(id, countries: countries); break;
                    case "Hydra": await new HydraQueryService(config, env, deniedPolicy).ExecuteAsync(id, countries: countries); break;
                    default: await new KairosAnalysisQueryService(config, env, deniedPolicy).ExecuteAsync(id, countries: countries); break;
                }
            }
            try { await ExecuteDenied(); }
            catch (UnauthorizedAccessException)
            {
                check(true, module + " query service also enforces ADMIN at the QVD execution boundary");
                continue;
            }
            throw new Exception(module + " QVD query service did not deny USER.");
        }
    }

    private static DefaultHttpContext Context() => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "TEST\\qvd-user")], "Windows")),
        Session = new Session()
    };

    private static PageCase Page(string module, Queries queries, IMemoryCache cache,
        IQvdReportAccessService access, Countries countries, string queryId)
    {
        var context = Context();
        var pageContext = new PageContext { HttpContext = context };
        var excel = new ExcelWorkbookService();
        switch (module)
        {
            case "Apis":
                var apis = new DATA_EXCHANGE_HUB.Pages.Apis.IndexModel(queries, new ModuleAccess(), null!, countries,
                    excel, cache, new HostEnvironment(Path.GetTempPath()), access) { PageContext = pageContext, QueryId = queryId };
                return new(context, apis.OnGetAsync, apis.OnPostExecuteAsync, apis.OnPostResultPageAsync,
                    apis.OnPostExportExcelAsync, apis.OnPostExportCsvAsync, () => apis.Queries.Select(query => query.Id).ToArray(), () => apis.Result is not null);
            case "Arete":
                var arete = new DATA_EXCHANGE_HUB.Pages.Arete.IndexModel(queries, new ModuleAccess(), countries, excel, cache, access)
                    { PageContext = pageContext, QueryId = queryId };
                return new(context, arete.OnGetAsync, arete.OnPostExecuteAsync, arete.OnPostResultPageAsync,
                    arete.OnPostExportExcelAsync, arete.OnPostExportCsvAsync, () => arete.Queries.Select(query => query.Id).ToArray(), () => arete.Result is not null);
            default:
                var hydra = new DATA_EXCHANGE_HUB.Pages.Hydra.IndexModel(queries, new ModuleAccess(), countries, excel, cache, access)
                    { PageContext = pageContext, QueryId = queryId };
                return new(context, hydra.OnGetAsync, hydra.OnPostExecuteAsync, hydra.OnPostResultPageAsync,
                    hydra.OnPostExportExcelAsync, hydra.OnPostExportCsvAsync, () => hydra.Queries.Select(query => query.Id).ToArray(), () => hydra.Result is not null);
        }
    }

    private sealed record PageCase(DefaultHttpContext Context, Func<Task<IActionResult>> Get,
        Func<Task<IActionResult>> Execute, Func<Task<IActionResult>> Paging, Func<Task<IActionResult>> Excel,
        Func<Task<IActionResult>> Csv, Func<IReadOnlyList<string>> Ids, Func<bool> HasResult)
    {
        public Task<IActionResult> Run(string action) => action switch
        {
            "get" => Get(), "execute" => Execute(), "page" => Paging(), "excel" => Excel(), _ => Csv()
        };
    }

    private sealed class Countries : IQueryCountryAccessService
    {
        public HashSet<string> AdminModules { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int AdminChecks { get; private set; }
        public bool FailAdminCheck { get; init; }
        public Task<IReadOnlySet<string>> GetUserCountriesAsync(ClaimsPrincipal user, string appName, string connectionStringName, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(["LT"]));
        public Task<bool> HasAdminAccessAsync(ClaimsPrincipal user, string appName, string connectionStringName, CancellationToken cancellationToken = default)
        {
            AdminChecks++;
            if (FailAdminCheck) throw new InvalidOperationException("Simulated access store failure");
            return Task.FromResult(AdminModules.Contains(appName));
        }
    }
    private sealed class UserNames : IUserNameResolver
    {
        public string ResolveUserName(ClaimsPrincipal user) => user.Identity?.Name ?? "";
    }
    private sealed class ModuleAccess : IApisAccessService, IAreteAccessService, IHydraAccessService
    {
        public Task<bool> HasAccessAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class Queries(string module) : IApisQueryService, IAreteQueryService, IHydraQueryService
    {
        public string SqlId => $"Sql/{module}/LT/Stat3/Processes/Normal.sql";
        public string QvdId => $"Sql/{module}/LT/QVD/Processes/Report.sql";
        public int Executions { get; private set; }
        private string Area => module switch { "Apis" => "Processes", "Arete" => "Tracking", _ => "Handling" };
        private static IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows => [new Dictionary<string, string?> { ["VALUE"] = "1" }];
        private IEnumerable<(string Id, string Connection)> Definitions(IReadOnlySet<string>? countries)
        {
            if (countries is null || countries.Contains("LT")) { yield return (SqlId, "Stat3"); yield return (QvdId, "QVD"); }
            if (countries is null || countries.Contains("EE")) yield return (QvdId.Replace("/LT/", "/EE/"), "QVD");
        }
        IReadOnlyList<ApisQueryDefinition> IApisQueryService.ListQueries() => ((IApisQueryService)this).ListQueries(null!);
        IReadOnlyList<ApisQueryDefinition> IApisQueryService.ListQueries(IReadOnlySet<string> countries) => Definitions(countries)
            .Select(query => new ApisQueryDefinition(query.Id, "Test", query.Connection, Area, query.Id, "Text", null, "", [])).ToArray();
        Task<ApisQueryResult> IApisQueryService.ExecuteAsync(string queryId, IReadOnlyList<ApisParameterValue>? parameters, IReadOnlySet<string>? countries, CancellationToken cancellationToken)
        { Executions++; return Task.FromResult(new ApisQueryResult(["VALUE"], Rows)); }
        IReadOnlyList<AreteQueryDefinition> IAreteQueryService.ListQueries() => ((IAreteQueryService)this).ListQueries(null!);
        IReadOnlyList<AreteQueryDefinition> IAreteQueryService.ListQueries(IReadOnlySet<string> countries) => Definitions(countries)
            .Select(query => new AreteQueryDefinition(query.Id, "Test", query.Connection, Area, query.Id, "", [])).ToArray();
        Task<AreteQueryResult> IAreteQueryService.ExecuteAsync(string queryId, IReadOnlyList<ExchangeTableFilter>? filters, IReadOnlySet<string>? countries, CancellationToken cancellationToken)
        { Executions++; return Task.FromResult(new AreteQueryResult(["VALUE"], Rows)); }
        IReadOnlyList<HydraQueryDefinition> IHydraQueryService.ListQueries() => ((IHydraQueryService)this).ListQueries(null!);
        IReadOnlyList<HydraQueryDefinition> IHydraQueryService.ListQueries(IReadOnlySet<string> countries) => Definitions(countries)
            .Select(query => new HydraQueryDefinition(query.Id, "Test", query.Connection, Area, query.Id, [])).ToArray();
        Task<HydraQueryResult> IHydraQueryService.ExecuteAsync(string queryId, IReadOnlyList<ExchangeTableFilter>? filters, IReadOnlySet<string>? countries, CancellationToken cancellationToken)
        { Executions++; return Task.FromResult(new HydraQueryResult(["VALUE"], Rows)); }
    }

    private sealed class Session : ISession
    {
        private readonly Dictionary<string, byte[]> values = [];
        public bool IsAvailable => true;
        public string Id => "qvd-access-fixture";
        public IEnumerable<string> Keys => values.Keys;
        public void Clear() => values.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => values.Remove(key);
        public void Set(string key, byte[] value) => values[key] = value;
        public bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? value) => values.TryGetValue(key, out value);
    }
}
