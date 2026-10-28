using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using DATA_EXCHANGE_HUB;
using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using DATA_EXCHANGE_HUB.Pages.Hub;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
int count=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);count++;Console.WriteLine("PASS "+text);}
DefaultHttpContext Context(string? name=@"SNX\owner"){
 var c=new DefaultHttpContext {Session=new MemorySession()};c.Response.Body=new MemoryStream();
 if(name is not null)c.User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Name,name)},"Windows"));return c;
}
(LoginModel Model,Credentials Credentials,Alerts Alerts) Login(DefaultHttpContext c,bool windowsHandler=false,string environment="Production"){
 var provider=new AuthenticationSchemeProvider(Options.Create(new AuthenticationOptions()));
 if(windowsHandler)provider.AddScheme(new AuthenticationScheme("Windows",null,typeof(Handler)));
 var validator=new Credentials();var alerts=new Alerts();
 var m=new LoginModel(Options.Create(new WorkHubOptions {Password="shared-dev"}),new Env{EnvironmentName=environment},validator,alerts,NullLogger<LoginModel>.Instance,provider){PageContext=new PageContext{HttpContext=c}};
 return(m,validator,alerts);
}
var ctx=Context();var login=Login(ctx);login.Model.ReturnUrl="/Dike/Index";
var result=await login.Model.OnGetAsync();
Check(result is LocalRedirectResult r && r.Url=="/Dike/Index","IIS Windows user automatically enters requested page");
Check(ctx.Session.GetString(LoginModel.SessionKey)=="1" && ctx.Session.GetString(LoginModel.PasswordSessionKey)=="0","Windows SSO creates non-development session");
Check(login.Credentials.Calls==0,"Windows SSO does not request or validate a password");
ctx=Context(null);login=Login(ctx,true);result=await login.Model.OnGetAsync();
Check(result is ChallengeResult challenge && challenge.AuthenticationSchemes.Contains("Windows"),"Missing IIS identity triggers Windows challenge");
ctx=Context(null);ctx.Session.SetString(LoginModel.SessionKey,"1");login=Login(ctx);result=await login.Model.OnGetAsync();
Check(result is PageResult && ctx.Response.StatusCode==401 && login.Model.WindowsAuthenticationRequired,"Production without Windows handler returns explanatory 401");
Check(ctx.Session.GetString(LoginModel.SessionKey) is null,"Missing production identity clears old session");
ctx=Context(null);login=Login(ctx,false,"Development");result=await login.Model.OnGetAsync();
Check(result is PageResult && !login.Model.WindowsAuthenticationRequired,"Development login remains available");
ctx=Context(null);login=Login(ctx);login.Model.UserName="owner";login.Model.Password="valid";result=await login.Model.OnPostAsync();
Check(result is PageResult && login.Credentials.Calls==0 && login.Model.ErrorMessage=="Hub.Security.WindowsRequired","Production cannot sign in with credentials alone");
ctx=Context();ctx.Session.SetString(LoginModel.SessionKey,"1");ctx.Session.SetString(WindowsUserNameResolver.AuthenticatedUserSessionKey,@"SNX\old-admin");login=Login(ctx);login.Model.UserName="colleague";login.Model.Password="valid";result=await login.Model.OnPostAsync();
Check(result is PageResult && login.Model.ErrorMessage=="Hub.Security.OtherUser","Other account is rejected on own terminal");
Check(ctx.Session.GetString(LoginModel.SessionKey) is null && ctx.Session.GetString(WindowsUserNameResolver.AuthenticatedUserSessionKey) is null,"Foreign login clears previous authentication");
Check(ctx.Session.GetString(LoginModel.BlockedSessionKey)=="1" && login.Alerts.Count==1,"Rejected foreign credentials block session and notify through fake");
Check(await login.Model.OnGetAsync() is PageResult,"Existing Windows authentication cannot reopen blocked session");
login.Model.UserName="owner";result=await login.Model.OnPostAsync();
Check(result is LocalRedirectResult && ctx.Session.GetString(LoginModel.BlockedSessionKey) is null,"Own validated account restores access");
ctx=Context();login=Login(ctx);login.Model.UserName="owner";login.Model.Password="shared-dev";await login.Model.OnPostAsync();
Check(ctx.Session.GetString(LoginModel.SessionKey) is null,"Shared development password never grants production access");
ctx=Context(@"OTHER\owner");login=Login(ctx);login.Model.UserName="owner";login.Model.Password="valid";result=await login.Model.OnPostAsync();
Check(result is PageResult && ctx.Session.GetString(LoginModel.SessionKey) is null,"Same short username in another domain rejected");
ctx=Context();login=Login(ctx);login.Model.ReturnUrl="//outside.invalid";result=await login.Model.OnGetAsync();Check(result is LocalRedirectResult safe && safe.Url=="/Hub/Index","External return URL is rejected");
var accessor=new HttpContextAccessor();var resolver=new WindowsUserNameResolver(accessor,new Env());
ctx=Context(null);accessor.HttpContext=ctx;ctx.Request.Headers["X-Remote-User"]=@"SNX\admin";
Check(resolver.ResolveUserName(ctx.User)=="","Production never falls back to process identity or forwarded username header");
ctx=Context(null);accessor.HttpContext=ctx;ctx.Features.Set<IServerVariablesFeature>(new Variables { ["LOGON_USER"]=@"IIS APPPOOL\Hub" });
Check(resolver.ResolveUserName(ctx.User)=="","AppPool virtual identity is rejected");
ctx=Context();accessor.HttpContext=ctx;ctx.Session.SetString(WindowsUserNameResolver.AuthenticatedUserSessionKey,@"SNX\old-admin");
Check(resolver.ResolveUserName(ctx.User)==@"SNX\owner","Current Windows identity overrides cached account");
ctx=Context(null);accessor.HttpContext=ctx;ctx.Features.Set<IServerVariablesFeature>(new Variables { ["LOGON_USER"]=@"SNX\alice" });
Check(resolver.ResolveUserName(ctx.User)==@"SNX\alice","IIS server variable resolves real user");
ctx=Context(null);accessor.HttpContext=ctx;ctx.Features.Set<IServerVariablesFeature>(new Variables { ["LOGON_USER"]=@"SNX\bob" });
Check(resolver.ResolveUserName(ctx.User)==@"SNX\bob","Singleton resolver does not retain previous request user");
var access=new Access();var ran=false;var middleware=new HubModuleAccessMiddleware(_=>{ran=true;return Task.CompletedTask;});var localizer=new Texts();
ctx=Context();ctx.Request.Path="/Dike/Index";ctx.Request.Method="GET";await middleware.InvokeAsync(ctx,access,localizer);
Check(!ran && ctx.Response.StatusCode==302 && ctx.Response.Headers.Location.ToString().Contains("AccessDenied"),"Unassigned module URL redirects to explanation");
ctx=Context();ctx.Request.Path="/Dike/Index";ctx.Request.Method="POST";await middleware.InvokeAsync(ctx,access,localizer);Check(!ran && ctx.Response.StatusCode==403,"Unassigned POST never reaches module");
ctx=Context();ctx.Request.Path="/Dike/Index";ctx.Request.Method="GET";ctx.Request.QueryString=new QueryString("?handler=Document");await middleware.InvokeAsync(ctx,access,localizer);Check(!ran && ctx.Response.StatusCode==403,"Unassigned document handler is blocked");
access.Allowed=true;ctx=Context();ctx.Request.Path="/Dike/Index";await middleware.InvokeAsync(ctx,access,localizer);Check(ran && ctx.Response.Headers.CacheControl=="no-store","Assigned module proceeds without caching");
ran=false;ctx=Context();ctx.Request.Path="/Dike/Index";ctx.Session.SetString(LoginModel.BlockedSessionKey,"1");var calls=access.Calls;await middleware.InvokeAsync(ctx,access,localizer);Check(!ran && calls==access.Calls,"Blocked login cannot use assigned module rights");
var registry=new ServiceCollection();registry.AddSingleton<IHydraAccessService>(new Hydra());
var policy=new HubModuleAccess(registry.BuildServiceProvider(),new NamedUser(),new ConfigurationBuilder().Build(),NullLogger<HubModuleAccess>.Instance);
Check(await policy.CanOpenAsync("/Hydra/Index",ctx.User),"Module uses existing permission service");Check(!await policy.CanOpenAsync("/Dike/Index",ctx.User),"Permission service failure denies access");

