using Microsoft.AspNetCore.Mvc.Rendering;
using PatriControl.Web.Models;

namespace PatriControl.Web.Models.ViewModels
{
    public class PatrimoniosIndexViewModel
    {
        // Lista paginada
        public List<Patrimonio> Itens { get; set; } = new();

        // Apoio para modais
        public List<TipoPatrimonio> Tipos { get; set; } = new();
        public List<Fornecedor> Fornecedores { get; set; } = new();
        public List<Unidade> Unidades { get; set; } = new();
        public List<Localizacao> Localizacoes { get; set; } = new();

        // Dropdowns Create/Edit (sem "Em manutenção")
        public List<SelectListItem> StatusListaCreateEdit { get; set; } = new();
        public List<SelectListItem> CondicaoListaCreateEdit { get; set; } = new();

        // Filtros (GET)
        public string? FiltroNumero { get; set; }
        public string? FiltroDescricao { get; set; }
        public string? FiltroUnidade { get; set; }
        public string? FiltroStatus { get; set; }
        public string? FiltroCondicao { get; set; }

        // Paginação
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public int TotalCount { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

        public int FirstItemIndex => TotalCount == 0 ? 0 : ((Page - 1) * PageSize) + 1;
        public int LastItemIndex => Math.Min(Page * PageSize, TotalCount);
    }
}
