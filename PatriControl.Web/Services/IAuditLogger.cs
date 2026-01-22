using System.Threading.Tasks;

namespace PatriControl.Web.Services
{
    public interface IAuditLogger
    {
        Task LogAsync(int? usuarioId, string acao, string? entidade = null, int? entidadeId = null, string? detalhes = null);
    }
}
