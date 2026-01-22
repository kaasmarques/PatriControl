using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace PatriControl.Web.Models
{
    public class Usuario : IdentityUser<int>
    {
        // Ex: USR001, USR002...
        public string Codigo { get; set; } = string.Empty;

        public string Nome { get; set; } = string.Empty;

        public string Sobrenome { get; set; } = string.Empty;

        //  Status do usuário no sistema (Ativo/Inativo)
        public string Status { get; set; } = "Ativo";

        //  Flag de admin (vamos sincronizar com Role "Admin")
        public bool Administrador { get; set; }

        public DateTime CriadoEm { get; set; } = DateTime.Now;
        public DateTime AtualizadoEm { get; set; } = DateTime.Now;

        //  Só para bind em telas antigas (Create/Login). NÃO vai pro banco.
        [NotMapped]
        public string Senha { get; set; } = string.Empty;

        // Navegação
        public ICollection<Patrimonio> Patrimonios { get; set; } = new List<Patrimonio>();
        public ICollection<Tramite> Tramites { get; set; } = new List<Tramite>();
    }
}
