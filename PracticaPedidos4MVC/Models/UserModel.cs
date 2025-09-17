using System.ComponentModel.DataAnnotations;

namespace PracticaPedidos4MVC.Models
{
    public class User
    {
        [Display(Name = "Identificador Cliente")]
        public int Id { get; set; }

        [Display(Name = "Nombre de Usuario")]
        public string Nombre { get; set; }

        [Display(Name = "Correo electrónico del Usuario")]
        public string Email { get; set; }

        [Display(Name = "Contraseña de Usuario")]
        public string Password { get; set; }

        [Display(Name = "Rol de Usuario")]
        public string Rol { get; set; }
    }
}

