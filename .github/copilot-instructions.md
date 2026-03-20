# PatriControl — Instruções para o GitHub Copilot

## Stack e projeto
- ASP.NET Core MVC com Razor Views (.cshtml), .NET 8, C# 12
- SQLite via Entity Framework Core
- Bootstrap 5.3 dark theme com classes customizadas (`app-card`, `app-combobox`, `app-badge-status`, etc.)
- Bootstrap Icons (`bi bi-*`)

## Regras gerais
- Sempre gerar código em português (variáveis, comentários, mensagens ao usuário)
- Preferir Razor Views (MVC) em vez de Razor Pages ou Blazor
- Modais Bootstrap com `bootstrap.Modal.getOrCreateInstance`
- Toasts via função `showToast(message, type)` já existente no Index.cshtml
- Anti-forgery token via `getAntiForgeryToken(formId)` já existente
- Comboboxes filtráveis seguem o padrão `app-combobox` + `app-combobox-list` do projeto
- Fotos: upload AJAX via `/Patrimonios/UploadFotos`, exclusão via `/Patrimonios/ExcluirFoto`
- Nunca duplicar blocos de código ou tags HTML ao sugerir alterações
- Manter indentação e estilo consistentes com os arquivos existentes
- Não excluir funcionalidades existentes, apenas adicionar ou modificar conforme necessário