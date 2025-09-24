using System.ComponentModel.DataAnnotations;

namespace PracticaPedidos4MVC.Models
{
    public class LoginViewModel
    {
        [Display(Name = "Correo electrónico")]
        [EmailAddress]
        public string? Email { get; set; }

        [Display(Name = "Nombre de usuario")]
        public string? Nombre { get; set; }

        [Required]
        [Display(Name = "Contraseña")]
        public string Password { get; set; } = "";
    }
}
