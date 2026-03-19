using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PatriControl.Web.Models
{
    public class Patrimonio
    {
        public int Id { get; set; }

        // --- Identificação ---
        public string? Numero { get; set; } // Código da plaquinha
        [Required]
        public string Descricao { get; set; } = string.Empty;

        // --- Organização / Localização (opcionais) ---
        public string? Unidade { get; set; }               // Ex: "9921 Sede"
        public string? Localizacao { get; set; }           // Ex: "TI", "Almoxarifado"
        [Required]
        public string? Tipo { get; set; }                  // Categoria
        public string? Fornecedor { get; set; }

        // --- Documentação / Financeiro (opcionais) ---
        public string? NumeroSerieNF { get; set; }         // Nº de série da nota
        public string? NumeroNF { get; set; }              // Nº da Nota Fiscal
        public DateTime? DataCompra { get; set; }
        public decimal? Valor { get; set; }

        // --- Estado ---
        public string Status { get; set; } = "Ativo";      // Ativo / Inativo / Baixado
        public string Condicao { get; set; } = "Novo";     // Novo / Semi-novo / Usado

        // --- Imagem (opcional) ---
        public string? ImagemPath { get; set; }

        // --- Auditoria ---
        public int CriadoPorId { get; set; }
        public Usuario? CriadoPor { get; set; }
        public DateTime CriadoEm { get; set; }

        // --- Histórico ---
        public ICollection<Tramite> Tramites { get; set; } = new List<Tramite>();

        // --- Fotos ---
        public ICollection<PatrimonioFoto> Fotos { get; set; } = new List<PatrimonioFoto>();
    }
}
