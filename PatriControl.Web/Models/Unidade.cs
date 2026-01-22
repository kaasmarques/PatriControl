using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PatriControl.Web.Models
{
    public class Unidade
    {
        public int Id { get; set; }  // PK no banco

        [Required]
        [MaxLength(4)]
        public string Per { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string Nome { get; set; } = string.Empty;

        // Agora usando Localizacao (nome real da classe)
        public ICollection<Localizacao> Localizacoes { get; set; }
            = new List<Localizacao>();
    }
}
