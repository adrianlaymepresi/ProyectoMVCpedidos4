using System.ComponentModel.DataAnnotations;

namespace PracticaPedidos4MVC.Models
{
    public class UserModel
    {
        [Display(Name = "Identificador Cliente")]
        public int Id { get; set; }

        [Display(Name = "Nombre de Usuario")]
        [Required, StringLength(120, MinimumLength = 5)] 
        // Li Yu (Nombre China) el mas corto posible nombre y apellido
        public string Nombre { get; set; }

        [Display(Name = "Correo electrónico del Usuario")]
        [Required, StringLength(320, MinimumLength = 7)]
        // a@z.com osea minimo 7 caracteres
        public string Email { get; set; }

        [Display(Name = "Contraseña de Usuario")]
        [Required, StringLength(64, MinimumLength = 8)]
        // para una buena contraseña segura
        public string Password { get; set; }

        [Display(Name = "Rol de Usuario")]
        [Required, StringLength(30)] 
        // sin minimo ya que los roles estaran por defecto
        // admin, cliente, empleado (como piden los requisitos)
        public string Rol { get; set; }
    }
}

