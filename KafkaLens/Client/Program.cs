using KafkaLens.Client.DataAccess;

using Blazor.Extensions.Logging;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Blazored;
using Blazored.LocalStorage;
using Syncfusion.Blazor;

namespace KafkaLens.Client
{
    public class Program
    {
        private const string LicenseKey = "NDkzMjIxQDMxMzkyZTMyMmUzMEdvNmZpZHBRbG1iRUhDT2FiRXZPY1hFTW5iZ1NqTFJSS1hZc0F0MEJub1E9";

        public static async Task Main(string[] args)
        {
            Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(LicenseKey);

            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("#app");

            builder.Services
                .AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) })
                .AddScoped<KafkaContext>()
                .AddBlazoredLocalStorage()
                .AddLogging(builder => builder
                    .AddBrowserConsole()
                    .SetMinimumLevel(LogLevel.Information));

            builder.Services.AddSyncfusionBlazor();

            await builder.Build().RunAsync();
        }
    }
}
