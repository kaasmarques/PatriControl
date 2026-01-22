namespace PatriControl.Web.Models
{
    public class PaginacaoViewModel
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int Total { get; set; }
        public int TotalPages { get; set; }

        public string Action { get; set; } = "";
        public string Controller { get; set; } = "";

        // IMPORTANTE: manter como object pra aceitar int/string
        public Dictionary<string, object?> RouteValues { get; set; } = new();
    }
}