Check(policy.HasCheckFailures,"Module policy reports technical failure separately from denial");
var deniedRegistry=new ServiceCollection();deniedRegistry.AddSingleton<IHydraAccessService>(new DeniedHydra());
var deniedPolicy=new HubModuleAccess(deniedRegistry.BuildServiceProvider(),new NamedUser(),new ConfigurationBuilder().Build(),NullLogger<HubModuleAccess>.Instance);
Check(!await deniedPolicy.CanOpenAsync("/Hydra/Index",Context().User) && !deniedPolicy.HasCheckFailures,"Normal permission denial is not reported as technical error");
DATA_EXCHANGE_HUB.Pages.Hub.IndexModel Catalogue(DefaultHttpContext c,MatrixAccess matrix)=>new(new CatalogueApps(),new Acronyms(),matrix,new ClaimsUser()){PageContext=new PageContext{HttpContext=c}};
ctx=Context(null);ctx.Session.SetString(LoginModel.SessionKey,"1");var matrix=new MatrixAccess();var hub=Catalogue(ctx,matrix);result=await hub.OnGetAsync();
Check(result is RedirectToPageResult lost && lost.PageName=="/Hub/Login","Lost Windows identity with stale session returns to authentication");
Check(matrix.Calls==0,"Missing identity cannot render false no-module catalogue");
ctx=Context();ctx.Session.SetString(LoginModel.BlockedSessionKey,"1");matrix=new MatrixAccess{AllowHydra=true};hub=Catalogue(ctx,matrix);result=await hub.OnGetAsync();
Check(result is RedirectToPageResult blocked && blocked.PageName=="/Hub/Login" && matrix.Calls==0,"Blocked session cannot reopen HUB catalogue");
ctx=Context();matrix=new MatrixAccess{AllowHydra=true};hub=Catalogue(ctx,matrix);result=await hub.OnGetAsync();
Check(result is PageResult && hub.Applications.Count==1 && hub.Applications[0].Title=="HYDRA","Catalogue shows only assigned modules after returning to HUB");
Check(hub.CurrentUserName==@"SNX\owner" && ctx.Response.Headers.CacheControl=="no-store","Catalogue displays the actual checked identity and disables caching");
ctx=Context();matrix=new MatrixAccess();hub=Catalogue(ctx,matrix);await hub.OnGetAsync();
Check(hub.Applications.Count==0 && !hub.HasAccessCheckErrors && ctx.Response.StatusCode==200,"Genuine missing permissions retain normal no-access state");
ctx=Context();matrix=new MatrixAccess{FailHydra=true};hub=Catalogue(ctx,matrix);await hub.OnGetAsync();
Check(hub.HasAccessCheckErrors && hub.Applications.Count==0 && ctx.Response.StatusCode==503,"All inaccessible with technical error produces retryable 503, not false permission denial");
ctx=Context();matrix=new MatrixAccess{FailDike=true,AllowHydra=true};hub=Catalogue(ctx,matrix);await hub.OnGetAsync();
Check(hub.HasAccessCheckErrors && hub.Applications.Count==1 && ctx.Response.StatusCode==200,"Partial permission failure retains confirmed assigned module");
matrix=new MatrixAccess{AllowHydra=true};ctx=Context();hub=Catalogue(ctx,matrix);await hub.OnGetAsync();
Check(!hub.HasAccessCheckErrors && hub.Applications.Count==1,"Retry rechecks permissions without a stale failure flag");
ctx=Context();matrix=new MatrixAccess{AllowHydra=true};hub=Catalogue(ctx,matrix);hub.SearchTerm="missing";await hub.OnGetAsync();
Check(hub.Applications.Count==1 && hub.FilteredApplications.Count==0 && !hub.HasAccessCheckErrors,"Search mismatch is distinct from missing access and technical failure");


