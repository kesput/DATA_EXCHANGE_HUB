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
var requestStore=new Requests();var landing=new DATA_EXCHANGE_HUB.Pages.IndexModel(requestStore,new NamedUser(),null!,null!,null!){PageContext=new PageContext{HttpContext=Context()},DomainUser=@"SNX\forged",AppName="HYDRA",RequestedAccess="USER"};
await landing.OnPostAsync();Check(requestStore.Saved?.DomainUser==@"SNX\owner","Access request always uses server identity");
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
sealed class Access:IHubModuleAccess {public bool Allowed;public int Calls;public Task<bool> CanOpenAsync(string path,ClaimsPrincipal user,CancellationToken ct=default){Calls++;return Task.FromResult(Allowed);}}
sealed class Texts:IStringLocalizer<SharedResource> {public LocalizedString this[string n]=>new(n,n);public LocalizedString this[string n,params object[] a]=>new(n,n);public IEnumerable<LocalizedString> GetAllStrings(bool p)=>Array.Empty<LocalizedString>();}
sealed class Hydra:IHydraAccessService {public Task<bool> HasAccessAsync(ClaimsPrincipal user,CancellationToken ct=default)=>Task.FromResult(true);}
sealed class NamedUser:IUserNameResolver {public string ResolveUserName(ClaimsPrincipal user)=>@"SNX\owner";}
sealed class Requests:IHubAccessRequestService {public HubAccessRequest? Saved;public Task SaveAsync(HubAccessRequest request,CancellationToken ct=default){Saved=request;return Task.CompletedTask;}}