using System;

namespace PatriControl.Web.Models
{
    public class Tramite
    {
        public int Id { get; set; }

        public int PatrimonioId { get; set; }

        // Navegação pode ser nula (EF preenche depois)
        public Patrimonio? Patrimonio { get; set; }

        public int UsuarioId { get; set; }

        // Strings obrigatórias com valor padrão
        public string NomeUsuario { get; set; } = string.Empty;

        // "CRIACAO" ou "ALTERACAO"
        public string Tipo { get; set; } = string.Empty;

        public DateTime DataHora { get; set; }

        // Para alterações (podem ser nulos em trâmite de criação)
        public string? Campo { get; set; }
        public string? ValorAntigo { get; set; }
        public string? ValorNovo { get; set; }
    }
}