var ssoContext=Context(@"SNX\owner");var ssoLogin=Login(ssoContext);await ssoLogin.Model.OnGetAsync();
Check(ssoContext.Session.GetString(WindowsUserNameResolver.AuthenticatedUserSessionKey)==@"SNX\owner","Windows SSO preserves the server-verified name for later requests");
var ssoAccessor=new HttpContextAccessor();var ssoResolver=new WindowsUserNameResolver(ssoAccessor,new Env());
var followup=Context(null);followup.Session=ssoContext.Session;ssoAccessor.HttpContext=followup;
Check(ssoResolver.ResolveUserName(followup.User)==@"SNX\owner","IIS request without a Windows token retains the verified SSO identity");
var ssoRegistry=new ServiceCollection();ssoRegistry.AddSingleton<IHydraAccessService>(new Hydra());ssoRegistry.AddSingleton<IDikeAccessService>(new DeniedDike());
IHubModuleAccess SsoPolicy()=>new HubModuleAccess(ssoRegistry.BuildServiceProvider(),ssoResolver,new ConfigurationBuilder().Build(),NullLogger<HubModuleAccess>.Instance);
var ssoHub=new DATA_EXCHANGE_HUB.Pages.Hub.IndexModel(new CatalogueApps(),new Acronyms(),SsoPolicy(),ssoResolver){PageContext=new PageContext{HttpContext=followup}};
result=await ssoHub.OnGetAsync();
Check(result is PageResult && ssoHub.CurrentUserName==@"SNX\owner" && ssoHub.Applications.Count==1 && !ssoHub.HasAccessCheckErrors,"SSO login followed by anonymous IIS HUB request keeps assigned modules");
ran=false;followup.Request.Path="/Hydra/Index";await middleware.InvokeAsync(followup,SsoPolicy(),localizer);
Check(ran,"Verified SSO session also works for direct assigned module access");
followup=Context(null);followup.Session=ssoContext.Session;ssoAccessor.HttpContext=followup;
ssoHub=new DATA_EXCHANGE_HUB.Pages.Hub.IndexModel(new CatalogueApps(),new Acronyms(),SsoPolicy(),ssoResolver){PageContext=new PageContext{HttpContext=followup}};await ssoHub.OnGetAsync();
Check(ssoHub.Applications.Count==1,"Returning to HUB repeatedly does not require another Windows login");
followup.User=Context(@"SNX\different").User;
Check(ssoResolver.ResolveUserName(followup.User)==@"SNX\different" && followup.Session.GetString(WindowsUserNameResolver.AuthenticatedUserSessionKey)==@"SNX\different","New trusted Windows identity replaces the previous verified session identity");
followup.User=Context(null).User;
Check(ssoResolver.ResolveUserName(followup.User)==@"SNX\different","Anonymous follow-up cannot revive the previous account after identity changes");
followup=Context(null);ssoAccessor.HttpContext=followup;
Check(ssoResolver.ResolveUserName(followup.User)=="","New or expired session cannot reuse an earlier SSO identity");

