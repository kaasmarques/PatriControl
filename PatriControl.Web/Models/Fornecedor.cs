using System.ComponentModel.DataAnnotations;

namespace PatriControl.Web.Models
{
    public class Fornecedor
    {
        public int Id { get; set; }
        [Required]
        public string Nome { get; set; } = string.Empty;
    }
}
