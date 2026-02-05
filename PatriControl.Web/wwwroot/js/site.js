// Site-wide scripts (PatriControl)

// ============================================================
// Bootstrap Modal scroll fix
//
// O PatriControl usa um layout com "main" scrollável em telas desktop.
// Em algumas resoluções (ex.: 1360x768), o Bootstrap pode não conseguir
// aplicar o scroll do modal corretamente quando o modal está renderizado
// dentro de um container com overflow.
//
// Estratégia:
// 1) Ao abrir qualquer modal, garantimos que ele esteja anexado no <body>.
//    Isso evita recorte por containers com overflow.
// ============================================================
document.addEventListener('show.bs.modal', function (event) {
  const modal = event.target;
  if (!modal) return;

  // Se o modal estiver dentro do "main"/conteúdo, move para o body.
  // (Não muda o comportamento do Bootstrap; só garante overlay/scroll.)
  if (modal.parentElement && modal.parentElement !== document.body) {
    document.body.appendChild(modal);
  }
});
