using Microsoft.AspNetCore.Identity;

namespace PatriControl.Web.Services
{
    public class PtBrIdentityErrorDescriber : IdentityErrorDescriber
    {
        public override IdentityError PasswordTooShort(int length) =>
            new() { Code = nameof(PasswordTooShort), Description = $"A senha deve ter no mínimo {length} caracteres." };

        public override IdentityError PasswordRequiresDigit() =>
            new() { Code = nameof(PasswordRequiresDigit), Description = "A senha deve conter pelo menos 1 número." };

        public override IdentityError PasswordRequiresLower() =>
            new() { Code = nameof(PasswordRequiresLower), Description = "A senha deve conter pelo menos 1 letra minúscula." };

        public override IdentityError PasswordRequiresUpper() =>
            new() { Code = nameof(PasswordRequiresUpper), Description = "A senha deve conter pelo menos 1 letra maiúscula." };

        public override IdentityError PasswordRequiresNonAlphanumeric() =>
            new() { Code = nameof(PasswordRequiresNonAlphanumeric), Description = "A senha deve conter pelo menos 1 caractere especial." };

        public override IdentityError DuplicateEmail(string email) =>
            new() { Code = nameof(DuplicateEmail), Description = "Já existe um usuário com este e-mail." };

        public override IdentityError DuplicateUserName(string userName) =>
            new() { Code = nameof(DuplicateUserName), Description = "Já existe um usuário com este login." };

        public override IdentityError InvalidEmail(string email) =>
            new() { Code = nameof(InvalidEmail), Description = "E-mail inválido." };

        public override IdentityError DefaultError() =>
            new() { Code = nameof(DefaultError), Description = "Ocorreu um erro ao processar sua solicitação." };
    }
}
