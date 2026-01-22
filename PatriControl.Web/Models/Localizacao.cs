using System.ComponentModel.DataAnnotations;

namespace PatriControl.Web.Models
{
    public class Localizacao
    {
        public int Id { get; set; }

        public int UnidadeId { get; set; }
        [Required]
        public string Nome { get; set; } = string.Empty;

        public Unidade Unidade { get; set; } = null!;
    }
}