using Newtonsoft.Json;

namespace WebScraper_IBGE
{
    public record Regiao
    {
        public int id { get; set; }
        public string sigla { get; set; }
        public string nome { get; set; }
    }

    public record UF
    {
        public int id { get; set; }
        public string sigla { get; set; }
        public string nome { get; set; }
        public Regiao regiao { get; set; }
    }

    public record Mesorregiao
    {
        public int id { get; set; }
        public string nome { get; set; }
        public UF UF { get; set; }
    }

    public record Microrregiao
    {
        public int id { get; set; }
        public string nome { get; set; }
        public Mesorregiao mesorregiao { get; set; }
    }

    public record RegiaoIntermediaria
    {
        public int id { get; set; }
        public string nome { get; set; }
        public UF UF { get; set; }
    }

    public record RegiaoImediata
    {
        public int id { get; set; }
        public string nome { get; set; }

        [JsonProperty("regiao-intermediaria")]
        public RegiaoIntermediaria RegiaoIntermediaria { get; set; }
    }

    public record Municipio
    {
        public string id { get; set; }
        public string nome { get; set; }
        public Microrregiao microrregiao { get; set; }

        [JsonProperty("regiao-imediata")]
        public RegiaoImediata RegiaoImediata { get; set; }
    }
}
