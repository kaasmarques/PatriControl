using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatriControl.Web.Data;
using PatriControl.Web.Models.ViewModels;
using System;
using System.Globalization;
using System.Linq;

namespace PatriControl.Web.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly PatriControlContext _context;

        public DashboardController(PatriControlContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Index(
            DateTime? de,
            DateTime? ate,
            string? unidade,
            string? tipo,
            string? fornecedor,
            string? status,
            string? condicao,
            int dias = 90 // default "BI": últimos 90 dias
        )
        {
            // ===== Período default =====
            var hoje = DateTime.Today;

            DateTime dataInicio;
            DateTime dataFim;

            if (de.HasValue || ate.HasValue)
            {
                dataInicio = (de ?? hoje.AddDays(-dias)).Date;
                dataFim = (ate ?? hoje).Date;
            }
            else
            {
                dataInicio = hoje.AddDays(-dias).Date;
                dataFim = hoje.Date;
            }

            if (dataFim < dataInicio)
            {
                // swap simples
                var tmp = dataInicio;
                dataInicio = dataFim;
                dataFim = tmp;
            }

            // fim do dia (inclusive)
            var fimDoDia = dataFim.Date.AddDays(1).AddTicks(-1);

            // ===== Query Patrimônios (aplica filtros gerais) =====
            var qPat = _context.Patrimonios
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(unidade))
                qPat = qPat.Where(p => (p.Unidade ?? "") == unidade.Trim());

            if (!string.IsNullOrWhiteSpace(tipo))
                qPat = qPat.Where(p => (p.Tipo ?? "") == tipo.Trim());

            if (!string.IsNullOrWhiteSpace(fornecedor))
                qPat = qPat.Where(p => (p.Fornecedor ?? "") == fornecedor.Trim());

            if (!string.IsNullOrWhiteSpace(status))
                qPat = qPat.Where(p => (p.Status ?? "") == status.Trim());

            if (!string.IsNullOrWhiteSpace(condicao))
                qPat = qPat.Where(p => (p.Condicao ?? "") == condicao.Trim());

            // ===== KPIs Patrimônios (UMA ÚNICA QUERY ao invés de 4) =====
            // statusDist já traz a distribuição completa — reutilizar para KPIs e chart
            var statusDist = qPat
                .GroupBy(p => (p.Status ?? "Sem status").Trim())
                .Select(g => new { Status = g.Key, Qtde = g.Count() })
                .ToList();

            var totalPatrimonios = statusDist.Sum(x => x.Qtde);

            // Buscar KPIs direto do statusDist já materializado (zero queries extras)
            int GetCount(string st) =>
                statusDist.FirstOrDefault(x => string.Equals(x.Status, st, StringComparison.OrdinalIgnoreCase))?.Qtde ?? 0;

            var totalAtivos = GetCount("Ativo");
            var totalBaixados = GetCount("Baixado");
            var totalEmManut = GetCount("Em manutenção");

            // ===== KPIs Valores dos Patrimônios (somar no C# — compatível com SQLite) =====
            var valoresPorStatus = qPat
                .Select(p => new { Status = (p.Status ?? "").Trim(), Valor = p.Valor ?? 0m })
                .ToList();

            var valorTotalPatrimonios = valoresPorStatus.Sum(x => x.Valor);
            var valorPatrimoniosAtivos = valoresPorStatus.Where(x => string.Equals(x.Status, "Ativo", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Valor);
            var valorPatrimoniosEmManut = valoresPorStatus.Where(x => string.Equals(x.Status, "Em manutenção", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Valor);
            var valorPatrimoniosBaixados = valoresPorStatus.Where(x => string.Equals(x.Status, "Baixado", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Valor);

            // Ordenar para o chart
            var statusDistOrdenado = statusDist.OrderByDescending(x => x.Qtde).ToList();

            // ===== Chart: Top Unidades =====
            var topUnidades = qPat
                .GroupBy(p => (p.Unidade ?? "Sem unidade").Trim())
                .Select(g => new { Nome = g.Key, Qtde = g.Count() })
                .OrderByDescending(x => x.Qtde)
                .Take(10)
                .ToList();

            // ===== Chart: Top Tipos =====
            var topTipos = qPat
                .GroupBy(p => (p.Tipo ?? "Sem tipo").Trim())
                .Select(g => new { Nome = g.Key, Qtde = g.Count() })
                .OrderByDescending(x => x.Qtde)
                .Take(10)
                .ToList();

            // ===== Recentes (Patrimônios) =====
            var recentes = qPat
                .OrderByDescending(p => p.CriadoEm)
                .Take(10)
                .ToList();

            // ===== Query Manutenções (no período) - SEM Include para contagens =====
            var qMan = _context.Manutencoes
                .AsNoTracking()
                .Where(m => m.AbertaEm >= dataInicio && m.AbertaEm <= fimDoDia);

            // aplica filtros de patrimônio também nas manutenções
            if (!string.IsNullOrWhiteSpace(unidade))
                qMan = qMan.Where(m => m.Patrimonio != null && (m.Patrimonio.Unidade ?? "") == unidade.Trim());

            if (!string.IsNullOrWhiteSpace(tipo))
                qMan = qMan.Where(m => m.Patrimonio != null && (m.Patrimonio.Tipo ?? "") == tipo.Trim());

            if (!string.IsNullOrWhiteSpace(fornecedor))
                qMan = qMan.Where(m => m.Patrimonio != null && (m.Patrimonio.Fornecedor ?? "") == fornecedor.Trim());

            // ===== KPIs Manutenções (2 counts + Sum) =====
            var manutAbertasPeriodo = qMan.Count(m => m.Status != "Finalizada" && m.Status != "Cancelada");
            var manutFinalizadasPeriodo = qMan.Count(m => m.Status == "Finalizada");

            // SQLite não suporta Sum em decimal — projetar só o campo e somar no C#
            var custoFinalTotalPeriodo = qMan
                .Where(m => m.Status == "Finalizada" && m.CustoFinal != null)
                .Select(m => m.CustoFinal)
                .ToList()
                .Sum(v => v ?? 0m);

            var ticketMedio = manutFinalizadasPeriodo > 0
                ? (custoFinalTotalPeriodo / manutFinalizadasPeriodo)
                : 0m;

            // ===== Chart: Manutenções por mês (aberturas) =====
            var manutPorMes = qMan
                .GroupBy(m => new { m.AbertaEm.Year, m.AbertaEm.Month })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    Qtde = g.Count()
                })
                .OrderBy(x => x.Year).ThenBy(x => x.Month)
                .ToList();

            // ===== Chart: Custo por mês (finalizadas) =====
            var custoPorMes = qMan
                .Where(m => m.Status == "Finalizada" && m.FinalizadaEm != null)
                .Select(m => new { m.FinalizadaEm, m.CustoFinal })
                .ToList()
                .GroupBy(x => new { x.FinalizadaEm!.Value.Year, x.FinalizadaEm!.Value.Month })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    Total = g.Sum(x => x.CustoFinal ?? 0m)
                })
                .OrderBy(x => x.Year).ThenBy(x => x.Month)
                .ToList();

            // ===== Recentes (Manutenções) — projeção direta sem Include =====
            var manutRecentes = _context.Manutencoes
                .AsNoTracking()
                .OrderByDescending(m => m.AbertaEm)
                .Take(10)
                .Select(m => new ManutencaoResumo
                {
                    Id = m.Id,
                    Codigo = m.Codigo,
                    Status = m.Status,
                    AbertaEm = m.AbertaEm,
                    FinalizadaEm = m.FinalizadaEm,
                    PatrimonioNumero = m.Patrimonio != null ? m.Patrimonio.Numero : null,
                    PatrimonioDescricao = m.Patrimonio != null ? m.Patrimonio.Descricao : null,
                    TipoPatrimonio = m.Patrimonio != null ? m.Patrimonio.Tipo : null,
                    Manutentor = m.Manutentor != null ? m.Manutentor.Nome : null,
                    CustoEstimado = m.CustoEstimado,
                    CustoFinal = m.CustoFinal
                })
                .ToList();

            // ===== Combobox datasets (distinct) =====
            ViewBag.Unidades = _context.Patrimonios.AsNoTracking()
                .Select(p => p.Unidade)
                .Where(x => x != null && x != "")
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            ViewBag.Tipos = _context.Patrimonios.AsNoTracking()
                .Select(p => p.Tipo)
                .Where(x => x != null && x != "")
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            ViewBag.Fornecedores = _context.Patrimonios.AsNoTracking()
                .Select(p => p.Fornecedor)
                .Where(x => x != null && x != "")
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            // selects pequenos (ok manter <select>)
            ViewBag.StatusLista = new[] { "", "Ativo", "Em manutenção", "Aguardando manutenção", "Emprestado", "Baixado" };
            ViewBag.CondicaoLista = new[] { "", "Novo", "Semi-Novo", "Usado" };

            // ===== Monta VM =====
            var vm = new DashboardViewModel
            {
                DataInicio = dataInicio,
                DataFim = dataFim,

                Unidade = unidade,
                Tipo = tipo,
                Fornecedor = fornecedor,
                Status = status,
                Condicao = condicao,

                TotalPatrimonios = totalPatrimonios,
                TotalAtivos = totalAtivos,
                TotalBaixados = totalBaixados,
                TotalEmManutencao = totalEmManut,

                ValorTotalPatrimonios = valorTotalPatrimonios,
                ValorPatrimoniosAtivos = valorPatrimoniosAtivos,
                ValorPatrimoniosEmManutencao = valorPatrimoniosEmManut,
                ValorPatrimoniosBaixados = valorPatrimoniosBaixados,

                ManutencoesAbertasPeriodo = manutAbertasPeriodo,
                ManutencoesFinalizadasPeriodo = manutFinalizadasPeriodo,
                CustoFinalTotalPeriodo = custoFinalTotalPeriodo,
                TicketMedioFinalizadasPeriodo = ticketMedio,

                Recentes = recentes,
                ManutencoesRecentes = manutRecentes
            };

            // charts
            vm.StatusPatrimonios.Labels = statusDistOrdenado.Select(x => x.Status).ToList();
            vm.StatusPatrimonios.Values = statusDistOrdenado.Select(x => (decimal)x.Qtde).ToList();

            vm.TopUnidades.Labels = topUnidades.Select(x => x.Nome).ToList();
            vm.TopUnidades.Values = topUnidades.Select(x => (decimal)x.Qtde).ToList();

            vm.TopTipos.Labels = topTipos.Select(x => x.Nome).ToList();
            vm.TopTipos.Values = topTipos.Select(x => (decimal)x.Qtde).ToList();

            vm.ManutencoesPorMes.Labels = manutPorMes
                .Select(x => $"{x.Month:00}/{x.Year}")
                .ToList();
            vm.ManutencoesPorMes.Values = manutPorMes
                .Select(x => (decimal)x.Qtde)
                .ToList();

            vm.CustoPorMes.Labels = custoPorMes
                .Select(x => $"{x.Month:00}/{x.Year}")
                .ToList();
            vm.CustoPorMes.Values = custoPorMes
                .Select(x => x.Total)
                .ToList();

            return View(vm);
        }
    }
}
