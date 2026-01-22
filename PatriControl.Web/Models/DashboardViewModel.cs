using System;
using System.Collections.Generic;
using PatriControl.Web.Models;

namespace PatriControl.Web.Models.ViewModels
{
    public class DashboardViewModel
    {
        // ===== Filtros =====
        public DateTime DataInicio { get; set; }
        public DateTime DataFim { get; set; }

        public string? Unidade { get; set; }
        public string? Tipo { get; set; }
        public string? Fornecedor { get; set; }

        public string? Status { get; set; }
        public string? Condicao { get; set; }

        // ===== KPIs (Patrimônios) =====
        public int TotalPatrimonios { get; set; }
        public int TotalAtivos { get; set; }
        public int TotalBaixados { get; set; }
        public int TotalEmManutencao { get; set; }

        // ===== KPIs (Manutenções no período) =====
        public int ManutencoesAbertasPeriodo { get; set; }
        public int ManutencoesFinalizadasPeriodo { get; set; }
        public decimal CustoFinalTotalPeriodo { get; set; }
        public decimal TicketMedioFinalizadasPeriodo { get; set; }

        // ===== Gráficos =====
        public ChartData StatusPatrimonios { get; set; } = new();
        public ChartData TopUnidades { get; set; } = new();
        public ChartData TopTipos { get; set; } = new();
        public ChartData ManutencoesPorMes { get; set; } = new();
        public ChartData CustoPorMes { get; set; } = new();

        // ===== Tabelas =====
        public List<Patrimonio> Recentes { get; set; } = new();
        public List<ManutencaoResumo> ManutencoesRecentes { get; set; } = new();
    }

    public class ChartData
    {
        public List<string> Labels { get; set; } = new();
        public List<decimal> Values { get; set; } = new();
    }

    public class ManutencaoResumo
    {
        public int Id { get; set; }
        public string? Codigo { get; set; }
        public string? Status { get; set; }
        public DateTime AbertaEm { get; set; }
        public DateTime? FinalizadaEm { get; set; }

        public string? PatrimonioNumero { get; set; }
        public string? PatrimonioDescricao { get; set; }
        public string? TipoPatrimonio { get; set; }

        public string? Manutentor { get; set; }
        public decimal? CustoEstimado { get; set; }
        public decimal? CustoFinal { get; set; }
    }
}
