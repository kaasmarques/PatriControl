using System.ComponentModel.DataAnnotations;

namespace PatriControl.Web.Models
{
    public class Manutentor
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Informe o nome do manutentor.")]
        [MaxLength(120)]
        public string Nome { get; set; } = string.Empty;
    }
}
