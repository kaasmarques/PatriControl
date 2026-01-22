using System;
using System.ComponentModel.DataAnnotations;

namespace PatriControl.Web.Models
{
    public class AuditLog
    {
        public int Id { get; set; }

        [Required]
        public DateTime DataHora { get; set; } = DateTime.Now;

        // Quem executou (null = sistema)
        public int? UsuarioId { get; set; }
        public Usuario? Usuario { get; set; }

        // Ex: "CRIAR", "EDITAR", "EXCLUIR", "LOGIN", "LOGOFF", "FINALIZAR", "CANCELAR"
        [Required, MaxLength(60)]
        public string Acao { get; set; } = string.Empty;

        // Ex: "Patrimonio", "Manutencao", "Usuario", "Fornecedor"...
        [MaxLength(80)]
        public string? Entidade { get; set; }

        // Id do registro afetado (se existir)
        public int? EntidadeId { get; set; }

        // Texto livre: o que aconteceu / antes->depois, etc.
        [MaxLength(4000)]
        public string? Detalhes { get; set; }

        [MaxLength(64)]
        public string? Ip { get; set; }

        [MaxLength(300)]
        public string? UserAgent { get; set; }
    }
}
