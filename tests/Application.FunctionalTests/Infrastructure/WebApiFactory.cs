using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SaigonWaterbus.Application.FunctionalTests.Infrastructure;

public class WebApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder
            .UseSetting("ConnectionStrings:SaigonWaterbusDb", connectionString);
    }
}
