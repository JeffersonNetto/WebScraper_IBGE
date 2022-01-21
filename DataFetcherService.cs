using Flurl.Http;
using Polly.Retry;
using Polly;
using Serilog;

namespace WebScraper_IBGE
{
    public interface IDataFetcherService
    {
        Task<string?> ObterDadosMunicipio(string url);
        Task<List<Municipio>> ObterMunicipios(string[] estados);
    }

    public class DataFetcherService : IDataFetcherService
    {
        private readonly IFlurlClient _flurlClient;

        public DataFetcherService(IFlurlClient flurlClient)
        {
            _flurlClient = flurlClient;
        }        

        private static AsyncRetryPolicy RetryPolicy
        {
            get
            {
                var retryPolicy = Policy
               .Handle<FlurlHttpException>()
               .WaitAndRetryAsync(3, retryAttempt =>
               {
                   var nextAttemptIn = TimeSpan.FromSeconds(Math.Pow(3, retryAttempt));
                   Log.Information($"Próxima tentativa em {nextAttemptIn.TotalSeconds} segundos.");
                   return nextAttemptIn;
               });

                return retryPolicy;
            }
        }

        public async Task<List<Municipio>> ObterMunicipios(string[] estados)
        {
            var municipios = new List<Municipio>();

            foreach (var uf in estados)
            {
                Log.Information("Obtendo municípios do estado: {uf}", uf.ToUpper());

                await RetryPolicy.ExecuteAsync(async () =>
                {
                    municipios.AddRange(await _flurlClient.Request($"https://servicodados.ibge.gov.br/api/v1/localidades/estados/{uf}/municipios").GetJsonAsync<List<Municipio>>());
                });
            }

            Log.Information("{total} Municípios obtidos", municipios.Count);

            return municipios;
        }

        public async Task<string?> ObterDadosMunicipio(string url)
        {
            string? result = null;

            await RetryPolicy.ExecuteAsync(async () =>
            {
                result = await _flurlClient.Request(url).GetStringAsync();
            });

            return result;
        }
    }
}
