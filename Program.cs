using Flurl.Http;
using Newtonsoft.Json;
using Polly;
using Polly.Retry;
using Serilog;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

bool IsTransientError(FlurlHttpException exception)
{
    int[] httpStatusCodesWorthRetrying =
    {
        (int)HttpStatusCode.RequestTimeout, // 408
        (int)HttpStatusCode.BadGateway, // 502
        (int)HttpStatusCode.ServiceUnavailable, // 503
        (int)HttpStatusCode.GatewayTimeout // 504
    };

    return exception.StatusCode.HasValue && httpStatusCodesWorthRetrying.Contains(exception.StatusCode.Value);
}

AsyncRetryPolicy BuildRetryPolicy()
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

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

var flurlClient = new FlurlClient();

flurlClient.Configure(s =>
{
    s.Timeout = TimeSpan.FromMinutes(1);
    s.BeforeCall = (state) => Log.Information("Requisição iniciada em {url}", state.Request.Url);
    s.AfterCall = (state) =>
    {
        if (state.Duration is not null)
            Log.Information("Duração: {total}", state.Duration.Value.TotalSeconds);
    };
    s.OnErrorAsync = async (err) =>
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

var fila = new Queue<Municipio>();
var timer = new Stopwatch();
timer.Start();
var dict = new List<Result>();
var web = new HtmlAgilityPack.HtmlWeb();
var tasks = new List<Task>();
var municipios = new List<Municipio>();
var estados = new string[]
{
    "mg",
    //"pr",
    //"sc",
    //"rs"
};

Log.Information("Obtendo municípios...");

foreach (var uf in estados)
{
    Log.Information("Obtendo municípios do estado: {uf}", uf.ToUpper());

    await BuildRetryPolicy().ExecuteAsync(async () =>
    {
        municipios = await flurlClient.Request($"https://servicodados.ibge.gov.br/api/v1/localidades/estados/{uf}/municipios").GetJsonAsync<List<Municipio>>();
    });
}

Log.Information("Municípios obtidos");

foreach (var m in municipios)
    fila.Enqueue(m);

async Task ProcessarMunicipio(Municipio municipio)
{
    ArgumentNullException.ThrowIfNull(municipio, nameof(municipio));

    var internalDict = new List<Result>();
    int contador = 0;
    string chave = "", valor = "";
    var erro = false;

    ArgumentNullException.ThrowIfNull(municipio, nameof(municipio));
    ArgumentNullException.ThrowIfNull(municipios, nameof(municipios));
    ArgumentNullException.ThrowIfNull(web, nameof(web));

    var siglaUf = municipio.microrregiao.mesorregiao.UF.sigla.ToLower();
    var nomeMunicipio = RemoverAcentuacao(municipio.nome.ToLower().Replace("'", "").Replace(" ", "-"));
    var url = $"https://cidades.ibge.gov.br/brasil/{siglaUf}/{nomeMunicipio}/panorama";

    HtmlAgilityPack.HtmlDocument doc = new();

    try
    {
        //doc = await web.LoadFromWebAsync(url);
        await BuildRetryPolicy().ExecuteAsync(async () =>
        {
            var res = await flurlClient.Request(url).GetStringAsync();

            doc.LoadHtml(res);

            Log.Information("{nomeMunicipio} / {siglaUf} - OK", nomeMunicipio, siglaUf.ToUpper());
        });
    }
    catch
    {
        Log.Error("{nomeMunicipio} / {siglaUf} - ERRO", nomeMunicipio, siglaUf.ToUpper());
        erro = true;
    }

    if (erro)
        return;

    internalDict.Add(new Result
    {
        IndexLinha = ++contador,
        IndexMunicipio = municipios.ToList().IndexOf(municipio),
        Chave = "Nome do município",
        Valor = nomeMunicipio,
    });

    ArgumentNullException.ThrowIfNull(doc, nameof(doc));

    var topo = doc.DocumentNode.SelectNodes("//div[@class='topo']");

    if (topo != null)
    {
        foreach (var item in topo.Descendants())
        {
            foreach (var subitem in item.Descendants())
            {
                if (subitem.Attributes["class"]?.Value == "topo__titulo")
                    chave = subitem.InnerText.Trim();

                if (subitem.Attributes["class"]?.Value == "topo__valor")
                    valor = subitem.InnerText.Trim();

                if (!string.IsNullOrWhiteSpace(chave) && !string.IsNullOrWhiteSpace(valor))
                {
                    internalDict.Add(new Result
                    {
                        IndexLinha = ++contador,
                        IndexMunicipio = municipios.ToList().IndexOf(municipio),
                        Chave = Regex.Replace(chave, @"\s+", " "),
                        Valor = Regex.Replace(valor, @"\s+", " ").Replace(" &nbsp;", ""),
                    });

                    chave = valor = "";
                }
            }
        }

        foreach (var item in doc.DocumentNode.SelectNodes("//table[@class='lista']"))
        {
            //foreach (var tr in item.SelectNodes("//tr[@class='lista__indicador']"))
            foreach (var tr in item.Descendants())
            {
                //foreach (var td in tr.SelectNodes("//td"))
                foreach (var td in tr.Descendants())
                {
                    if (td.Attributes["class"]?.Value == "lista__nome")
                        chave = td.InnerText.Trim();

                    if (td.Attributes["class"]?.Value == "lista__valor")
                        valor = td.InnerText.Trim();

                    if (!string.IsNullOrWhiteSpace(chave) && !string.IsNullOrWhiteSpace(valor))
                    {
                        internalDict.Add(new Result
                        {
                            IndexLinha = ++contador,
                            IndexMunicipio = municipios.ToList().IndexOf(municipio),
                            Chave = Regex.Replace(chave, @"\s+", " "),
                            Valor = Regex.Replace(valor, @"\s+", " ").Replace(" &nbsp;", ""),
                        });

                        chave = valor = "";
                    }
                }
            }
        }
    }

    ArgumentNullException.ThrowIfNull(dict, nameof(dict));

    dict.AddRange(internalDict);
}

async Task Finalizar()
{
    Log.Information("Obtenção de dados concluída.");

    var fileName = Path.Combine(Directory.GetCurrentDirectory(), "resultado.txt");

    using StreamWriter sw = File.CreateText(fileName);

    Log.Information("Gerando resultado...");

    foreach (var item in dict.Where(x => x != null).OrderBy(x => x.IndexMunicipio).ThenBy(x => x.IndexLinha))
        await sw.WriteLineAsync($"{item.Chave};{item.Valor};");

    Log.Information("Processo finalizado.");

    timer.Stop();

    Log.Information("Tempo total: {total} segundos", timer.Elapsed.TotalSeconds);

    Console.ReadLine();
}

while (fila.Any())
{
    var municipio = fila.Dequeue();

    Log.Information("Processando {municipio}", municipio.nome);
    await ProcessarMunicipio(municipio);

    //if (fila.Any())
    //{
    //    Console.WriteLine($"Aguardando 5 segundos...");
    //    Thread.Sleep(5000);
    //}
}

await Finalizar();


static string RemoverAcentuacao(string value)
{
    return new string(value.Normalize(NormalizationForm.FormD)
                                 .Where(ch => char.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                                 .ToArray());
}

record Result
{
    public long IndexLinha { get; init; } = default!;
    public long IndexMunicipio { get; init; } = default!;
    public string Chave { get; init; } = default!;
    public string Valor { get; init; } = default!;
}

record Regiao
{
    public int id { get; set; }
    public string sigla { get; set; }
    public string nome { get; set; }
}

record UF
{
    public int id { get; set; }
    public string sigla { get; set; }
    public string nome { get; set; }
    public Regiao regiao { get; set; }
}

record Mesorregiao
{
    public int id { get; set; }
    public string nome { get; set; }
    public UF UF { get; set; }
}

record Microrregiao
{
    public int id { get; set; }
    public string nome { get; set; }
    public Mesorregiao mesorregiao { get; set; }
}

record RegiaoIntermediaria
{
    public int id { get; set; }
    public string nome { get; set; }
    public UF UF { get; set; }
}

record RegiaoImediata
{
    public int id { get; set; }
    public string nome { get; set; }

    [JsonProperty("regiao-intermediaria")]
    public RegiaoIntermediaria RegiaoIntermediaria { get; set; }
}

record Municipio
{
    public string id { get; set; }
    public string nome { get; set; }
    public Microrregiao microrregiao { get; set; }

    [JsonProperty("regiao-imediata")]
    public RegiaoImediata RegiaoImediata { get; set; }
}