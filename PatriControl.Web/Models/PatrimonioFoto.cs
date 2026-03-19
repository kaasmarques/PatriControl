using System;
using System.ComponentModel.DataAnnotations;

namespace PatriControl.Web.Models
{
    public class PatrimonioFoto
    {
        public int Id { get; set; }

    [Required]
 public int PatrimonioId { get; set; }
 public Patrimonio? Patrimonio { get; set; }

  /// <summary>Caminho relativo dentro de wwwroot (ex: uploads/patrimonios/xxx.jpg)</summary>
        [Required]
        public string CaminhoArquivo { get; set; } = string.Empty;

 /// <summary>Nome original do arquivo enviado pelo usuário</summary>
        [MaxLength(260)]
        public string NomeOriginal { get; set; } = string.Empty;

        public DateTime CriadoEm { get; set; } = DateTime.Now;
    }
}