var requestStore=new Requests();var landing=new DATA_EXCHANGE_HUB.Pages.IndexModel(requestStore,new NamedUser(),null!,null!,null!,new AuthenticationSchemeProvider(Options.Create(new AuthenticationOptions()))){PageContext=new PageContext{HttpContext=Context()},DomainUser=@"SNX\forged",AppName="HYDRA",RequestedAccess="USER"};
await landing.OnPostAsync();Check(requestStore.Saved?.DomainUser==@"SNX\owner","Access request always uses server identity");

(DATA_EXCHANGE_HUB.Pages.IndexModel Model, Requests Store) AccessRequest(DefaultHttpContext c,bool windowsHandler=false,Requests? store=null) {
 var schemes=new AuthenticationSchemeProvider(Options.Create(new AuthenticationOptions()));
 if(windowsHandler)schemes.AddScheme(new AuthenticationScheme("Windows",null,typeof(Handler)));
 store ??= new Requests();
 var page=new DATA_EXCHANGE_HUB.Pages.IndexModel(store,new ClaimsUser(),new Glossary(),new PublicSphinx(),null!,schemes){PageContext=new PageContext{HttpContext=c}};
 return(page,store);
}
ctx=Context(null);var requestPage=AccessRequest(ctx,true);requestPage.Model.AppName="APIS";requestPage.Model.RequestedAccess="ADMIN";requestPage.Model.Comment="Need reporting access";requestPage.Model.DomainUser=@"SNX\forged";
result=await requestPage.Model.OnPostAsync();
Check(result is RedirectToPageResult authRedirect && authRedirect.PageName=="/Index" && authRedirect.PageHandler=="AuthorizeAccessRequest","Anonymous request goes to GET authentication, preventing POST replay");
Check(requestPage.Store.Calls==0,"Anonymous request never writes to access store");
result=await requestPage.Model.OnGetAuthorizeAccessRequestAsync();
Check(result is ChallengeResult native && native.AuthenticationSchemes.Contains("Windows"),"Access request triggers native Windows authentication");
Check(ctx.Response.Headers.CacheControl=="no-store","Identity challenge is not cached");
Check(requestPage.Store.Calls==0,"Challenge does not submit request");
ctx.User=Context(@"SNX\actual").User;
result=await requestPage.Model.OnGetAuthorizeAccessRequestAsync();
Check(result is RedirectToPageResult back && back.PageName=="/Index" && back.PageHandler is null && back.Fragment=="access-request" && (string?)back.RouteValues?["AppName"]=="APIS","Authenticated request returns to original access form and application");
Check(requestPage.Store.Calls==0 && ctx.Session.GetString(LoginModel.SessionKey) is null,"Identity confirmation neither submits nor creates HUB password session");
var restoredPage=AccessRequest(ctx,true,requestPage.Store).Model;restoredPage.ModelState.SetModelValue("AppName","HYDRA","HYDRA");await restoredPage.OnGetAsync();
Check(restoredPage.DomainUser==@"SNX\actual","Restored form shows real Windows identity instead of forged input");
Check(restoredPage.AppName=="APIS" && restoredPage.RequestedAccess=="ADMIN" && restoredPage.Comment=="Need reporting access","Application, requested role and comment survive authentication");
Check(!restoredPage.ModelState.ContainsKey("AppName"),"GET binding cannot replace restored application in rendered select");
Check(requestPage.Store.Calls==0,"Returning to form waits for explicit user submission");
restoredPage.DomainUser=@"SNX\forged";await restoredPage.OnPostAsync();
Check(requestPage.Store.Calls==1 && requestPage.Store.Saved?.DomainUser==@"SNX\actual" && requestPage.Store.Saved?.AppName=="APIS","Explicit submission saves exactly once using real authenticated user");
var secondGet=AccessRequest(ctx,true).Model;await secondGet.OnGetAsync();
Check(secondGet.Comment=="" && secondGet.RequestedAccess=="USER","Authentication draft is consumed only once");
ctx=Context(null);requestPage=AccessRequest(ctx);result=await requestPage.Model.OnGetAuthorizeAccessRequestAsync();
Check(result is PageResult && ctx.Response.StatusCode==401 && requestPage.Model.ErrorMessage=="Hub.Security.WindowsRequired","Plain Kestrel authentication failure shows explanation instead of throwing");
Check(requestPage.Store.Calls==0,"Missing Windows handler cannot create access request");
ctx=Context();ctx.Session.SetString(LoginModel.BlockedSessionKey,"1");requestPage=AccessRequest(ctx,true);await requestPage.Model.OnGetAuthorizeAccessRequestAsync();
Check(ctx.Session.GetString(LoginModel.BlockedSessionKey)=="1","Request identity confirmation cannot clear a blocked HUB login");
ctx=Context(null);requestPage=AccessRequest(ctx,true);requestPage.Model.AppName="//outside.invalid";requestPage.Model.RequestedAccess="SUPERADMIN";requestPage.Model.Comment=new string('x',2000);requestPage.Model.OnPostAuthenticateAccessRequest();ctx.User=Context().User;await requestPage.Model.OnGetAsync();
Check(requestPage.Model.AppName=="HYDRA" && requestPage.Model.RequestedAccess=="USER" && requestPage.Model.Comment.Length==1000,"Authentication draft bounds input and cannot introduce unknown app or role");
ctx=Context(null);requestPage=AccessRequest(ctx,true);requestPage.Model.Comment="Expired";requestPage.Model.OnPostAuthenticateAccessRequest();
var draft=System.Text.Json.Nodes.JsonNode.Parse(ctx.Session.GetString("Hub.AccessRequest.Draft")!)!;draft["ExpiresAt"]=DateTimeOffset.UtcNow.AddMinutes(-1);ctx.Session.SetString("Hub.AccessRequest.Draft",draft.ToJsonString());ctx.User=Context().User;requestPage=AccessRequest(ctx,true);await requestPage.Model.OnGetAsync();
Check(requestPage.Model.Comment=="","Expired authentication draft is discarded");
ctx=Context();ctx.Session.SetString("Hub.AccessRequest.Draft","invalid-json");requestPage=AccessRequest(ctx,true);await requestPage.Model.OnGetAsync();
Check(requestPage.Model.Comment=="","Corrupt authentication draft cannot break public page");

