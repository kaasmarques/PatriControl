using Microsoft.AspNetCore.Http;
using PatriControl.Web.Data;
using PatriControl.Web.Models;
using System;
using System.Threading.Tasks;

namespace PatriControl.Web.Services
{
    public class AuditLogger : IAuditLogger
    {
        private readonly PatriControlContext _context;
        private readonly IHttpContextAccessor _http;

        public AuditLogger(PatriControlContext context, IHttpContextAccessor http)
        {
            _context = context;
            _http = http;
        }

        public async Task LogAsync(int? usuarioId, string acao, string? entidade = null, int? entidadeId = null, string? detalhes = null)
        {
            var req = _http.HttpContext?.Request;

            var log = new AuditLog
            {
                DataHora = DateTime.Now,
                UsuarioId = usuarioId,
                Acao = (acao ?? "").Trim(),
                Entidade = string.IsNullOrWhiteSpace(entidade) ? null : entidade.Trim(),
                EntidadeId = entidadeId,
                Detalhes = string.IsNullOrWhiteSpace(detalhes) ? null : detalhes.Trim(),
                Ip = req?.HttpContext?.Connection?.RemoteIpAddress?.ToString(),
                UserAgent = req?.Headers["User-Agent"].ToString()
            };

            _context.AuditLogs.Add(log);
            await _context.SaveChangesAsync();
        }
    }
}
