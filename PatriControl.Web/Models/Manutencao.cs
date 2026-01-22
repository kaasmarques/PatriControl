using System;
using System.ComponentModel.DataAnnotations;

namespace PatriControl.Web.Models
{
    public class Manutencao
    {
        public int Id { get; set; }

        [Required]
        public string Codigo { get; set; } = string.Empty;

        [Required]
        public int PatrimonioId { get; set; }
        public Patrimonio? Patrimonio { get; set; }

        // Em andamento / Finalizada / Cancelada
        [Required]
        public string Status { get; set; } = "Em andamento";

        [Required]
        public DateTime AbertaEm { get; set; } = DateTime.Now;

        public DateTime? FinalizadaEm { get; set; }

        public int AbertaPorId { get; set; }
        public Usuario? AbertaPor { get; set; }

        public int? FinalizadaPorId { get; set; }
        public Usuario? FinalizadaPor { get; set; }

        // Obrigatório: Tipo de Manutenção (depende do Tipo do Patrimônio)
        [Required]
        public int TipoManutencaoId { get; set; }
        public TipoManutencao? TipoManutencao { get; set; }

        public int? ManutentorId { get; set; }
        public Manutentor? Manutentor { get; set; }

        public decimal? CustoEstimado { get; set; }
        public decimal? CustoFinal { get; set; }

        // somente na finalização
        public string? ObservacoesFinais { get; set; }

        public string? StatusFinalPatrimonio { get; set; }
    }
}