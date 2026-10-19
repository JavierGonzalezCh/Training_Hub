using GSM.Client;
using GSM.Client.Generated;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.Web.Tests;

/// <summary>Cada cliente NSwag generado queda registrado en DI; los bUnit usan dobles y no lo detectan.</summary>
public class ClientRegistrationTests
{
    [Fact]
    public void Every_generated_client_interface_is_registered()
    {
        var services = new ServiceCollection();
        services.AddGsmClient("https://localhost:7219");
        var registered = services.Select(d => d.ServiceType).ToHashSet();

        var generated = typeof(IRoutinesClient).Assembly.GetTypes()
            .Where(t => t.IsInterface && t.Namespace == typeof(IRoutinesClient).Namespace && t.Name.EndsWith("Client"))
            .ToArray();

        Assert.NotEmpty(generated);
        Assert.Empty(generated.Where(t => !registered.Contains(t)).Select(t => t.Name));
    }
}
