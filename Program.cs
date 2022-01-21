using Microsoft.Extensions.DependencyInjection;
using WebScraper_IBGE;

public partial class Program
{
    static async Task Main()
    {
        IServiceCollection services = new ServiceCollection();

        services.Register();

        var serviceProvider = services.BuildServiceProvider();
        var execute = serviceProvider.GetService<Execute>();

        await execute.IniciarExecucao();
    }
}