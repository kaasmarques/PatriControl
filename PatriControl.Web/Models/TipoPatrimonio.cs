using System.ComponentModel.DataAnnotations;

namespace PatriControl.Web.Models
{
    public class TipoPatrimonio
    {
        public int Id { get; set; }

        /// <summary>
        /// Nome do tipo de patrimônio. Ex: "Computador", "Monitor", "Cadeira"
        /// </summary>
        [Required]
        [MaxLength(80)]
        public string Nome { get; set; } = string.Empty;
    }
}
