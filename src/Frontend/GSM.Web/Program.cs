using GSM.Client;
using GSM.Client.Http;
using GSM.Member.UI;
using GSM.Panel.UI.Navigation;
using GSM.UI.Shared;
using GSM.Web;
using GSM.Web.Platform;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["Api:ApiBaseUrl"]
    ?? throw new InvalidOperationException("Falta 'Api:ApiBaseUrl' en wwwroot/appsettings.json.");

builder.Services.AddMudServices();
builder.Services.AddScoped<ITokenStore, LocalStorageTokenStore>();
builder.Services.AddGsmClient(apiBaseUrl);
builder.Services.AddGsmUiShared();
builder.Services.AddGsmMemberUi();
builder.Services.AddGsmPanel();
builder.Services.AddScoped<GSM.UI.Shared.IClipboard, BrowserClipboard>();
builder.Services.AddSingleton<GSM.Member.UI.IQrScanner, GSM.Member.UI.UnsupportedQrScanner>();

await builder.Build().RunAsync();
