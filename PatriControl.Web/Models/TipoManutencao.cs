using System.ComponentModel.DataAnnotations;

namespace PatriControl.Web.Models
{
    public class TipoManutencao
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(120)]
        public string Nome { get; set; } = string.Empty;

        [Required]
        public int TipoPatrimonioId { get; set; }

        public TipoPatrimonio? TipoPatrimonio { get; set; }
    }
}
