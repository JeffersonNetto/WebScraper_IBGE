using Flurl.Http;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace WebScraper_IBGE
{
    public static class ServicesConfiguration
    {
        public static IServiceCollection Register(this IServiceCollection services)
        {
            Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Log.CloseAndFlush();

            FlurlHttp.Configure(x =>
            {
                x.Timeout = TimeSpan.FromSeconds(60);
                x.AfterCall = (state) =>
                {
                    if (state.Duration is not null)
                        Log.Information("Duração: {total} segundos", state.Duration.Value.TotalSeconds);
                };
                x.BeforeCall = (state) => Log.Information("Requisição iniciada em {url}", state.Request.Url);
                x.OnErrorAsync = async (err) =>
                {
                    if (err.Response is null)
                    {
                        Log.Error(err.Exception?.InnerException?.Message ?? err.Exception?.Message);
                        return;
                    }

                    var error = await err.Response.GetStringAsync();

                    if (string.IsNullOrWhiteSpace(error))
                        Log.Error(err.Exception, "Código: {statusCode}. Mensagem: {error}", err.Response.StatusCode, err.Exception?.InnerException?.Message ?? err.Exception?.Message);
                    else
                        Log.Error(err.Exception, "Código: {statusCode}. Mensagem: {error}", err.Response.StatusCode, error);
                };
            });

            services.AddScoped<IFlurlClient, FlurlClient>();
            services.AddScoped<IDataFetcherService, DataFetcherService>();
            services.AddScoped<Execute>();

            return services;
        }
    }
}