var config=new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json").AddJsonFile("appsettings.Production.template.json").Build();
var apps=config.GetSection("HubApplications").Get<List<HubApplication>>()!;
Check(apps.Any(a=>a.Title=="DIKE") && apps.Any(a=>a.Title=="IRIS"),"Production template preserves new modules");
Check(apps.Select(a=>a.Page).Distinct(StringComparer.OrdinalIgnoreCase).Count()==apps.Count,"Production catalogue contains no merged duplicates");
Check(config["WorkHub:DefaultDomain"]=="SNX","Production inherits the correct domain");
Console.WriteLine($"ALL {count} PRODUCTION/IIS AUTH CHECKS PASSED; no real credentials validated and no notifications sent.");

sealed class MemorySession:ISession {
 readonly Dictionary<string,byte[]> values=new();public bool IsAvailable=>true;public string Id=>"test";public IEnumerable<string> Keys=>values.Keys;
 public void Clear()=>values.Clear();public Task CommitAsync(CancellationToken ct=default)=>Task.CompletedTask;public Task LoadAsync(CancellationToken ct=default)=>Task.CompletedTask;
 public void Remove(string key)=>values.Remove(key);public void Set(string key,byte[] value)=>values[key]=value;
 public bool TryGetValue(string key,[NotNullWhen(true)]out byte[]? value)=>values.TryGetValue(key,out value);
}
sealed class Variables:IServerVariablesFeature {readonly Dictionary<string,string?> values=new();public string? this[string key]{get=>values.GetValueOrDefault(key);set=>values[key]=value;}}
sealed class Env:IWebHostEnvironment {public string EnvironmentName{get;set;}="Production";public string ApplicationName{get;set;}="test";public string ContentRootPath{get;set;}="";public string WebRootPath{get;set;}="";public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();}
sealed class Credentials:IWindowsCredentialValidator {public int Calls;public bool TryNormalizeUserName(string user,out string normalized){normalized=@"SNX\"+user;return !string.IsNullOrWhiteSpace(user);}public bool TryValidate(string user,string password,out string normalized){Calls++;normalized=@"SNX\"+user;return password=="valid";}}
sealed class Alerts:IHubSecurityAlertService {public int Count;public Task NotifySuspiciousLoginAsync(string a,string b,string c,DateTimeOffset d,CancellationToken ct=default){Count++;return Task.CompletedTask;}}
sealed class Handler:IAuthenticationHandler {public Task InitializeAsync(AuthenticationScheme s,HttpContext c)=>Task.CompletedTask;public Task<AuthenticateResult> AuthenticateAsync()=>Task.FromResult(AuthenticateResult.NoResult());public Task ChallengeAsync(AuthenticationProperties? p)=>Task.CompletedTask;public Task ForbidAsync(AuthenticationProperties? p)=>Task.CompletedTask;}
sealed class Access:IHubModuleAccess {public bool HasCheckFailures=>false;public bool Allowed;public int Calls;public Task<bool> CanOpenAsync(string path,ClaimsPrincipal user,CancellationToken ct=default){Calls++;return Task.FromResult(Allowed);}}
sealed class Texts:IStringLocalizer<SharedResource> {public LocalizedString this[string n]=>new(n,n);public LocalizedString this[string n,params object[] a]=>new(n,n);public IEnumerable<LocalizedString> GetAllStrings(bool p)=>Array.Empty<LocalizedString>();}
sealed class Hydra:IHydraAccessService {public Task<bool> HasAccessAsync(ClaimsPrincipal user,CancellationToken ct=default)=>Task.FromResult(true);}
sealed class NamedUser:IUserNameResolver {public string ResolveUserName(ClaimsPrincipal user)=>@"SNX\owner";}
sealed class Requests:IHubAccessRequestService {public HubAccessRequest? Saved;public int Calls;public Task SaveAsync(HubAccessRequest request,CancellationToken ct=default){Calls++;Saved=request;return Task.CompletedTask;}}
sealed class ClaimsUser:IUserNameResolver {public string ResolveUserName(ClaimsPrincipal user)=>user.Identity?.IsAuthenticated==true?user.Identity.Name??"":"";}
sealed class Glossary:IGlossaryService {public Task<IReadOnlyList<GlossaryTermView>> ListAsync(System.Globalization.CultureInfo culture,CancellationToken ct=default)=>Task.FromResult<IReadOnlyList<GlossaryTermView>>([]);}
sealed class PublicSphinx:ISphinxService {
 public Task<IReadOnlyList<SphinxSurveyCard>> GetSurveyCardsAsync(string user,CancellationToken ct=default)=>Task.FromResult<IReadOnlyList<SphinxSurveyCard>>([]);
 public Task<IReadOnlyList<SphinxSurveyCard>> GetQuickPollCardsAsync(string user,CancellationToken ct=default)=>Task.FromResult<IReadOnlyList<SphinxSurveyCard>>([]);
 public Task<SphinxAccessState> GetAccessAsync(ClaimsPrincipal user,CancellationToken ct=default)=>throw new NotImplementedException();
 public Task CreateSurveyAsync(SphinxCreateSurveyRequest r,CancellationToken ct=default)=>throw new NotImplementedException();
 public Task<SphinxSurveyDetail?> GetSurveyDetailAsync(int id,string user,CancellationToken ct=default)=>throw new NotImplementedException();
 public Task SubmitResponseAsync(SphinxSubmitRequest r,CancellationToken ct=default)=>throw new NotImplementedException();
 public Task<SphinxSurveyDetail?> GetLatestQuickPollAsync(string user,CancellationToken ct=default)=>throw new NotImplementedException();
 public Task<IReadOnlyDictionary<int,int>> GetQuickPollResultsAsync(int id,CancellationToken ct=default)=>throw new NotImplementedException();
 public Task<IReadOnlyList<SphinxManagementItem>> GetManagementListAsync(string user,bool admin,CancellationToken ct=default)=>throw new NotImplementedException();
 public Task<SphinxSurveyDetail?> GetSurveyForOwnerAsync(int id,string user,bool admin,CancellationToken ct=default)=>throw new NotImplementedException();
 public Task CloseSurveyAsync(int id,string user,bool admin,CancellationToken ct=default)=>throw new NotImplementedException();
 public Task<SphinxResultsExport?> GetResultsExportAsync(int id,string user,bool admin,CancellationToken ct=default)=>throw new NotImplementedException();
}

sealed class DeniedHydra:IHydraAccessService {public Task<bool> HasAccessAsync(ClaimsPrincipal user,CancellationToken ct=default)=>Task.FromResult(false);}
sealed class CatalogueApps:IHubApplicationCatalog {public IReadOnlyList<HubApplication> GetApplications()=>[new(){Title="HYDRA",Page="/Hydra/Index"},new(){Title="DIKE",Page="/Dike/Index"}];}
sealed class Acronyms:IHubApplicationAcronymService {public IReadOnlyDictionary<string,HubApplicationAcronym> GetAcronymsByTitle()=>new Dictionary<string,HubApplicationAcronym>();}
sealed class MatrixAccess:IHubModuleAccess {
 public bool HasCheckFailures{get;set;} public bool AllowHydra,FailHydra,FailDike; public int Calls;
 public Task<bool> CanOpenAsync(string path,ClaimsPrincipal user,CancellationToken ct=default){Calls++;var hydra=path.Contains("Hydra");if(hydra?FailHydra:FailDike){HasCheckFailures=true;return Task.FromResult(false);}return Task.FromResult(hydra && AllowHydra);}
}

sealed class DeniedDike:IDikeAccessService {public Task<bool> HasAccessAsync(ClaimsPrincipal user,CancellationToken ct=default)=>Task.FromResult(false);public Task<bool> IsAdminAsync(ClaimsPrincipal user,CancellationToken ct=default)=>Task.FromResult(false);}
