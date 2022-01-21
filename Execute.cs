using Serilog;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace WebScraper_IBGE
{
    public class Execute
    {
        private readonly IDataFetcherService _dataFetcherService;
        readonly Queue<Municipio> fila = new();
        readonly List<Result> dict = new();
        readonly HtmlAgilityPack.HtmlWeb web = new();
        private List<Municipio> municipios = new();
        private readonly Stopwatch timer = new();
        private readonly string path = Path.Combine(Directory.GetCurrentDirectory(), "resultado.txt");
        private readonly string[] estados = new string[] { "mg", "pr", "sc", "rs" };

        public Execute(IDataFetcherService dataFetcherService)
        {
            _dataFetcherService = dataFetcherService;
        }

        public async Task IniciarExecucao()
        {
            timer.Start();

            if(File.Exists(path))
                File.Delete(path);

            municipios = await _dataFetcherService.ObterMunicipios(estados);

            foreach (var municipio in municipios)
                fila.Enqueue(municipio);

            while (fila.Any())
            {
                var municipio = fila.Dequeue();

                Log.Information("Processando {municipio}", municipio.nome);

                await ProcessarMunicipio(municipio);

                await EscreverNoArquivo();
            }

            Finalizar();
        }

        private async Task EscreverNoArquivo()
        {            
            var contents = dict.Where(x => x != null)
                               .OrderBy(x => x.IndexMunicipio)
                               .ThenBy(x => x.IndexLinha)
                               .Select(x => $"{x.Chave};{x.Valor};");

            await File.AppendAllLinesAsync(path, contents);            
        }

        private async Task ProcessarMunicipio(Municipio municipio)
        {            
            var internalDict = new List<Result>();
            int contador = 0;
            string chave = "", valor = "";
            var erro = false;                    

            var siglaUf = municipio.microrregiao.mesorregiao.UF.sigla.ToLower();
            var nomeMunicipio = municipio.nome.ToLower().Replace("'", "").Replace(" ", "-").RemoverAcentuacao();
            var url = $"https://cidades.ibge.gov.br/brasil/{siglaUf}/{nomeMunicipio}/panorama";

            HtmlAgilityPack.HtmlDocument doc = new();

            try
            {
                doc.LoadHtml(await _dataFetcherService.ObterDadosMunicipio(url));
                Log.Information("{nomeMunicipio} / {siglaUf} - OK", nomeMunicipio, siglaUf.ToUpper());
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
                    foreach (var tr in item.Descendants())
                    {
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

            dict.AddRange(internalDict);
        }

        private void Finalizar()
        {
            Log.Information("Processo finalizado.");

            timer.Stop();

            Log.Information("Tempo total: {total} segundos", timer.Elapsed.TotalSeconds);

            Console.ReadLine();
        }

        record Result
        {
            public long IndexLinha { get; init; } = default!;
            public long IndexMunicipio { get; init; } = default!;
            public string Chave { get; init; } = default!;
            public string Valor { get; init; } = default!;
        }
    }
}
